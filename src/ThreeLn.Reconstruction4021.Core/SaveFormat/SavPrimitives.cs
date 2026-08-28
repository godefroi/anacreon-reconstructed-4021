using System.Buffers.Binary;
using System.Text;

namespace ThreeLn.Reconstruction4021.Core.SaveFormat;

/// <summary>
/// Raw on-disk <c>ObjectTypes</c> ordinal (TYPES.PAS:128, <c>docs/SAV_FILE_FORMAT.md</c>'s
/// ObjectTypes reference) — a parsing-boundary-only type, not a domain type: this port already has
/// no C# equivalent (object identity/kind is expressed via which concrete <see
/// cref="Entities.ISectorObject"/> a reference points to, not a stored tag), matching how
/// <c>ScenarioLoader</c> keeps its own raw-ordinal decode tables private to the parsing boundary
/// rather than mirroring every Pascal enum permanently.
/// </summary>
public enum SavObjectType : byte
{
    Void = 0,
    Con = 1,
    Pln = 2,
    Base = 3,
    Gate = 4,
    BlkHl = 5,
    Plsr = 6,
    WrmHl = 7,
    Flt = 8,
    DestFlt = 9,
    Wndr = 10,
    ArtObj = 11,
}

/// <summary>
/// <c>IDNumber</c> (TYPES.PAS:133-136) — a 2-byte object reference: type tag + index into that
/// type's array. <see cref="Index"/> of <c>0</c> conventionally means "no object" regardless of
/// <see cref="ObjectType"/>.
/// </summary>
public readonly record struct SavIdNumber(SavObjectType ObjectType, byte Index)
{
    public bool IsEmpty => Index == 0;
}

/// <summary>
/// Sequential little-endian reader over a `.SAV` file's bytes, matching the primitive shapes
/// <c>docs/SAV_FILE_FORMAT.md</c>'s Conventions section documents: fixed-width ints in the exact
/// declared byte size (Turbo Pascal picks the smallest type that fits a declared range, so this is
/// <em>not</em> always the type's C#-idiomatic width — <c>XYCoord</c> is 2 bytes, not 4), length
/// -prefixed <c>STRING[N]</c> buffers, and bitset-shaped <c>SET OF T</c> values. Read-only: this
/// port's save/load starts as `.SAV` import (see `docs/ROADMAP.md` Phase 7's scope note) —
/// write-side primitives land alongside the minimal `.SAV` write-back tool.
///
/// Deliberately does not preserve opaque bytes (pointer fields, `Reserved` arrays, unused string
/// buffer tails) the way `scripts/savtool.py`/`.ps1` do — those tools exist for a byte-identical
/// round trip; this reader feeds a live <see cref="Game.cs"/> object graph that has nowhere to put
/// garbage bytes and no need to, since this phase's `.SAV` write-back is a correctness-verification
/// tool, not a byte-faithful save format (see `docs/ROADMAP.md` Phase 7's scope note).
/// </summary>
public sealed class SavReader(byte[] data)
{
    private int _position;

    public int Position => _position;
    public int Length => data.Length;
    public bool AtEnd => _position >= data.Length;

    public byte ReadByte() => data[_position++];

    public bool ReadBoolean() => ReadByte() != 0;

    /// Turbo Pascal `Word` — unsigned 16-bit.
    public ushort ReadWord()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_position, 2));
        _position += 2;
        return value;
    }

    /// Turbo Pascal `Integer` — signed 16-bit (not 32; that's `LongInt`).
    public short ReadInteger()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(_position, 2));
        _position += 2;
        return value;
    }

    /// Turbo Pascal `LongInt` — signed 32-bit.
    public int ReadLongInt()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_position, 4));
        _position += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        var slice = data.AsSpan(_position, count).ToArray();
        _position += count;
        return slice;
    }

    public void Skip(int count) => _position += count;

    /// <summary>
    /// `STRING[bufferSize]`: 1 length byte + `bufferSize` character slots, only the first `Length`
    /// meaningful (see [Strings] in the format doc — the rest is unreliable, sometimes non-zero,
    /// heap/stack leftover this port has no use for and does not preserve).
    /// </summary>
    public string ReadPascalString(int bufferSize)
    {
        var length = ReadByte();
        var chars = ReadBytes(bufferSize);
        return Encoding.ASCII.GetString(chars, 0, Math.Min(length, bufferSize));
    }

    /// The header signature — the one field written without a length byte (`LOADSAVE.PAS:84`,
    /// `WriteVariable(SF, Sign[1], N)`).
    public string ReadRawString(int length) => Encoding.ASCII.GetString(ReadBytes(length));

    /// `XYCoord` (GALAXY.PAS:22-24) — 2 bytes, `0..MaxSizeOfGalaxy` fits a `Byte` each. Already the
    /// same 0-based range this port's `Coordinate` uses, unlike `.SCN`'s 1-based literals — no
    /// offset conversion needed here, unlike `ScenarioLoader.GetNextXY`.
    public Galaxy.Coordinate ReadCoordinate()
    {
        var x = ReadByte();
        var y = ReadByte();
        return new Galaxy.Coordinate(x, y);
    }

    public SavIdNumber ReadIdNumber() => new((SavObjectType)ReadByte(), ReadByte());

    /// <summary>
    /// `SET OF T` — `ceil(byteCount*8/8)` bytes, bit 0 = lowest ordinal of `T`'s declared range (see
    /// [Sets] in the format doc). Returns the set bits as raw ordinals; callers map those to
    /// whatever enum/slot-table the specific set's base type needs (e.g. `ScoutSet`'s ordinals are
    /// empire slot indices, resolved via the same empire-slot table Empire Data itself builds).
    /// </summary>
    public HashSet<int> ReadBitSet(int byteCount)
    {
        var set = new HashSet<int>();

        for (var byteIndex = 0; byteIndex < byteCount; byteIndex++) {
            var b = ReadByte();
            for (var bit = 0; bit < 8; bit++) {
                if ((b & (1 << bit)) != 0) {
                    set.Add(byteIndex * 8 + bit);
                }
            }
        }

        return set;
    }
}
