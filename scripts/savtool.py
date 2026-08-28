#!/usr/bin/env python3
"""Convert between Anacreon .SAV (binary, format v13) and JSON.

Layout reference: docs/SAV_FILE_FORMAT.md (every field here corresponds to a row there).

Enum/set fields are kept as raw integers/bit-lists (matching the on-disk ordinal), not
symbolic names -- see the doc's "Enum reference" section to interpret them. Pointer and
reserved byte regions round-trip as hex strings; they are never meaningful, only preserved.
A hex field is omitted from the JSON entirely when it's all-zero (the common case for a
hand-built or freshly-initialized record) -- a missing key writes back as zero-fill, so this
is lossless. Fixed-width strings work the same way: a string with an all-zero tail (see
"Strings" below) is emitted as a plain JSON string instead of {text, tail_hex}.

The sector grid is emitted sparsely as {sizeOfGalaxy, default, cells}: `default` holds the
most-common value per field across the whole galaxy (almost always "empty"), and `cells`
lists only the (x, y) cells that differ, and only the fields that differ on each. A real
galaxy is 95%+ boring cells, so this cuts sector JSON size by an order of magnitude.

Usage:
    savtool.py to-json input.SAV output.json
    savtool.py to-sav input.json output.SAV
    savtool.py check input.SAV      # round-trip self-test: SAV -> JSON -> SAV, byte-diff
"""
import argparse
import json
import struct
import sys

SIGNATURE = b"Anacreon save file v1.3\r\n\x1a      "
VERSION = 13

MAX_NO_OF_STARBASES = 100
MAX_NO_OF_FLEETS = 240
MAX_NO_OF_STARGATES = 50
MAX_NO_OF_CONSTR_SITES = 50
NO_OF_FLEETS_PER_EMPIRE = 30
MAX_NO_OF_BLOCKS = 20  # MaxSizeOfGalaxy(100) div 5


# ---- cursor helpers ----------------------------------------------------------------

class Reader:
    def __init__(self, data):
        self.data = data
        self.pos = 0

    def bytes(self, n):
        b = self.data[self.pos:self.pos + n]
        if len(b) != n:
            raise ValueError(f"unexpected EOF at offset {self.pos}, wanted {n} bytes")
        self.pos += n
        return b

    def u8(self):
        return self.bytes(1)[0]

    def u16(self):
        return struct.unpack_from("<H", self.bytes(2))[0]

    def i16(self):
        return struct.unpack_from("<h", self.bytes(2))[0]

    def i32(self):
        return struct.unpack_from("<l", self.bytes(4))[0]

    def hex(self, n):
        """Opaque byte region (pointer/reserved/legacy field). `None` when all-zero --
        `Writer.hex` already zero-fills a missing value, so omitting it here is lossless
        and cuts the common case (a freshly-initialized or hand-built record) from the JSON."""
        b = self.bytes(n)
        return b.hex() if any(b) else None

    def remaining(self):
        return len(self.data) - self.pos


class Writer:
    def __init__(self):
        self.buf = bytearray()

    def raw(self, b):
        self.buf += b

    def u8(self, v):
        self.buf.append(v & 0xFF)

    def u16(self, v):
        self.buf += struct.pack("<H", v)

    def i16(self, v):
        self.buf += struct.pack("<h", v)

    def i32(self, v):
        self.buf += struct.pack("<l", v)

    def hex(self, h, n):
        b = bytes.fromhex(h) if h else b""
        if len(b) != n:
            b = (b + bytes(n))[:n]
        self.buf += b


# ---- shared field codecs (Pascal type -> JSON) --------------------------------------
# See docs/SAV_FILE_FORMAT.md "Conventions" for why these sizes are what they are.

def read_string(r, width):
    """STRING[width-1]: 1 length byte + (width-1) char slots, whole buffer consumed.

    Bytes past the logical length are leftover heap contents in Turbo Pascal, not zeroed --
    confirmed non-zero in a real played save (INTRO_2.SAV's NameRecord.Name tails). When the
    tail is all-zero (the common case: freshly-initialized or hand-built records) this returns
    the plain decoded string; only a genuinely non-zero tail promotes it to `{text, tail_hex}`
    so an unmodified real save still round-trips byte-exact.
    """
    raw = r.bytes(width)
    n = raw[0]
    text = raw[1:1 + n].decode("cp437", errors="replace")
    tail = raw[1 + n:]
    if any(tail):
        return {"text": text, "tail_hex": tail.hex()}
    return text


def write_string(w, s, width):
    if isinstance(s, dict):
        text, tail_hex = s.get("text", ""), s.get("tail_hex")
    else:
        text, tail_hex = s, None
    body = text.encode("cp437", errors="replace")[:width - 1]
    w.u8(len(body))
    w.raw(body)
    pad_len = width - 1 - len(body)
    tail = bytes.fromhex(tail_hex) if tail_hex else b""
    if len(tail) != pad_len:
        tail = (tail + bytes(pad_len))[:pad_len]
    w.raw(tail)


def read_set(r, nbytes):
    """SET OF T, bit i (LSB first, byte 0 first) = ordinal i present."""
    raw = r.bytes(nbytes)
    bits = int.from_bytes(raw, "little")
    return [i for i in range(nbytes * 8) if bits & (1 << i)]


def write_set(w, members, nbytes):
    bits = 0
    for m in members:
        bits |= 1 << m
    w.raw(bits.to_bytes(nbytes, "little"))


def read_xy(r):
    return {"x": r.u8(), "y": r.u8()}


def write_xy(w, xy):
    w.u8(xy["x"])
    w.u8(xy["y"])


def read_id(r):
    return {"objType": r.u8(), "index": r.u8()}


def write_id(w, idn):
    w.u8(idn["objType"])
    w.u8(idn["index"])


def read_location(r):
    return {"xy": read_xy(r), "id": read_id(r)}


def write_location(w, loc):
    write_xy(w, loc["xy"])
    write_id(w, loc["id"])


def _mode(values):
    """Most-common value in `values` (a list of hashable keys). Used to pick the sector
    default per field -- Turbo Pascal's galaxy init doesn't zero-fill (e.g. `Special`'s
    "no mine" sentinel is 0x80, not 0), so "most common" is the only safe default, not "zero"."""
    from collections import Counter
    return Counter(values).most_common(1)[0][0]


def read_sector(r):
    """SectorRecord grid, densely stored on disk (`GALAXY.PAS:75-105`) but re-emitted here
    as {sizeOfGalaxy, default, cells}: `default` is the most common value per field (almost
    always "empty"), `cells` lists only the (x, y) cells that differ, and only the fields
    that differ. A real galaxy is 95%+ boring cells, so this cuts JSON size by an order of
    magnitude and makes "add a fleet at (x, y)" a one-line edit instead of a grid hunt."""
    size_x = r.u16()
    r.u16()  # SizeOfGalaxy written twice (GALAXY.PAS:81-82); both copies are always equal
    cells = {}
    for x in range(size_x + 1):
        for y in range(size_x + 1):
            cells[(x, y)] = {
                "obj": read_id(r), "flts": read_set(r, 1),
                "mineScout": read_set(r, 1), "special": r.u8(),
            }
    default = {
        "obj": dict(zip(("objType", "index"), _mode((c["obj"]["objType"], c["obj"]["index"]) for c in cells.values()))),
        "flts": list(_mode(tuple(c["flts"]) for c in cells.values())),
        "mineScout": list(_mode(tuple(c["mineScout"]) for c in cells.values())),
        "special": _mode(c["special"] for c in cells.values()),
    }
    overrides = []
    for x in range(size_x + 1):
        for y in range(size_x + 1):
            cell = cells[(x, y)]
            diff = {k: v for k, v in cell.items() if v != default[k]}
            if diff:
                overrides.append({"x": x, "y": y, **diff})
    return {"sizeOfGalaxy": size_x, "default": default, "cells": overrides}


def write_sector(w, sector):
    size = sector["sizeOfGalaxy"]
    w.u16(size); w.u16(size)
    default = sector["default"]
    overrides = {(o["x"], o["y"]): o for o in sector["cells"]}
    for x in range(size + 1):
        for y in range(size + 1):
            o = overrides.get((x, y), {})
            write_id(w, o.get("obj", default["obj"]))
            write_set(w, o.get("flts", default["flts"]), 1)
            write_set(w, o.get("mineScout", default["mineScout"]), 1)
            w.u8(o.get("special", default["special"]))


def read_word_array(r, n):
    return [r.u16() for _ in range(n)]


def write_word_array(w, arr, n):
    for i in range(n):
        w.u16(arr[i] if i < len(arr) else 0)


# ---- Planets / Starbases / Fleets / Stargates / Constructions (DATASTRC.PAS) --------

def read_planet(r):
    return {
        "xy": read_xy(r), "emp": r.u8(),
        "scoutedBy": read_set(r, 1), "knownBy": read_set(r, 1),
        "cls": r.u8(), "typ": r.u8(), "impExp": r.i16(), "tech": r.u8(),
        "eff": r.u8(), "revIndex": r.u8(), "special": read_set(r, 1),
        "pop": r.u16(),
        "ships": read_word_array(r, 7), "cargo": read_word_array(r, 7),
        "defns": read_word_array(r, 4), "indus": read_word_array(r, 9),
        "triReserve": r.u16(),
        "reserved_hex": r.hex(16),
        "nextId": read_id(r),
    }


def write_planet(w, p):
    write_xy(w, p["xy"]); w.u8(p["emp"])
    write_set(w, p["scoutedBy"], 1); write_set(w, p["knownBy"], 1)
    w.u8(p["cls"]); w.u8(p["typ"]); w.i16(p["impExp"]); w.u8(p["tech"])
    w.u8(p["eff"]); w.u8(p["revIndex"]); write_set(w, p["special"], 1)
    w.u16(p["pop"])
    write_word_array(w, p["ships"], 7); write_word_array(w, p["cargo"], 7)
    write_word_array(w, p["defns"], 4); write_word_array(w, p["indus"], 9)
    w.u16(p["triReserve"])
    w.hex(p["reserved_hex"], 16)
    write_id(w, p["nextId"])


def read_starbase(r):
    return {
        "xy": read_xy(r), "emp": r.u8(),
        "scoutedBy": read_set(r, 1), "knownBy": read_set(r, 1),
        "styp": r.u8(), "typ": r.u8(), "tech": r.u8(),
        "eff": r.u8(), "revIndex": r.u8(), "special": read_set(r, 1),
        "pop": r.u16(),
        "ships": read_word_array(r, 7), "cargo": read_word_array(r, 7),
        "defns": read_word_array(r, 4), "indus": read_word_array(r, 9),
        "move": r.u8(), "dest": read_xy(r), "status": r.u8(),
        "reserved_hex": r.hex(18),
        "nextId": read_id(r),
    }


def write_starbase(w, s):
    write_xy(w, s["xy"]); w.u8(s["emp"])
    write_set(w, s["scoutedBy"], 1); write_set(w, s["knownBy"], 1)
    w.u8(s["styp"]); w.u8(s["typ"]); w.u8(s["tech"])
    w.u8(s["eff"]); w.u8(s["revIndex"]); write_set(w, s["special"], 1)
    w.u16(s["pop"])
    write_word_array(w, s["ships"], 7); write_word_array(w, s["cargo"], 7)
    write_word_array(w, s["defns"], 4); write_word_array(w, s["indus"], 9)
    w.u8(s["move"]); write_xy(w, s["dest"]); w.u8(s["status"])
    w.hex(s["reserved_hex"], 18)
    write_id(w, s["nextId"])


def read_fleet_record(r):
    return {
        "xy": read_xy(r), "emp": r.u8(), "scoutedBy": read_set(r, 1),
        "ships": read_word_array(r, 7), "cargo": read_word_array(r, 7),
        "dest": read_xy(r), "status": r.u8(),
        "fuelHigh": r.u8(), "fuel": r.i16(),
        "knownBy": read_set(r, 1),
        "nextOrder": r.u8(), "orderData_hex": r.hex(6),
        "npeDataIndex": r.u8(),
        "reserved_hex": r.hex(8),
        "nextId": read_id(r),
    }


def write_fleet_record(w, f):
    write_xy(w, f["xy"]); w.u8(f["emp"]); write_set(w, f["scoutedBy"], 1)
    write_word_array(w, f["ships"], 7); write_word_array(w, f["cargo"], 7)
    write_xy(w, f["dest"]); w.u8(f["status"])
    w.u8(f["fuelHigh"]); w.i16(f["fuel"])
    write_set(w, f["knownBy"], 1)
    w.u8(f["nextOrder"]); w.hex(f["orderData_hex"], 6)
    w.u8(f["npeDataIndex"])
    w.hex(f["reserved_hex"], 8)
    write_id(w, f["nextId"])


def read_command(r):
    typ = r.u8()
    variant_hex = r.hex(4)
    return {"typ": typ, "variant_hex": variant_hex}


def write_command(w, c):
    w.u8(c["typ"])
    w.hex(c["variant_hex"], 4)


def read_stargate(r):
    return {
        "xy": read_xy(r), "emp": r.u8(),
        "scoutedBy": read_set(r, 1), "knownBy": read_set(r, 1),
        "gtyp": r.u8(), "dest": read_xy(r), "nextId": read_id(r),
    }


def write_stargate(w, g):
    write_xy(w, g["xy"]); w.u8(g["emp"])
    write_set(w, g["scoutedBy"], 1); write_set(w, g["knownBy"], 1)
    w.u8(g["gtyp"]); write_xy(w, g["dest"]); write_id(w, g["nextId"])


def read_constr(r):
    return {
        "xy": read_xy(r), "emp": r.u8(),
        "scoutedBy": read_set(r, 1), "knownBy": read_set(r, 1),
        "ctyp": r.u8(), "timeToCompletion": r.u8(), "nextId": read_id(r),
    }


def write_constr(w, c):
    write_xy(w, c["xy"]); w.u8(c["emp"])
    write_set(w, c["scoutedBy"], 1); write_set(w, c["knownBy"], 1)
    w.u8(c["ctyp"]); w.u8(c["timeToCompletion"]); write_id(w, c["nextId"])


# ---- indexed sparse/dense section helper --------------------------------------------

def read_indexed_section(r, record_reader):
    items = []
    idx = r.u16()
    while idx != 0:
        rec = record_reader(r)
        items.append({"index": idx, "record": rec})
        idx = r.u16()
    return items


def write_indexed_section(w, items, record_writer):
    for item in items:
        w.u16(item["index"])
        record_writer(w, item["record"])
    w.u16(0)


# ---- Empire Data (DATASTRC.PAS:177-204) ---------------------------------------------

def read_defense_record(r):
    # DefenseDistributionArray = ARRAY[ShellPos(5), ShipTypes(7)] OF Index(byte)
    return {
        "shellDefDist": [r.u8() for _ in range(35)],
        "starbaseDefDist": [r.u8() for _ in range(35)],
    }


def write_defense_record(w, d):
    for v in d["shellDefDist"]:
        w.u8(v)
    for v in d["starbaseDefDist"]:
        w.u8(v)


def read_probe_array(r):
    return [{"dest": read_xy(r), "status": r.u8()} for _ in range(10)]


def write_probe_array(w, arr):
    for p in arr:
        write_xy(w, p["dest"]); w.u8(p["status"])


def read_empire_data(r):
    return {
        "inUse": bool(r.u8()), "isAPlayer": bool(r.u8()),
        "empireName": read_string(r, 33), "pass": read_string(r, 9),
        "timeLeft": r.i16(), "capital": read_id(r),
        "defenseSettings": read_defense_record(r),
        "probe": read_probe_array(r),
        "namesPtr_hex": r.hex(4), "lastNamePtr_hex": r.hex(4),
        "totalRevIndex": r.i16(),
        "technologyLevel": r.u8(), "technology": read_set(r, 4),
        "isAnEmpress": bool(r.u8()), "revFactor": r.i16(),
        "founding": r.u16(), "modifiers": read_set(r, 1),
        "reserved_hex": r.hex(14),
    }


def write_empire_data(w, e):
    w.u8(1 if e["inUse"] else 0); w.u8(1 if e["isAPlayer"] else 0)
    write_string(w, e["empireName"], 33); write_string(w, e["pass"], 9)
    w.i16(e["timeLeft"]); write_id(w, e["capital"])
    write_defense_record(w, e["defenseSettings"])
    write_probe_array(w, e["probe"])
    w.hex(e["namesPtr_hex"], 4); w.hex(e["lastNamePtr_hex"], 4)
    w.i16(e["totalRevIndex"])
    w.u8(e["technologyLevel"]); write_set(w, e["technology"], 4)
    w.u8(1 if e["isAnEmpress"] else 0); w.i16(e["revFactor"])
    w.u16(e["founding"]); write_set(w, e["modifiers"], 1)
    w.hex(e["reserved_hex"], 14)


def read_name_record(r):
    name = read_string(r, 9)
    coord = read_location(r)
    nxt = r.hex(4)
    return {"name": name, "coord": coord, "next_hex": nxt}


def write_name_record(w, n):
    write_string(w, n["name"], 9)
    write_location(w, n["coord"])
    w.hex(n["next_hex"], 4)


# ---- News (NEWS.PAS:113-122) ---------------------------------------------------------

def read_news_record(r):
    return {
        "headline": r.u8(), "loc1": read_location(r),
        "parm1": r.i16(), "parm2": r.i16(), "parm3": r.i16(),
        "next_hex": r.hex(4),
    }


def write_news_record(w, n):
    w.u8(n["headline"]); write_location(w, n["loc1"])
    w.i16(n["parm1"]); w.i16(n["parm2"]); w.i16(n["parm3"])
    w.hex(n["next_hex"], 4)


# ---- Messages (MESS.PAS:23-33) --------------------------------------------------------

def read_message(r):
    sender = r.u8()
    recipient = read_set(r, 1)
    read_by = read_set(r, 1)
    is_read = bool(r.u8())
    intercepted = bool(r.u8())
    no_of_lines_field = r.u16()  # MesText.NoOfLines -- redundant with the byte that follows
    first_line_hex = r.hex(4)
    last_line_hex = r.hex(4)
    next_hex = r.hex(4)
    prev_hex = r.hex(4)
    no_of_lines = r.u8()
    lines = [read_string(r, 81) for _ in range(no_of_lines)]
    return {
        "sender": sender, "recipient": recipient, "readBy": read_by,
        "read": is_read, "intercepted": intercepted,
        "mesTextNoOfLines_redundant": no_of_lines_field,
        "firstLinePtr_hex": first_line_hex, "lastLinePtr_hex": last_line_hex,
        "nextPtr_hex": next_hex, "prevPtr_hex": prev_hex,
        "lines": lines,
    }


def write_message(w, m):
    w.u8(m["sender"])
    write_set(w, m["recipient"], 1); write_set(w, m["readBy"], 1)
    w.u8(1 if m["read"] else 0); w.u8(1 if m["intercepted"] else 0)
    w.u16(m["mesTextNoOfLines_redundant"])
    w.hex(m["firstLinePtr_hex"], 4); w.hex(m["lastLinePtr_hex"], 4)
    w.hex(m["nextPtr_hex"], 4); w.hex(m["prevPtr_hex"], 4)
    w.u8(len(m["lines"]))
    for line in m["lines"]:
        write_string(w, line, 81)


# ---- NPE data (NPETYPES.PAS) ----------------------------------------------------------

def read_fleet_data_array(r, n):
    out = []
    for _ in range(n):
        out.append({
            "mission": r.u8(), "targetId": read_id(r), "homeBaseId": read_id(r),
            "midway": read_id(r), "waiting": r.u8(), "blockX": r.u8(),
            "blockY": r.u8(), "index": r.u8(),
        })
    return out


def write_fleet_data_array(w, arr):
    for f in arr:
        w.u8(f["mission"]); write_id(w, f["targetId"]); write_id(w, f["homeBaseId"])
        write_id(w, f["midway"]); w.u8(f["waiting"]); w.u8(f["blockX"])
        w.u8(f["blockY"]); w.u8(f["index"])


def read_pirate_data(r):
    fleet_data = read_fleet_data_array(r, NO_OF_FLEETS_PER_EMPIRE)
    hunting_ground = [r.u8() for _ in range(MAX_NO_OF_BLOCKS * MAX_NO_OF_BLOCKS)]
    sheep = [r.u8() for _ in range(9)]
    return {"fleetData": fleet_data, "huntingGround": hunting_ground, "sheep": sheep}


def write_pirate_data(w, d):
    write_fleet_data_array(w, d["fleetData"])
    for v in d["huntingGround"]:
        w.u8(v)
    for v in d["sheep"]:
        w.u8(v)


def read_state_dept_array(r):
    out = []
    for _ in range(9):
        out.append({
            "policy": r.u8(), "attackChance": r.u8(), "totalMilitary": r.i32(),
            "worlds": r.u16(), "threatAssess": r.u8(), "aggressiveness": r.u8(),
            "balance": r.i16(),
        })
    return out


def write_state_dept_array(w, arr):
    for s in arr:
        w.u8(s["policy"]); w.u8(s["attackChance"]); w.i32(s["totalMilitary"])
        w.u16(s["worlds"]); w.u8(s["threatAssess"]); w.u8(s["aggressiveness"])
        w.i16(s["balance"])


def read_persona(r):
    keys = ["impGene", "defGene", "offGene", "factorGene", "randomGene", "defensive",
            "offensive", "techno", "provoke", "imperialist", "worldPower", "honorable",
            "sphereX"]
    vals = {k: r.u8() for k in keys}
    vals["clock"] = r.u16()
    vals["offset"] = r.u8()
    return vals


def write_persona(w, p):
    keys = ["impGene", "defGene", "offGene", "factorGene", "randomGene", "defensive",
            "offensive", "techno", "provoke", "imperialist", "worldPower", "honorable",
            "sphereX"]
    for k in keys:
        w.u8(p[k])
    w.u16(p["clock"])
    w.u8(p["offset"])


def read_kingdom_data(r):
    return {
        "fleetData": read_fleet_data_array(r, NO_OF_FLEETS_PER_EMPIRE),
        "state": read_state_dept_array(r),
        "persona": read_persona(r),
    }


def write_kingdom_data(w, d):
    write_fleet_data_array(w, d["fleetData"])
    write_state_dept_array(w, d["state"])
    write_persona(w, d["persona"])


def read_base_data_array(r):
    out = []
    for _ in range(MAX_NO_OF_STARBASES):
        out.append({"mission": r.u8(), "targetId": read_id(r), "count": r.u16()})
    return out


def write_base_data_array(w, arr):
    for b in arr:
        w.u8(b["mission"]); write_id(w, b["targetId"]); w.u16(b["count"])


def read_berserker_data(r):
    return {
        "fleetData": read_fleet_data_array(r, NO_OF_FLEETS_PER_EMPIRE),
        "baseData": read_base_data_array(r),
        "spare": [r.u16() for _ in range(50)],
    }


def write_berserker_data(w, d):
    write_fleet_data_array(w, d["fleetData"])
    write_base_data_array(w, d["baseData"])
    for v in d["spare"]:
        w.u16(v)


def read_guardian_data(r):
    return {
        "fleetData": read_fleet_data_array(r, NO_OF_FLEETS_PER_EMPIRE),
        "spare": [r.u16() for _ in range(50)],
    }


def write_guardian_data(w, d):
    write_fleet_data_array(w, d["fleetData"])
    for v in d["spare"]:
        w.u16(v)


NPE_READERS = {
    1: ("pirate", read_pirate_data),    # PirateNPE
    2: ("kingdom", read_kingdom_data),  # Kingdom1NPE
    3: ("kingdom", read_kingdom_data),  # Kingdom2NPE
    4: ("berserker", read_berserker_data),
    5: ("guardian", read_guardian_data),
    6: ("pirate", read_pirate_data),    # TraderNPE falls back to pirate layout, NPE.PAS:108-109
}
NPE_WRITERS = {
    "pirate": write_pirate_data, "kingdom": write_kingdom_data,
    "berserker": write_berserker_data, "guardian": write_guardian_data,
}


# ---- top-level parse / build ----------------------------------------------------------

def parse_sav(data):
    r = Reader(data)
    sig = r.bytes(32)
    version = r.u16()
    if version != VERSION:
        raise ValueError(f"unsupported save format version {version} (only {VERSION} is documented)")

    env = {
        "year": r.u16(), "player": r.u8(), "empiresToMove": read_set(r, 2),
        "scenaFilename": read_string(r, 17), "timePerTurn": r.u16(),
        "autoSave": bool(r.u8()), "asyncTurns": bool(r.u8()),
        "pauseActive": bool(r.u8()), "reEnterGame": bool(r.u8()),
    }

    sector = read_sector(r)

    planets = read_indexed_section(r, read_planet)
    starbases = read_indexed_section(r, read_starbase)

    fleets = []
    idx = r.u16()
    while idx != 0:
        rec = read_fleet_record(r)
        order_count = r.u16()
        orders = [read_command(r) for _ in range(order_count)]
        fleets.append({"index": idx, "record": rec, "orders": orders})
        idx = r.u16()

    stargates = read_indexed_section(r, read_stargate)
    constructions = read_indexed_section(r, read_constr)

    no_of_messages = r.u8()
    messages = [read_message(r) for _ in range(no_of_messages)]

    empires = []
    for _ in range(8):
        rec = read_empire_data(r)
        no_of_names = r.u8()
        names = [read_name_record(r) for _ in range(no_of_names)]
        empires.append({"record": rec, "names": names})

    news = []
    for _ in range(8):
        n = r.u16()
        news.append([read_news_record(r) for _ in range(n)])

    npe_types = []
    for _ in range(9):
        npe_types.append({"typ": r.u8(), "data_hex": r.hex(4)})

    npe_blobs = {}
    for i in range(8):
        emp = empires[i]["record"]
        if not emp["inUse"] or emp["isAPlayer"]:
            continue
        typ = npe_types[i]["typ"]
        kind, reader = NPE_READERS.get(typ, ("pirate", read_pirate_data))
        npe_blobs[str(i)] = {"kind": kind, "data": reader(r)}

    if r.remaining() != 0:
        raise ValueError(f"{r.remaining()} trailing bytes after parsing (format mismatch)")

    return {
        "signature": sig.decode("cp437"), "version": version,
        "environment": env, "sector": sector,
        "planets": planets, "starbases": starbases, "fleets": fleets,
        "stargates": stargates, "constructions": constructions,
        "messages": messages, "empires": empires, "news": news,
        "npe": {"types": npe_types, "blobs": npe_blobs},
    }


def build_sav(doc):
    w = Writer()
    w.raw(SIGNATURE)
    w.u16(doc["version"])

    env = doc["environment"]
    w.u16(env["year"]); w.u8(env["player"]); write_set(w, env["empiresToMove"], 2)
    write_string(w, env["scenaFilename"], 17); w.u16(env["timePerTurn"])
    w.u8(1 if env["autoSave"] else 0); w.u8(1 if env["asyncTurns"] else 0)
    w.u8(1 if env["pauseActive"] else 0); w.u8(1 if env["reEnterGame"] else 0)

    write_sector(w, doc["sector"])

    write_indexed_section(w, doc["planets"], write_planet)
    write_indexed_section(w, doc["starbases"], write_starbase)

    for f in doc["fleets"]:
        w.u16(f["index"])
        write_fleet_record(w, f["record"])
        w.u16(len(f["orders"]))
        for c in f["orders"]:
            write_command(w, c)
    w.u16(0)

    write_indexed_section(w, doc["stargates"], write_stargate)
    write_indexed_section(w, doc["constructions"], write_constr)

    w.u8(len(doc["messages"]))
    for m in doc["messages"]:
        write_message(w, m)

    for e in doc["empires"]:
        write_empire_data(w, e["record"])
        w.u8(len(e["names"]))
        for n in e["names"]:
            write_name_record(w, n)

    for empire_news in doc["news"]:
        w.u16(len(empire_news))
        for item in empire_news:
            write_news_record(w, item)

    for t in doc["npe"]["types"]:
        w.u8(t["typ"]); w.hex(t["data_hex"], 4)

    for i in range(8):
        key = str(i)
        if key not in doc["npe"]["blobs"]:
            continue
        blob = doc["npe"]["blobs"][key]
        NPE_WRITERS[blob["kind"]](w, blob["data"])

    return bytes(w.buf)


# ---- CLI --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("to-json", help="convert a .SAV file to JSON")
    p.add_argument("sav_in")
    p.add_argument("json_out")

    p = sub.add_parser("to-sav", help="convert a JSON file to .SAV")
    p.add_argument("json_in")
    p.add_argument("sav_out")

    p = sub.add_parser("check", help="round-trip a .SAV through JSON and diff the bytes")
    p.add_argument("sav_in")

    args = ap.parse_args()

    if args.cmd == "to-json":
        with open(args.sav_in, "rb") as f:
            data = f.read()
        doc = parse_sav(data)
        with open(args.json_out, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2)
        print(f"wrote {args.json_out}")

    elif args.cmd == "to-sav":
        with open(args.json_in, "r", encoding="utf-8") as f:
            doc = json.load(f)
        data = build_sav(doc)
        with open(args.sav_out, "wb") as f:
            f.write(data)
        print(f"wrote {args.sav_out} ({len(data)} bytes)")

    elif args.cmd == "check":
        with open(args.sav_in, "rb") as f:
            original = f.read()
        doc = parse_sav(original)
        rebuilt = build_sav(doc)
        if rebuilt == original:
            print(f"OK: byte-identical round trip ({len(original)} bytes)")
        else:
            print(f"MISMATCH: original {len(original)} bytes, rebuilt {len(rebuilt)} bytes")
            n = min(len(original), len(rebuilt))
            diffs = 0
            for i in range(n):
                if original[i] != rebuilt[i]:
                    print(f"  first diff at offset {i}: original={original[i]:02x} rebuilt={rebuilt[i]:02x}")
                    diffs += 1
                    if diffs >= 10:
                        print("  ...")
                        break
            sys.exit(1)


if __name__ == "__main__":
    main()
