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
/// -prefixed <c>STRING[N]</c> buffers, and bitset-shaped <c>SET OF T</c> values. See
/// <see cref="SavWriter"/> for the write-side counterpart.
///
/// Deliberately does not preserve opaque bytes (pointer fields, `Reserved` arrays, unused string
/// buffer tails) the way `scripts/savtool.py`/`.ps1` do — those tools exist for a byte-identical
/// round trip; this reader feeds a live <see cref="Game"/> object graph that has nowhere to put
/// garbage bytes and no need to, since `.SAV` write-back in this port is a correctness-verification
/// tool, not a byte-faithful save format (see `docs/ROADMAP.md`'s save/load notes).
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

/// <summary>
/// Sequential little-endian writer, the exact mirror of <see cref="SavReader"/> — same primitive
/// shapes, same deliberate non-goal of byte-faithful reproduction (a `Reserved`/pointer-field byte
/// is always written as zero, never round-tripped, since nothing in <see cref="Game"/> has one to
/// preserve). Built for <see cref="SavGameWriter"/>, this port's minimal `.SAV` write-back
/// verification tool: the bar is "real Pascal `LoadGame` accepts the result," not a byte-identical
/// file — see `docs/ROADMAP.md`'s save/load notes.
/// </summary>
public sealed class SavWriter
{
    private readonly List<byte> _data = [];

    public byte[] ToArray() => [.. _data];

    public void WriteByte(byte value) => _data.Add(value);

    public void WriteBoolean(bool value) => WriteByte((byte)(value ? 1 : 0));

    /// Turbo Pascal `Word` — unsigned 16-bit.
    public void WriteWord(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
    }

    /// Turbo Pascal `Integer` — signed 16-bit (not 32; that's `LongInt`).
    public void WriteInteger(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
    }

    /// Turbo Pascal `LongInt` — signed 32-bit.
    public void WriteLongInt(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
    }

    public void WriteZeros(int count) => _data.AddRange(new byte[count]);

    /// Raw byte passthrough — for an opaque blob (<see cref="Game.UnimplementedNpeBlobs"/>) this
    /// port has no interpretation of and must reproduce verbatim.
    public void WriteBytes(byte[] bytes) => _data.AddRange(bytes);

    /// <summary>
    /// `STRING[bufferSize]`: 1 length byte + `bufferSize` character slots. <paramref name="value"/>
    /// is truncated to <paramref name="bufferSize"/> if too long (a real `.SAV`-format string field
    /// is a fixed buffer — Pascal itself silently truncates an over-length assignment, the same trap
    /// `docs/SAV_FILE_FORMAT.md`'s own Conventions section notes for `.SCN` paths); unused buffer
    /// slots beyond the written length are zero, never garbage (this port has no stack/heap leftover
    /// to reproduce there).
    /// </summary>
    public void WritePascalString(string value, int bufferSize)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var length = Math.Min(bytes.Length, bufferSize);

        WriteByte((byte)length);
        _data.AddRange(bytes.AsSpan(0, length).ToArray());
        WriteZeros(bufferSize - length);
    }

    /// The header signature — written without a length byte, matching <see cref="SavReader.ReadRawString"/>.
    public void WriteRawString(string value, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var written = Math.Min(bytes.Length, length);
        _data.AddRange(bytes.AsSpan(0, written).ToArray());
        WriteZeros(length - written);
    }

    public void WriteCoordinate(Galaxy.Coordinate coordinate)
    {
        WriteByte((byte)coordinate.X);
        WriteByte((byte)coordinate.Y);
    }

    public void WriteIdNumber(SavIdNumber id)
    {
        WriteByte((byte)id.ObjectType);
        WriteByte(id.Index);
    }

    /// <summary>See <see cref="SavReader.ReadBitSet"/> — same `ceil(byteCount*8/8)`-byte bitset shape, ordinals relative to the set's own base type.</summary>
    public void WriteBitSet(IEnumerable<int> ordinals, int byteCount)
    {
        var bytes = new byte[byteCount];
        foreach (var ordinal in ordinals) {
            bytes[ordinal / 8] |= (byte)(1 << (ordinal % 8));
        }
        _data.AddRange(bytes);
    }
}
