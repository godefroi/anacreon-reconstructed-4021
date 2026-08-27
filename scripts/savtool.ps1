<#
.SYNOPSIS
    Convert between Anacreon .SAV (binary, format v13) and JSON.

.DESCRIPTION
    Layout reference: docs/SAV_FILE_FORMAT.md (every field here corresponds to a row there).

    Enum/set fields are kept as raw integers/bit-lists (matching the on-disk ordinal), not
    symbolic names -- see the doc's "Enum reference" section to interpret them. Pointer and
    reserved byte regions round-trip as hex strings; they are never meaningful, only preserved.

    Text fields are decoded/encoded as Latin-1 (byte value == code point), not the DOS-era
    CP437 code page, so round-tripping doesn't depend on the CP437 codepage provider being
    registered. This is exact for the ASCII range (all real save data seen so far) and merely
    cosmetic for the rarely-hit 0x80-0xFF range (box-drawing glyphs never appear in save data).

.PARAMETER Command
    to-json  : convert a .SAV file to JSON
    to-sav   : convert a JSON file to .SAV
    check    : round-trip a .SAV through JSON in memory and byte-diff the result

.PARAMETER InputPath
    Source file (.SAV for to-json/check, .json for to-sav).

.PARAMETER OutputPath
    Destination file (.json for to-json, .SAV for to-sav). Not used by check.

.EXAMPLE
    ./savtool.ps1 to-json ../reference/saves/INTRO_1.SAV intro.json
    ./savtool.ps1 to-sav intro.json rebuilt.SAV
    ./savtool.ps1 check ../reference/saves/INTRO_1.SAV
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('to-json', 'to-sav', 'check')]
    [string]$Command,

    [Parameter(Mandatory, Position = 1)]
    [string]$InputPath,

    [Parameter(Position = 2)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Script:SignatureBytes = [byte[]]([System.Text.Encoding]::Latin1.GetBytes("Anacreon save file v1.3`r`n") + 0x1A + [byte[]](, 0x20 * 6))
$Script:Version = 13
$Script:MaxNoOfStarbases = 100
$Script:NoOfFleetsPerEmpire = 30
$Script:MaxNoOfBlocks = 20  # MaxSizeOfGalaxy(100) div 5
$Script:Text = [System.Text.Encoding]::Latin1

# ==== primitive codecs ================================================================

function Read-Str([System.IO.BinaryReader]$Reader, [int]$Width) {
    # Bytes past the logical length are leftover heap contents in Turbo Pascal, not zeroed --
    # confirmed non-zero in a real played save (INTRO_2.SAV's NameRecord.Name tails). Keep them
    # as tail_hex so an unmodified file round-trips byte-exact; Text is the editable value.
    $raw = $Reader.ReadBytes($Width)
    $len = $raw[0]
    $text = $Script:Text.GetString($raw, 1, $len)
    $tailHex = [Convert]::ToHexString($raw, 1 + $len, $Width - 1 - $len)
    return [ordered]@{ text = $text; tail_hex = $tailHex }
}

function Write-Str([System.IO.BinaryWriter]$Writer, $Value, [int]$Width) {
    $maxLen = $Width - 1
    if ($Value -is [string]) {
        $text = $Value
        $tailHex = $null
    }
    else {
        $text = $Value.text
        $tailHex = $Value.tail_hex
    }
    $bytes = $Script:Text.GetBytes([string]$text)
    if ($bytes.Length -gt $maxLen) { $bytes = $bytes[0..($maxLen - 1)] }
    $Writer.Write([byte]$bytes.Length)
    $Writer.Write($bytes)
    $padLen = $maxLen - $bytes.Length
    $tail = if ($tailHex) { [Convert]::FromHexString($tailHex) } else { , @() }
    if ($tail.Length -ne $padLen) {
        $padded = New-Object byte[] $padLen
        [Array]::Copy($tail, $padded, [Math]::Min($tail.Length, $padLen))
        $tail = $padded
    }
    if ($padLen -gt 0) { $Writer.Write([byte[]]$tail) }
}

function Read-SetBits([System.IO.BinaryReader]$Reader, [int]$NumBytes) {
    $raw = $Reader.ReadBytes($NumBytes)
    $bits = @()
    for ($i = 0; $i -lt $NumBytes; $i++) {
        $b = $raw[$i]
        for ($bit = 0; $bit -lt 8; $bit++) {
            if ($b -band (1 -shl $bit)) { $bits += ($i * 8 + $bit) }
        }
    }
    return , $bits
}

function Write-SetBits([System.IO.BinaryWriter]$Writer, $Bits, [int]$NumBytes) {
    $bytes = New-Object byte[] $NumBytes
    foreach ($m in $Bits) {
        $byteIdx = [math]::Floor($m / 8)
        $bitIdx = $m % 8
        $bytes[$byteIdx] = $bytes[$byteIdx] -bor (1 -shl $bitIdx)
    }
    $Writer.Write($bytes)
}

function Read-Hex([System.IO.BinaryReader]$Reader, [int]$N) {
    return [Convert]::ToHexString($Reader.ReadBytes($N))
}

function Write-Hex([System.IO.BinaryWriter]$Writer, [string]$Hex, [int]$N) {
    $bytes = if ($Hex) { [Convert]::FromHexString($Hex) } else { , @() }
    if ($bytes.Length -ne $N) {
        $padded = New-Object byte[] $N
        [Array]::Copy($bytes, $padded, [Math]::Min($bytes.Length, $N))
        $bytes = $padded
    }
    $Writer.Write([byte[]]$bytes)
}

function Read-XY([System.IO.BinaryReader]$Reader) {
    return [ordered]@{ x = $Reader.ReadByte(); y = $Reader.ReadByte() }
}

function Write-XY([System.IO.BinaryWriter]$Writer, $XY) {
    $Writer.Write([byte]$XY.x)
    $Writer.Write([byte]$XY.y)
}

function Read-Id([System.IO.BinaryReader]$Reader) {
    return [ordered]@{ objType = $Reader.ReadByte(); index = $Reader.ReadByte() }
}

function Write-Id([System.IO.BinaryWriter]$Writer, $Id) {
    $Writer.Write([byte]$Id.objType)
    $Writer.Write([byte]$Id.index)
}

function Read-Location([System.IO.BinaryReader]$Reader) {
    return [ordered]@{ xy = (Read-XY $Reader); id = (Read-Id $Reader) }
}

function Write-Location([System.IO.BinaryWriter]$Writer, $Loc) {
    Write-XY $Writer $Loc.xy
    Write-Id $Writer $Loc.id
}

function Read-WordArray([System.IO.BinaryReader]$Reader, [int]$N) {
    $arr = @()
    for ($i = 0; $i -lt $N; $i++) { $arr += $Reader.ReadUInt16() }
    return , $arr
}

function Write-WordArray([System.IO.BinaryWriter]$Writer, $Arr, [int]$N) {
    for ($i = 0; $i -lt $N; $i++) {
        $v = if ($i -lt $Arr.Count) { $Arr[$i] } else { 0 }
        $Writer.Write([UInt16]$v)
    }
}

# ==== Planets / Starbases / Fleets / Stargates / Constructions (DATASTRC.PAS) =========

function Read-PlanetRecord([System.IO.BinaryReader]$Reader) {
    $p = [ordered]@{}
    $p.xy = Read-XY $Reader
    $p.emp = $Reader.ReadByte()
    $p.scoutedBy = Read-SetBits $Reader 1
    $p.knownBy = Read-SetBits $Reader 1
    $p.cls = $Reader.ReadByte()
    $p.typ = $Reader.ReadByte()
    $p.impExp = $Reader.ReadInt16()
    $p.tech = $Reader.ReadByte()
    $p.eff = $Reader.ReadByte()
    $p.revIndex = $Reader.ReadByte()
    $p.special = Read-SetBits $Reader 1
    $p.pop = $Reader.ReadUInt16()
    $p.ships = Read-WordArray $Reader 7
    $p.cargo = Read-WordArray $Reader 7
    $p.defns = Read-WordArray $Reader 4
    $p.indus = Read-WordArray $Reader 9
    $p.triReserve = $Reader.ReadUInt16()
    $p.reserved_hex = Read-Hex $Reader 16
    $p.nextId = Read-Id $Reader
    return $p
}

function Write-PlanetRecord([System.IO.BinaryWriter]$Writer, $P) {
    Write-XY $Writer $P.xy
    $Writer.Write([byte]$P.emp)
    Write-SetBits $Writer $P.scoutedBy 1
    Write-SetBits $Writer $P.knownBy 1
    $Writer.Write([byte]$P.cls)
    $Writer.Write([byte]$P.typ)
    $Writer.Write([Int16]$P.impExp)
    $Writer.Write([byte]$P.tech)
    $Writer.Write([byte]$P.eff)
    $Writer.Write([byte]$P.revIndex)
    Write-SetBits $Writer $P.special 1
    $Writer.Write([UInt16]$P.pop)
    Write-WordArray $Writer $P.ships 7
    Write-WordArray $Writer $P.cargo 7
    Write-WordArray $Writer $P.defns 4
    Write-WordArray $Writer $P.indus 9
    $Writer.Write([UInt16]$P.triReserve)
    Write-Hex $Writer $P.reserved_hex 16
    Write-Id $Writer $P.nextId
}

function Read-StarbaseRecord([System.IO.BinaryReader]$Reader) {
    $s = [ordered]@{}
    $s.xy = Read-XY $Reader
    $s.emp = $Reader.ReadByte()
    $s.scoutedBy = Read-SetBits $Reader 1
    $s.knownBy = Read-SetBits $Reader 1
    $s.styp = $Reader.ReadByte()
    $s.typ = $Reader.ReadByte()
    $s.tech = $Reader.ReadByte()
    $s.eff = $Reader.ReadByte()
    $s.revIndex = $Reader.ReadByte()
    $s.special = Read-SetBits $Reader 1
    $s.pop = $Reader.ReadUInt16()
    $s.ships = Read-WordArray $Reader 7
    $s.cargo = Read-WordArray $Reader 7
    $s.defns = Read-WordArray $Reader 4
    $s.indus = Read-WordArray $Reader 9
    $s.move = $Reader.ReadByte()
    $s.dest = Read-XY $Reader
    $s.status = $Reader.ReadByte()
    $s.reserved_hex = Read-Hex $Reader 18
    $s.nextId = Read-Id $Reader
    return $s
}

function Write-StarbaseRecord([System.IO.BinaryWriter]$Writer, $S) {
    Write-XY $Writer $S.xy
    $Writer.Write([byte]$S.emp)
    Write-SetBits $Writer $S.scoutedBy 1
    Write-SetBits $Writer $S.knownBy 1
    $Writer.Write([byte]$S.styp)
    $Writer.Write([byte]$S.typ)
    $Writer.Write([byte]$S.tech)
    $Writer.Write([byte]$S.eff)
    $Writer.Write([byte]$S.revIndex)
    Write-SetBits $Writer $S.special 1
    $Writer.Write([UInt16]$S.pop)
    Write-WordArray $Writer $S.ships 7
    Write-WordArray $Writer $S.cargo 7
    Write-WordArray $Writer $S.defns 4
    Write-WordArray $Writer $S.indus 9
    $Writer.Write([byte]$S.move)
    Write-XY $Writer $S.dest
    $Writer.Write([byte]$S.status)
    Write-Hex $Writer $S.reserved_hex 18
    Write-Id $Writer $S.nextId
}

function Read-FleetRecord([System.IO.BinaryReader]$Reader) {
    $f = [ordered]@{}
    $f.xy = Read-XY $Reader
    $f.emp = $Reader.ReadByte()
    $f.scoutedBy = Read-SetBits $Reader 1
    $f.ships = Read-WordArray $Reader 7
    $f.cargo = Read-WordArray $Reader 7
    $f.dest = Read-XY $Reader
    $f.status = $Reader.ReadByte()
    $f.fuelHigh = $Reader.ReadByte()
    $f.fuel = $Reader.ReadInt16()
    $f.knownBy = Read-SetBits $Reader 1
    $f.nextOrder = $Reader.ReadByte()
    $f.orderData_hex = Read-Hex $Reader 6
    $f.npeDataIndex = $Reader.ReadByte()
    $f.reserved_hex = Read-Hex $Reader 8
    $f.nextId = Read-Id $Reader
    return $f
}

function Write-FleetRecord([System.IO.BinaryWriter]$Writer, $F) {
    Write-XY $Writer $F.xy
    $Writer.Write([byte]$F.emp)
    Write-SetBits $Writer $F.scoutedBy 1
    Write-WordArray $Writer $F.ships 7
    Write-WordArray $Writer $F.cargo 7
    Write-XY $Writer $F.dest
    $Writer.Write([byte]$F.status)
    $Writer.Write([byte]$F.fuelHigh)
    $Writer.Write([Int16]$F.fuel)
    Write-SetBits $Writer $F.knownBy 1
    $Writer.Write([byte]$F.nextOrder)
    Write-Hex $Writer $F.orderData_hex 6
    $Writer.Write([byte]$F.npeDataIndex)
    Write-Hex $Writer $F.reserved_hex 8
    Write-Id $Writer $F.nextId
}

function Read-Command([System.IO.BinaryReader]$Reader) {
    return [ordered]@{ typ = $Reader.ReadByte(); variant_hex = (Read-Hex $Reader 4) }
}

function Write-Command([System.IO.BinaryWriter]$Writer, $C) {
    $Writer.Write([byte]$C.typ)
    Write-Hex $Writer $C.variant_hex 4
}

function Read-StargateRecord([System.IO.BinaryReader]$Reader) {
    $g = [ordered]@{}
    $g.xy = Read-XY $Reader
    $g.emp = $Reader.ReadByte()
    $g.scoutedBy = Read-SetBits $Reader 1
    $g.knownBy = Read-SetBits $Reader 1
    $g.gtyp = $Reader.ReadByte()
    $g.dest = Read-XY $Reader
    $g.nextId = Read-Id $Reader
    return $g
}

function Write-StargateRecord([System.IO.BinaryWriter]$Writer, $G) {
    Write-XY $Writer $G.xy
    $Writer.Write([byte]$G.emp)
    Write-SetBits $Writer $G.scoutedBy 1
    Write-SetBits $Writer $G.knownBy 1
    $Writer.Write([byte]$G.gtyp)
    Write-XY $Writer $G.dest
    Write-Id $Writer $G.nextId
}

function Read-ConstrRecord([System.IO.BinaryReader]$Reader) {
    $c = [ordered]@{}
    $c.xy = Read-XY $Reader
    $c.emp = $Reader.ReadByte()
    $c.scoutedBy = Read-SetBits $Reader 1
    $c.knownBy = Read-SetBits $Reader 1
    $c.ctyp = $Reader.ReadByte()
    $c.timeToCompletion = $Reader.ReadByte()
    $c.nextId = Read-Id $Reader
    return $c
}

function Write-ConstrRecord([System.IO.BinaryWriter]$Writer, $C) {
    Write-XY $Writer $C.xy
    $Writer.Write([byte]$C.emp)
    Write-SetBits $Writer $C.scoutedBy 1
    Write-SetBits $Writer $C.knownBy 1
    $Writer.Write([byte]$C.ctyp)
    $Writer.Write([byte]$C.timeToCompletion)
    Write-Id $Writer $C.nextId
}

function Read-IndexedSection([System.IO.BinaryReader]$Reader, [scriptblock]$RecordReader) {
    $items = @()
    $idx = $Reader.ReadUInt16()
    while ($idx -ne 0) {
        $rec = & $RecordReader $Reader
        $items += [ordered]@{ index = $idx; record = $rec }
        $idx = $Reader.ReadUInt16()
    }
    return , $items
}

function Write-IndexedSection([System.IO.BinaryWriter]$Writer, $Items, [scriptblock]$RecordWriter) {
    foreach ($item in $Items) {
        $Writer.Write([UInt16]$item.index)
        & $RecordWriter $Writer $item.record
    }
    $Writer.Write([UInt16]0)
}

# ==== Empire Data (DATASTRC.PAS:177-204) ==============================================

function Read-DefenseRecord([System.IO.BinaryReader]$Reader) {
    $shell = @()
    for ($i = 0; $i -lt 35; $i++) { $shell += $Reader.ReadByte() }
    $starbase = @()
    for ($i = 0; $i -lt 35; $i++) { $starbase += $Reader.ReadByte() }
    return [ordered]@{ shellDefDist = $shell; starbaseDefDist = $starbase }
}

function Write-DefenseRecord([System.IO.BinaryWriter]$Writer, $D) {
    foreach ($v in $D.shellDefDist) { $Writer.Write([byte]$v) }
    foreach ($v in $D.starbaseDefDist) { $Writer.Write([byte]$v) }
}

function Read-ProbeArray([System.IO.BinaryReader]$Reader) {
    $arr = @()
    for ($i = 0; $i -lt 10; $i++) {
        $arr += [ordered]@{ dest = (Read-XY $Reader); status = $Reader.ReadByte() }
    }
    return , $arr
}

function Write-ProbeArray([System.IO.BinaryWriter]$Writer, $Arr) {
    foreach ($p in $Arr) {
        Write-XY $Writer $p.dest
        $Writer.Write([byte]$p.status)
    }
}

function Read-EmpireData([System.IO.BinaryReader]$Reader) {
    $e = [ordered]@{}
    $e.inUse = [bool]$Reader.ReadByte()
    $e.isAPlayer = [bool]$Reader.ReadByte()
    $e.empireName = Read-Str $Reader 33
    $e.pass = Read-Str $Reader 9
    $e.timeLeft = $Reader.ReadInt16()
    $e.capital = Read-Id $Reader
    $e.defenseSettings = Read-DefenseRecord $Reader
    $e.probe = Read-ProbeArray $Reader
    $e.namesPtr_hex = Read-Hex $Reader 4
    $e.lastNamePtr_hex = Read-Hex $Reader 4
    $e.totalRevIndex = $Reader.ReadInt16()
    $e.technologyLevel = $Reader.ReadByte()
    $e.technology = Read-SetBits $Reader 4
    $e.isAnEmpress = [bool]$Reader.ReadByte()
    $e.revFactor = $Reader.ReadInt16()
    $e.founding = $Reader.ReadUInt16()
    $e.modifiers = Read-SetBits $Reader 1
    $e.reserved_hex = Read-Hex $Reader 14
    return $e
}

function Write-EmpireData([System.IO.BinaryWriter]$Writer, $E) {
    $Writer.Write([byte]($(if ($E.inUse) { 1 } else { 0 })))
    $Writer.Write([byte]($(if ($E.isAPlayer) { 1 } else { 0 })))
    Write-Str $Writer $E.empireName 33
    Write-Str $Writer $E.pass 9
    $Writer.Write([Int16]$E.timeLeft)
    Write-Id $Writer $E.capital
    Write-DefenseRecord $Writer $E.defenseSettings
    Write-ProbeArray $Writer $E.probe
    Write-Hex $Writer $E.namesPtr_hex 4
    Write-Hex $Writer $E.lastNamePtr_hex 4
    $Writer.Write([Int16]$E.totalRevIndex)
    $Writer.Write([byte]$E.technologyLevel)
    Write-SetBits $Writer $E.technology 4
    $Writer.Write([byte]($(if ($E.isAnEmpress) { 1 } else { 0 })))
    $Writer.Write([Int16]$E.revFactor)
    $Writer.Write([UInt16]$E.founding)
    Write-SetBits $Writer $E.modifiers 1
    Write-Hex $Writer $E.reserved_hex 14
}

function Read-NameRecord([System.IO.BinaryReader]$Reader) {
    $n = [ordered]@{}
    $n.name = Read-Str $Reader 9
    $n.coord = Read-Location $Reader
    $n.next_hex = Read-Hex $Reader 4
    return $n
}

function Write-NameRecord([System.IO.BinaryWriter]$Writer, $N) {
    Write-Str $Writer $N.name 9
    Write-Location $Writer $N.coord
    Write-Hex $Writer $N.next_hex 4
}

# ==== News (NEWS.PAS:113-122) ==========================================================

function Read-NewsRecord([System.IO.BinaryReader]$Reader) {
    $n = [ordered]@{}
    $n.headline = $Reader.ReadByte()
    $n.loc1 = Read-Location $Reader
    $n.parm1 = $Reader.ReadInt16()
    $n.parm2 = $Reader.ReadInt16()
    $n.parm3 = $Reader.ReadInt16()
    $n.next_hex = Read-Hex $Reader 4
    return $n
}

function Write-NewsRecord([System.IO.BinaryWriter]$Writer, $N) {
    $Writer.Write([byte]$N.headline)
    Write-Location $Writer $N.loc1
    $Writer.Write([Int16]$N.parm1)
    $Writer.Write([Int16]$N.parm2)
    $Writer.Write([Int16]$N.parm3)
    Write-Hex $Writer $N.next_hex 4
}

# ==== Messages (MESS.PAS:23-33) ========================================================

function Read-MessageRecord([System.IO.BinaryReader]$Reader) {
    $m = [ordered]@{}
    $m.sender = $Reader.ReadByte()
    $m.recipient = Read-SetBits $Reader 1
    $m.readBy = Read-SetBits $Reader 1
    $m.read = [bool]$Reader.ReadByte()
    $m.intercepted = [bool]$Reader.ReadByte()
    $m.mesTextNoOfLines_redundant = $Reader.ReadUInt16()
    $m.firstLinePtr_hex = Read-Hex $Reader 4
    $m.lastLinePtr_hex = Read-Hex $Reader 4
    $m.nextPtr_hex = Read-Hex $Reader 4
    $m.prevPtr_hex = Read-Hex $Reader 4
    $noOfLines = $Reader.ReadByte()
    $lines = @()
    for ($i = 0; $i -lt $noOfLines; $i++) { $lines += Read-Str $Reader 81 }
    $m.lines = $lines
    return $m
}

function Write-MessageRecord([System.IO.BinaryWriter]$Writer, $M) {
    $Writer.Write([byte]$M.sender)
    Write-SetBits $Writer $M.recipient 1
    Write-SetBits $Writer $M.readBy 1
    $Writer.Write([byte]($(if ($M.read) { 1 } else { 0 })))
    $Writer.Write([byte]($(if ($M.intercepted) { 1 } else { 0 })))
    $Writer.Write([UInt16]$M.mesTextNoOfLines_redundant)
    Write-Hex $Writer $M.firstLinePtr_hex 4
    Write-Hex $Writer $M.lastLinePtr_hex 4
    Write-Hex $Writer $M.nextPtr_hex 4
    Write-Hex $Writer $M.prevPtr_hex 4
    $Writer.Write([byte]$M.lines.Count)
    foreach ($line in $M.lines) { Write-Str $Writer $line 81 }
}

# ==== NPE data (NPETYPES.PAS) ==========================================================

function Read-FleetDataArray([System.IO.BinaryReader]$Reader, [int]$N) {
    $out = @()
    for ($i = 0; $i -lt $N; $i++) {
        $out += [ordered]@{
            mission    = $Reader.ReadByte()
            targetId   = Read-Id $Reader
            homeBaseId = Read-Id $Reader
            midway     = Read-Id $Reader
            waiting    = $Reader.ReadByte()
            blockX     = $Reader.ReadByte()
            blockY     = $Reader.ReadByte()
            index      = $Reader.ReadByte()
        }
    }
    return , $out
}

function Write-FleetDataArray([System.IO.BinaryWriter]$Writer, $Arr) {
    foreach ($f in $Arr) {
        $Writer.Write([byte]$f.mission)
        Write-Id $Writer $f.targetId
        Write-Id $Writer $f.homeBaseId
        Write-Id $Writer $f.midway
        $Writer.Write([byte]$f.waiting)
        $Writer.Write([byte]$f.blockX)
        $Writer.Write([byte]$f.blockY)
        $Writer.Write([byte]$f.index)
    }
}

function Read-PirateData([System.IO.BinaryReader]$Reader) {
    $fleetData = Read-FleetDataArray $Reader $Script:NoOfFleetsPerEmpire
    $hunting = @()
    for ($i = 0; $i -lt ($Script:MaxNoOfBlocks * $Script:MaxNoOfBlocks); $i++) { $hunting += $Reader.ReadByte() }
    $sheep = @()
    for ($i = 0; $i -lt 9; $i++) { $sheep += $Reader.ReadByte() }
    return [ordered]@{ fleetData = $fleetData; huntingGround = $hunting; sheep = $sheep }
}

function Write-PirateData([System.IO.BinaryWriter]$Writer, $D) {
    Write-FleetDataArray $Writer $D.fleetData
    foreach ($v in $D.huntingGround) { $Writer.Write([byte]$v) }
    foreach ($v in $D.sheep) { $Writer.Write([byte]$v) }
}

function Read-StateDeptArray([System.IO.BinaryReader]$Reader) {
    $out = @()
    for ($i = 0; $i -lt 9; $i++) {
        $out += [ordered]@{
            policy         = $Reader.ReadByte()
            attackChance   = $Reader.ReadByte()
            totalMilitary  = $Reader.ReadInt32()
            worlds         = $Reader.ReadUInt16()
            threatAssess   = $Reader.ReadByte()
            aggressiveness = $Reader.ReadByte()
            balance        = $Reader.ReadInt16()
        }
    }
    return , $out
}

function Write-StateDeptArray([System.IO.BinaryWriter]$Writer, $Arr) {
    foreach ($s in $Arr) {
        $Writer.Write([byte]$s.policy)
        $Writer.Write([byte]$s.attackChance)
        $Writer.Write([Int32]$s.totalMilitary)
        $Writer.Write([UInt16]$s.worlds)
        $Writer.Write([byte]$s.threatAssess)
        $Writer.Write([byte]$s.aggressiveness)
        $Writer.Write([Int16]$s.balance)
    }
}

$Script:PersonaKeys = @('impGene', 'defGene', 'offGene', 'factorGene', 'randomGene', 'defensive',
    'offensive', 'techno', 'provoke', 'imperialist', 'worldPower', 'honorable', 'sphereX')

function Read-Persona([System.IO.BinaryReader]$Reader) {
    $p = [ordered]@{}
    foreach ($k in $Script:PersonaKeys) { $p[$k] = $Reader.ReadByte() }
    $p.clock = $Reader.ReadUInt16()
    $p.offset = $Reader.ReadByte()
    return $p
}

function Write-Persona([System.IO.BinaryWriter]$Writer, $P) {
    foreach ($k in $Script:PersonaKeys) { $Writer.Write([byte]$P[$k]) }
    $Writer.Write([UInt16]$P.clock)
    $Writer.Write([byte]$P.offset)
}

function Read-KingdomData([System.IO.BinaryReader]$Reader) {
    return [ordered]@{
        fleetData = Read-FleetDataArray $Reader $Script:NoOfFleetsPerEmpire
        state     = Read-StateDeptArray $Reader
        persona   = Read-Persona $Reader
    }
}

function Write-KingdomData([System.IO.BinaryWriter]$Writer, $D) {
    Write-FleetDataArray $Writer $D.fleetData
    Write-StateDeptArray $Writer $D.state
    Write-Persona $Writer $D.persona
}

function Read-BaseDataArray([System.IO.BinaryReader]$Reader) {
    $out = @()
    for ($i = 0; $i -lt $Script:MaxNoOfStarbases; $i++) {
        $out += [ordered]@{ mission = $Reader.ReadByte(); targetId = (Read-Id $Reader); count = $Reader.ReadUInt16() }
    }
    return , $out
}

function Write-BaseDataArray([System.IO.BinaryWriter]$Writer, $Arr) {
    foreach ($b in $Arr) {
        $Writer.Write([byte]$b.mission)
        Write-Id $Writer $b.targetId
        $Writer.Write([UInt16]$b.count)
    }
}

function Read-BerserkerData([System.IO.BinaryReader]$Reader) {
    $fleetData = Read-FleetDataArray $Reader $Script:NoOfFleetsPerEmpire
    $baseData = Read-BaseDataArray $Reader
    $spare = @()
    for ($i = 0; $i -lt 50; $i++) { $spare += $Reader.ReadUInt16() }
    return [ordered]@{ fleetData = $fleetData; baseData = $baseData; spare = $spare }
}

function Write-BerserkerData([System.IO.BinaryWriter]$Writer, $D) {
    Write-FleetDataArray $Writer $D.fleetData
    Write-BaseDataArray $Writer $D.baseData
    foreach ($v in $D.spare) { $Writer.Write([UInt16]$v) }
}

function Read-GuardianData([System.IO.BinaryReader]$Reader) {
    $fleetData = Read-FleetDataArray $Reader $Script:NoOfFleetsPerEmpire
    $spare = @()
    for ($i = 0; $i -lt 50; $i++) { $spare += $Reader.ReadUInt16() }
    return [ordered]@{ fleetData = $fleetData; spare = $spare }
}

function Write-GuardianData([System.IO.BinaryWriter]$Writer, $D) {
    Write-FleetDataArray $Writer $D.fleetData
    foreach ($v in $D.spare) { $Writer.Write([UInt16]$v) }
}

function Get-NpeKind([int]$Typ) {
    # NPE.PAS:100-111 dispatch: Kingdom1/2 share a layout; unrecognized/Trader falls back to Pirate.
    switch ($Typ) {
        1 { return 'pirate' }
        2 { return 'kingdom' }
        3 { return 'kingdom' }
        4 { return 'berserker' }
        5 { return 'guardian' }
        default { return 'pirate' }
    }
}

function Read-NpeData([System.IO.BinaryReader]$Reader, [string]$Kind) {
    switch ($Kind) {
        'pirate' { return Read-PirateData $Reader }
        'kingdom' { return Read-KingdomData $Reader }
        'berserker' { return Read-BerserkerData $Reader }
        'guardian' { return Read-GuardianData $Reader }
    }
}

function Write-NpeData([System.IO.BinaryWriter]$Writer, [string]$Kind, $Data) {
    switch ($Kind) {
        'pirate' { Write-PirateData $Writer $Data }
        'kingdom' { Write-KingdomData $Writer $Data }
        'berserker' { Write-BerserkerData $Writer $Data }
        'guardian' { Write-GuardianData $Writer $Data }
    }
}

# ==== top-level parse / build ==========================================================

function ConvertFrom-SavBytes([byte[]]$Data) {
    $ms = New-Object System.IO.MemoryStream(, $Data)
    $r = New-Object System.IO.BinaryReader($ms)

    $sig = $r.ReadBytes(32)
    $version = $r.ReadUInt16()
    if ($version -ne $Script:Version) {
        throw "unsupported save format version $version (only $($Script:Version) is documented)"
    }

    $env = [ordered]@{
        year          = $r.ReadUInt16()
        player        = $r.ReadByte()
        empiresToMove = Read-SetBits $r 2
        scenaFilename = Read-Str $r 17
        timePerTurn   = $r.ReadUInt16()
        autoSave      = [bool]$r.ReadByte()
        asyncTurns    = [bool]$r.ReadByte()
        pauseActive   = [bool]$r.ReadByte()
        reEnterGame   = [bool]$r.ReadByte()
    }

    $sizeX = $r.ReadUInt16()
    $sizeY = $r.ReadUInt16()
    $rows = @()
    for ($y = 0; $y -le $sizeY; $y++) {
        $row = @()
        for ($x = 0; $x -le $sizeX; $x++) {
            $row += [ordered]@{
                obj       = Read-Id $r
                flts      = Read-SetBits $r 1
                mineScout = Read-SetBits $r 1
                special   = $r.ReadByte()
            }
        }
        $rows += , $row
    }
    $sector = [ordered]@{ sizeOfGalaxy = $sizeX; rows = $rows }

    $planets = Read-IndexedSection $r ${function:Read-PlanetRecord}
    $starbases = Read-IndexedSection $r ${function:Read-StarbaseRecord}

    $fleets = @()
    $idx = $r.ReadUInt16()
    while ($idx -ne 0) {
        $rec = Read-FleetRecord $r
        $orderCount = $r.ReadUInt16()
        $orders = @()
        for ($i = 0; $i -lt $orderCount; $i++) { $orders += Read-Command $r }
        $fleets += [ordered]@{ index = $idx; record = $rec; orders = $orders }
        $idx = $r.ReadUInt16()
    }

    $stargates = Read-IndexedSection $r ${function:Read-StargateRecord}
    $constructions = Read-IndexedSection $r ${function:Read-ConstrRecord}

    $noOfMessages = $r.ReadByte()
    $messages = @()
    for ($i = 0; $i -lt $noOfMessages; $i++) { $messages += Read-MessageRecord $r }

    $empires = @()
    for ($i = 0; $i -lt 8; $i++) {
        $rec = Read-EmpireData $r
        $noOfNames = $r.ReadByte()
        $names = @()
        for ($j = 0; $j -lt $noOfNames; $j++) { $names += Read-NameRecord $r }
        $empires += [ordered]@{ record = $rec; names = $names }
    }

    $news = @()
    for ($i = 0; $i -lt 8; $i++) {
        $n = $r.ReadUInt16()
        $items = @()
        for ($j = 0; $j -lt $n; $j++) { $items += Read-NewsRecord $r }
        $news += , $items
    }

    $npeTypes = @()
    for ($i = 0; $i -lt 9; $i++) {
        $npeTypes += [ordered]@{ typ = $r.ReadByte(); data_hex = (Read-Hex $r 4) }
    }

    $npeBlobs = [ordered]@{}
    for ($i = 0; $i -lt 8; $i++) {
        $emp = $empires[$i].record
        if (-not $emp.inUse -or $emp.isAPlayer) { continue }
        $kind = Get-NpeKind $npeTypes[$i].typ
        $npeBlobs["$i"] = [ordered]@{ kind = $kind; data = (Read-NpeData $r $kind) }
    }

    if ($ms.Position -ne $ms.Length) {
        $remaining = $ms.Length - $ms.Position
        throw "$remaining trailing bytes after parsing (format mismatch)"
    }

    return [ordered]@{
        signature     = $Script:Text.GetString($sig)
        version       = $version
        environment   = $env
        sector        = $sector
        planets       = $planets
        starbases     = $starbases
        fleets        = $fleets
        stargates     = $stargates
        constructions = $constructions
        messages      = $messages
        empires       = $empires
        news          = $news
        npe           = [ordered]@{ types = $npeTypes; blobs = $npeBlobs }
    }
}

function ConvertTo-SavBytes($Doc) {
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)

    $w.Write([byte[]]$Script:SignatureBytes)
    $w.Write([UInt16]$Doc.version)

    $env = $Doc.environment
    $w.Write([UInt16]$env.year)
    $w.Write([byte]$env.player)
    Write-SetBits $w $env.empiresToMove 2
    Write-Str $w $env.scenaFilename 17
    $w.Write([UInt16]$env.timePerTurn)
    $w.Write([byte]($(if ($env.autoSave) { 1 } else { 0 })))
    $w.Write([byte]($(if ($env.asyncTurns) { 1 } else { 0 })))
    $w.Write([byte]($(if ($env.pauseActive) { 1 } else { 0 })))
    $w.Write([byte]($(if ($env.reEnterGame) { 1 } else { 0 })))

    $sector = $Doc.sector
    $size = $sector.sizeOfGalaxy
    $w.Write([UInt16]$size)
    $w.Write([UInt16]$size)
    foreach ($row in $sector.rows) {
        foreach ($cell in $row) {
            Write-Id $w $cell.obj
            Write-SetBits $w $cell.flts 1
            Write-SetBits $w $cell.mineScout 1
            $w.Write([byte]$cell.special)
        }
    }

    Write-IndexedSection $w $Doc.planets ${function:Write-PlanetRecord}
    Write-IndexedSection $w $Doc.starbases ${function:Write-StarbaseRecord}

    foreach ($f in $Doc.fleets) {
        $w.Write([UInt16]$f.index)
        Write-FleetRecord $w $f.record
        $w.Write([UInt16]$f.orders.Count)
        foreach ($c in $f.orders) { Write-Command $w $c }
    }
    $w.Write([UInt16]0)

    Write-IndexedSection $w $Doc.stargates ${function:Write-StargateRecord}
    Write-IndexedSection $w $Doc.constructions ${function:Write-ConstrRecord}

    $w.Write([byte]$Doc.messages.Count)
    foreach ($m in $Doc.messages) { Write-MessageRecord $w $m }

    foreach ($e in $Doc.empires) {
        Write-EmpireData $w $e.record
        $w.Write([byte]$e.names.Count)
        foreach ($n in $e.names) { Write-NameRecord $w $n }
    }

    foreach ($empireNews in $Doc.news) {
        $w.Write([UInt16]$empireNews.Count)
        foreach ($item in $empireNews) { Write-NewsRecord $w $item }
    }

    foreach ($t in $Doc.npe.types) {
        $w.Write([byte]$t.typ)
        Write-Hex $w $t.data_hex 4
    }

    for ($i = 0; $i -lt 8; $i++) {
        $key = "$i"
        if (-not $Doc.npe.blobs.Contains($key)) { continue }
        $blob = $Doc.npe.blobs[$key]
        Write-NpeData $w $blob.kind $blob.data
    }

    $w.Flush()
    return $ms.ToArray()
}

# ==== CLI ==============================================================================

switch ($Command) {
    'to-json' {
        if (-not $OutputPath) { throw "to-json requires an OutputPath" }
        $data = [System.IO.File]::ReadAllBytes($InputPath)
        $doc = ConvertFrom-SavBytes $data
        $json = $doc | ConvertTo-Json -Depth 30
        [System.IO.File]::WriteAllText($OutputPath, $json)
        Write-Host "wrote $OutputPath"
    }
    'to-sav' {
        if (-not $OutputPath) { throw "to-sav requires an OutputPath" }
        $json = [System.IO.File]::ReadAllText($InputPath)
        $doc = $json | ConvertFrom-Json -AsHashtable -Depth 30
        $bytes = ConvertTo-SavBytes $doc
        [System.IO.File]::WriteAllBytes($OutputPath, $bytes)
        Write-Host "wrote $OutputPath ($($bytes.Length) bytes)"
    }
    'check' {
        $original = [System.IO.File]::ReadAllBytes($InputPath)
        $doc = ConvertFrom-SavBytes $original
        # round-trip through JSON text too, so this exercises the exact same path to-json/to-sav use
        $json = $doc | ConvertTo-Json -Depth 30
        $reloaded = $json | ConvertFrom-Json -AsHashtable -Depth 30
        $rebuilt = ConvertTo-SavBytes $reloaded
        $identical = $original.Length -eq $rebuilt.Length
        if ($identical) {
            for ($i = 0; $i -lt $original.Length; $i++) {
                if ($original[$i] -ne $rebuilt[$i]) { $identical = $false; break }
            }
        }
        if ($identical) {
            Write-Host "OK: byte-identical round trip ($($original.Length) bytes)"
        }
        else {
            Write-Host "MISMATCH: original $($original.Length) bytes, rebuilt $($rebuilt.Length) bytes"
            $n = [Math]::Min($original.Length, $rebuilt.Length)
            $diffs = 0
            for ($i = 0; $i -lt $n; $i++) {
                if ($original[$i] -ne $rebuilt[$i]) {
                    $o = $original[$i].ToString('x2')
                    $b = $rebuilt[$i].ToString('x2')
                    Write-Host "  first diff at offset $i`: original=$o rebuilt=$b"
                    $diffs++
                    if ($diffs -ge 10) { Write-Host "  ..."; break }
                }
            }
            exit 1
        }
    }
}
