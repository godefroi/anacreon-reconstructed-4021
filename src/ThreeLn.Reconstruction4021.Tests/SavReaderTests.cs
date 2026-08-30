using ThreeLn.Reconstruction4021.Core.SaveFormat;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// The byte-cursor primitives (`SavReader`/`SavWriter`), exercised directly against hand-built byte
/// arrays — no `Game`/`Galaxy` dependency, matching `CombatConstantsTests`'s own precedent of
/// unit-testing pure data/format shapes directly.
/// </summary>
public class SavReaderTests
{
    [Test]
    public async Task ReadByte_ReadsSequentially()
    {
        var reader = new SavReader([0x01, 0x02, 0x03]);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0x02);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0x03);
        await Assert.That(reader.AtEnd).IsTrue();
    }

    [Test]
    public async Task ReadBoolean_NonzeroIsTrue()
    {
        var reader = new SavReader([0x00, 0x01, 0xFF]);
        await Assert.That(reader.ReadBoolean()).IsFalse();
        await Assert.That(reader.ReadBoolean()).IsTrue();
        await Assert.That(reader.ReadBoolean()).IsTrue();
    }

    [Test]
    public async Task ReadWord_IsLittleEndianUnsigned()
    {
        // 0x1234 stored little-endian: 0x34, 0x12.
        var reader = new SavReader([0x34, 0x12]);
        await Assert.That(reader.ReadWord()).IsEqualTo((ushort)0x1234);
    }

    [Test]
    public async Task ReadInteger_IsSigned16Bit()
    {
        // -1 as 16-bit two's complement: 0xFFFF.
        var reader = new SavReader([0xFF, 0xFF]);
        await Assert.That(reader.ReadInteger()).IsEqualTo((short)-1);
    }

    [Test]
    public async Task ReadLongInt_IsSigned32Bit()
    {
        var reader = new SavReader([0xFF, 0xFF, 0xFF, 0xFF]);
        await Assert.That(reader.ReadLongInt()).IsEqualTo(-1);
    }

    [Test]
    public async Task ReadPascalString_UsesLengthByteNotBufferSize()
    {
        // STRING[8]: length byte 2, "hi", then 6 unused/garbage buffer bytes.
        byte[] bytes = [2, (byte)'h', (byte)'i', 9, 9, 9, 9, 9, 9];
        var reader = new SavReader(bytes);
        await Assert.That(reader.ReadPascalString(8)).IsEqualTo("hi");
        await Assert.That(reader.Position).IsEqualTo(9);
    }

    [Test]
    public async Task ReadRawString_HasNoLengthByte()
    {
        byte[] bytes = "abc"u8.ToArray();
        var reader = new SavReader(bytes);
        await Assert.That(reader.ReadRawString(3)).IsEqualTo("abc");
    }

    [Test]
    public async Task ReadCoordinate_ReadsXThenY()
    {
        var reader = new SavReader([5, 9]);
        var coordinate = reader.ReadCoordinate();
        await Assert.That(coordinate.X).IsEqualTo(5);
        await Assert.That(coordinate.Y).IsEqualTo(9);
    }

    [Test]
    public async Task ReadIdNumber_ReadsObjectTypeThenIndex()
    {
        var reader = new SavReader([(byte)SavObjectType.Pln, 7]);
        var id = reader.ReadIdNumber();
        await Assert.That(id.ObjectType).IsEqualTo(SavObjectType.Pln);
        await Assert.That(id.Index).IsEqualTo((byte)7);
        await Assert.That(id.IsEmpty).IsFalse();
    }

    [Test]
    public async Task ReadIdNumber_ZeroIndexIsEmpty()
    {
        var reader = new SavReader([(byte)SavObjectType.Void, 0]);
        await Assert.That(reader.ReadIdNumber().IsEmpty).IsTrue();
    }

    [Test]
    public async Task ReadBitSet_Bit0IsLowestOrdinalOfFirstByte()
    {
        // 0b00000101 = bits 0 and 2 set.
        var reader = new SavReader([0b0000_0101]);
        var bits = reader.ReadBitSet(1);
        await Assert.That(bits).Contains(0);
        await Assert.That(bits).Contains(2);
        await Assert.That(bits.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReadBitSet_SpansMultipleBytes()
    {
        // EmpireSet-shaped: 2 bytes, bit 8 (first bit of second byte) set.
        var reader = new SavReader([0x00, 0b0000_0001]);
        var bits = reader.ReadBitSet(2);
        await Assert.That(bits).Contains(8);
        await Assert.That(bits.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Skip_AdvancesPositionWithoutReading()
    {
        var reader = new SavReader([1, 2, 3, 4]);
        reader.Skip(2);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)3);
    }
}
