using System.Buffers.Binary;
using System.IO.Compression;

namespace FullThrust.Assets;

// Minimal RGBA8 PNG writer so generated textures can be embedded without touching the file system.
public static class PngEncoder {

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height) {

        using var output = new MemoryStream();

        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];

        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);

        header[8] = 8;
        header[9] = 6;

        WriteChunk(output, "IHDR"u8, header);

        using (var raw = new MemoryStream()) {

            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true)) {

                var stride = width * 4;

                for (var y = 0; y < height; y++) {

                    zlib.WriteByte(0);
                    zlib.Write(rgba.Slice(y * stride, stride));

                }

            }

            WriteChunk(output, "IDAT"u8, raw.ToArray());

        }

        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();

    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data) {

        Span<byte> word = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(type);
        output.Write(data);

        var crc = Crc(0xFFFFFFFFu, type);

        crc = Crc(crc, data) ^ 0xFFFFFFFFu;

        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);

    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes) {

        foreach (var b in bytes) {

            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        }

        return crc;

    }

    private static uint[] BuildCrcTable() {

        var table = new uint[256];

        for (var n = 0u; n < 256; n++) {

            var c = n;

            for (var k = 0; k < 8; k++) {

                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            }

            table[n] = c;

        }

        return table;

    }

}
