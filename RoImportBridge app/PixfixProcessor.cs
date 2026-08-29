using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RoImportBridge;

internal static class PixfixProcessor
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static readonly (int X, int Y)[] Neighbors =
    [
        (-1, -1),
        (0, -1),
        (1, -1),
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0)
    ];

    public static byte[] Apply(byte[] bytes)
    {
        var image = DecodePng(bytes);
        var width = image.Width;
        var height = image.Height;
        var rgba = image.Rgba;
        var pixelCount = width * height;
        var sourcePixels = new int[pixelCount];
        var queue = new int[pixelCount];
        Array.Fill(sourcePixels, -1);

        var queueStart = 0;
        var queueEnd = 0;
        var transparentCount = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = y * width + x;
                var alpha = rgba[pixel * 4 + 3];

                if (alpha == 0)
                {
                    transparentCount++;
                    continue;
                }

                var isEdge = false;

                foreach (var neighbor in Neighbors)
                {
                    var nx = x + neighbor.X;
                    var ny = y + neighbor.Y;

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    if (rgba[(ny * width + nx) * 4 + 3] == 0)
                    {
                        isEdge = true;
                        break;
                    }
                }

                if (!isEdge)
                {
                    continue;
                }

                sourcePixels[pixel] = pixel;
                queue[queueEnd++] = pixel;
            }
        }

        if (transparentCount == 0 || queueEnd == 0)
        {
            return bytes;
        }

        while (queueStart < queueEnd)
        {
            var pixel = queue[queueStart++];
            var sourcePixel = sourcePixels[pixel];
            var x = pixel % width;
            var y = pixel / width;

            foreach (var neighbor in Neighbors)
            {
                var nx = x + neighbor.X;
                var ny = y + neighbor.Y;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }

                var next = ny * width + nx;

                if (rgba[next * 4 + 3] != 0 || sourcePixels[next] != -1)
                {
                    continue;
                }

                sourcePixels[next] = sourcePixel;
                queue[queueEnd++] = next;
            }
        }

        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            if (rgba[pixel * 4 + 3] != 0)
            {
                continue;
            }

            var sourcePixel = sourcePixels[pixel];

            if (sourcePixel < 0)
            {
                continue;
            }

            var targetOffset = pixel * 4;
            var sourceOffset = sourcePixel * 4;
            rgba[targetOffset] = rgba[sourceOffset];
            rgba[targetOffset + 1] = rgba[sourceOffset + 1];
            rgba[targetOffset + 2] = rgba[sourceOffset + 2];
        }

        return EncodePng(width, height, rgba);
    }

    private static DecodedPng DecodePng(byte[] bytes)
    {
        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidOperationException("Pixfix only supports PNG images.");
        }

        var offset = PngSignature.Length;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        var idat = new MemoryStream();

        while (offset + 12 <= bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var dataStart = offset + 8;
            var dataEnd = dataStart + length;

            if (dataEnd + 4 > bytes.Length)
            {
                throw new InvalidOperationException("PNG data is incomplete.");
            }

            if (type == "IHDR")
            {
                if (length != 13)
                {
                    throw new InvalidOperationException("PNG header is invalid.");
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataStart, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataStart + 4, 4)));
                bitDepth = bytes[dataStart + 8];
                colorType = bytes[dataStart + 9];
                interlace = bytes[dataStart + 12];
            }
            else if (type == "IDAT")
            {
                idat.Write(bytes, dataStart, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset = dataEnd + 4;
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("PNG header is missing.");
        }

        if (bitDepth != 8)
        {
            throw new InvalidOperationException("Pixfix currently supports 8-bit PNG images.");
        }

        if (interlace != 0)
        {
            throw new InvalidOperationException("Pixfix currently supports non-interlaced PNG images.");
        }

        var bytesPerPixel = GetBytesPerPixel(colorType);
        idat.Position = 0;
        using var decompressed = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.CopyTo(decompressed);
        }

        var raw = UnfilterScanlines(decompressed.ToArray(), width, height, bytesPerPixel);
        var rgba = new byte[checked(width * height * 4)];

        for (var source = 0, pixel = 0; pixel < width * height; pixel++, source += bytesPerPixel)
        {
            var target = pixel * 4;

            if (colorType == 6)
            {
                rgba[target] = raw[source];
                rgba[target + 1] = raw[source + 1];
                rgba[target + 2] = raw[source + 2];
                rgba[target + 3] = raw[source + 3];
            }
            else if (colorType == 2)
            {
                rgba[target] = raw[source];
                rgba[target + 1] = raw[source + 1];
                rgba[target + 2] = raw[source + 2];
                rgba[target + 3] = 255;
            }
            else if (colorType == 4)
            {
                rgba[target] = raw[source];
                rgba[target + 1] = raw[source];
                rgba[target + 2] = raw[source];
                rgba[target + 3] = raw[source + 1];
            }
            else
            {
                rgba[target] = raw[source];
                rgba[target + 1] = raw[source];
                rgba[target + 2] = raw[source];
                rgba[target + 3] = 255;
            }
        }

        return new DecodedPng(width, height, rgba);
    }

    private static int GetBytesPerPixel(int colorType)
    {
        return colorType switch
        {
            6 => 4,
            2 => 3,
            4 => 2,
            0 => 1,
            _ => throw new InvalidOperationException("Pixfix does not support this PNG color type.")
        };
    }

    private static byte[] UnfilterScanlines(byte[] data, int width, int height, int bytesPerPixel)
    {
        var rowLength = checked(width * bytesPerPixel);
        var expectedLength = checked((rowLength + 1) * height);

        if (data.Length < expectedLength)
        {
            throw new InvalidOperationException("PNG image data is incomplete.");
        }

        var output = new byte[checked(rowLength * height)];
        var sourceOffset = 0;

        for (var y = 0; y < height; y++)
        {
            var filter = data[sourceOffset++];
            var rowOffset = y * rowLength;
            var previousOffset = rowOffset - rowLength;

            for (var x = 0; x < rowLength; x++)
            {
                var raw = data[sourceOffset++];
                var left = x >= bytesPerPixel ? output[rowOffset + x - bytesPerPixel] : 0;
                var up = y > 0 ? output[previousOffset + x] : 0;
                var upLeft = y > 0 && x >= bytesPerPixel ? output[previousOffset + x - bytesPerPixel] : 0;
                var value = filter switch
                {
                    0 => raw,
                    1 => raw + left,
                    2 => raw + up,
                    3 => raw + ((left + up) >> 1),
                    4 => raw + PaethPredictor(left, up, upLeft),
                    _ => throw new InvalidOperationException("Unsupported PNG filter.")
                };

                output[rowOffset + x] = (byte)value;
            }
        }

        return output;
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), checked((uint)height));
        header[8] = 8;
        header[9] = 6;

        var rowLength = checked(width * 4);
        var raw = new byte[checked((rowLength + 1) * height)];

        for (var y = 0; y < height; y++)
        {
            var target = y * (rowLength + 1);
            raw[target] = 0;
            Buffer.BlockCopy(rgba, y * rowLength, raw, target + 1, rowLength);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            compressed = output.ToArray();
        }

        using var png = new MemoryStream();
        png.Write(PngSignature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, checked((uint)data.Length));
        stream.Write(lengthBytes);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(crcInput));
        stream.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;

        foreach (var value in bytes)
        {
            crc = CrcTable[(int)((crc ^ value) & 255)] ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];

        for (var n = 0; n < table.Length; n++)
        {
            var c = (uint)n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private sealed record DecodedPng(int Width, int Height, byte[] Rgba);
}
