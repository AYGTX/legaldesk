using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using SignFlow.Application.Common.Exceptions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// A drawn signature as the server keeps it (R-M5-6): PNG only, at most 500 KB and 1200 × 600 pixels, decoded and written
/// again by the server so nothing but the pixels survives (no metadata, no extra chunks, nothing a viewer might run).
/// Supports what browsers produce from a canvas: 8-bit, non-interlaced, greyscale, RGB, palette or with alpha.
/// </summary>
public static class SignatureImage
{
    public const int MaxBytes = 500 * 1024;
    public const int MaxWidth = 1200;
    public const int MaxHeight = 600;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The re-encoded PNG and its SHA-256 (hex), or a validation error the signer can read.</summary>
    public static (byte[] Png, string Sha256) Sanitize(string base64)
    {
        byte[] bytes;
        try
        {
            // Accepts a data URL ("data:image/png;base64,...") or bare base64.
            bytes = Convert.FromBase64String(base64[(base64.IndexOf(',') + 1)..].Trim());
        }
        catch (FormatException)
        {
            throw Invalid("The signature image could not be read.");
        }

        if (bytes.Length > MaxBytes)
            throw Invalid("The signature image is larger than 500 KB.");

        var (width, height, rgba) = Decode(bytes);
        var png = Encode(width, height, rgba);
        return (png, Convert.ToHexStringLower(SHA256.HashData(png)));
    }

    private static ValidationException Invalid(string message) => new(message);

    private static (int Width, int Height, byte[] Rgba) Decode(byte[] data)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(PngMagic))
            throw Invalid("The signature must be a PNG image.");

        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var idat = new MemoryStream();

        var pos = 8;
        var sawHeader = false;
        while (pos + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (length < 0 || pos + 12 + length > data.Length) throw Invalid("The signature image is damaged.");
            var body = data.AsSpan(pos + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    bitDepth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    sawHeader = true;
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": transparency = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
                case "IEND": pos = data.Length; continue;
            }
            pos += 12 + length;
        }

        if (!sawHeader || width <= 0 || height <= 0) throw Invalid("The signature image is damaged.");
        if (width > MaxWidth || height > MaxHeight) throw Invalid("The signature image is larger than 1200 × 600 pixels.");
        if (bitDepth != 8 || interlace != 0 || colorType is not (0 or 2 or 3 or 4 or 6) || (colorType == 3 && palette is null))
            throw Invalid("This kind of PNG is not supported for signatures; draw the signature again.");

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        try
        {
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            var read = 0;
            while (read < raw.Length)
            {
                var n = z.Read(raw, read, raw.Length - read);
                if (n == 0) throw Invalid("The signature image is damaged.");
                read += n;
            }
        }
        catch (InvalidDataException)
        {
            throw Invalid("The signature image is damaged.");
        }

        var pixels = Unfilter(raw, stride, height, channels);
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            byte r, g, b, a = 255;
            var p = i * channels;
            switch (colorType)
            {
                case 0: r = g = b = pixels[p]; break;
                case 4: r = g = b = pixels[p]; a = pixels[p + 1]; break;
                case 2: r = pixels[p]; g = pixels[p + 1]; b = pixels[p + 2]; break;
                case 3:
                    var index = pixels[p];
                    if (index * 3 + 2 >= palette!.Length) throw Invalid("The signature image is damaged.");
                    r = palette[index * 3]; g = palette[index * 3 + 1]; b = palette[index * 3 + 2];
                    if (transparency is not null && index < transparency.Length) a = transparency[index];
                    break;
                default: r = pixels[p]; g = pixels[p + 1]; b = pixels[p + 2]; a = pixels[p + 3]; break;
            }
            rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a;
        }
        return (width, height, rgba);
    }

    private static byte[] Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        var output = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = y * (stride + 1) + 1;
            var dst = y * stride;
            for (var x = 0; x < stride; x++)
            {
                int a = x >= bpp ? output[dst + x - bpp] : 0;
                int b = y > 0 ? output[dst - stride + x] : 0;
                int c = x >= bpp && y > 0 ? output[dst - stride + x - bpp] : 0;
                int value = raw[src + x];
                output[dst + x] = filter switch
                {
                    0 => (byte)value,
                    1 => (byte)(value + a),
                    2 => (byte)(value + b),
                    3 => (byte)(value + (a + b) / 2),
                    4 => (byte)(value + Paeth(a, b, c)),
                    _ => throw Invalid("The signature image is damaged.")
                };
            }
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>A plain RGBA PNG: IHDR, one IDAT, IEND.</summary>
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(z);

        using var png = new MemoryStream();
        png.Write(PngMagic);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        s.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(body);
        var crc = Crc32([.. typeBytes, .. body]);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>The CRC-32 PNG chunks end with (ISO 3309).</summary>
    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
