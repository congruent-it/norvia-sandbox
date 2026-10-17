using System.Buffers.Binary;
using Norvia.Simulator.Domain.Drivers;

namespace Norvia.Simulator.Domain.UnitTests.Drivers;

public sealed class DeliveryFilesTests
{
    private static (int Width, int Height) Size(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

    [Fact]
    public void Signature_IsAPng_LargerThanOnePixel_AndSmall()
    {
        var signature = DeliveryFiles.Signature(7, "SH-0000001");

        signature.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        Size(signature).Should().Be((600, 200));
        signature.Length.Should().BeInRange(500, 50 * 1024);
    }

    [Fact]
    public void Signature_SameSeedAndShipment_GivesTheSameBytes()
    {
        DeliveryFiles.Signature(7, "SH-0000001").Should().Equal(DeliveryFiles.Signature(7, "SH-0000001"));
    }

    [Fact]
    public void Signature_DiffersAcrossShipmentsAndSeeds()
    {
        var signatures = Enumerable.Range(1, 20).Select(n => Convert.ToBase64String(DeliveryFiles.Signature(7, $"SH-{n:D7}"))).ToList();

        signatures.Distinct().Should().HaveCount(20);
        DeliveryFiles.Signature(8, "SH-0000001").Should().NotEqual(DeliveryFiles.Signature(7, "SH-0000001"));
    }

    [Fact]
    public void Photo_IsAPng_AndDiffersAcrossShipments()
    {
        var photo = DeliveryFiles.Photo(7, "SH-0000001");

        photo.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        Size(photo).Should().Be((320, 240));
        photo.Should().Equal(DeliveryFiles.Photo(7, "SH-0000001"));
        photo.Should().NotEqual(DeliveryFiles.Photo(7, "SH-0000002"));
    }

    [Fact]
    public void Files_HaveValidChunkChecksums_SoTheyDecode()
    {
        foreach (var png in new[] { DeliveryFiles.Signature(1, "A"), DeliveryFiles.Photo(1, "A") })
        {
            var at = 8;
            var types = new List<string>();
            while (at < png.Length)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
                types.Add(System.Text.Encoding.ASCII.GetString(png, at + 4, 4));
                var crc = Crc(png.AsSpan(at + 4, length + 4));
                BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)).Should().Be(crc);
                at += 12 + length;
            }

            types.Should().Equal("IHDR", "IDAT", "IEND");
        }
    }

    // The PNG spec's CRC-32, written out here so the test checks the writer against the spec and not against itself.
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
