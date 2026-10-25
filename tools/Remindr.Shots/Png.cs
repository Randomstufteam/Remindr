using System.IO.Compression;

namespace Remindr.Shots;

/// <summary>
/// Small lossless PNG writer: 8-bit RGBA (or RGB when fully opaque), per-row best filter, smallest zlib.
/// Chunks other than IHDR/IDAT/IEND are never written, so no metadata (paths, times, names) ends up in the file.
/// </summary>
internal static class Png
{
    public static void Save(Frame f, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode(f));
    }

    public static byte[] Encode(Frame f)
    {
        var opaque = true;
        for (var i = 3; i < f.Bgra.Length; i += 4) if (f.Bgra[i] != 255) { opaque = false; break; }
        var bpp = opaque ? 3 : 4;
        var stride = f.Width * bpp;

        var rows = new byte[f.Height][];
        for (var y = 0; y < f.Height; y++)
        {
            var row = new byte[stride];
            for (var x = 0; x < f.Width; x++)
            {
                var s = (y * f.Width + x) * 4;
                row[x * bpp] = f.Bgra[s + 2]; row[x * bpp + 1] = f.Bgra[s + 1]; row[x * bpp + 2] = f.Bgra[s];
                if (!opaque) row[x * bpp + 3] = f.Bgra[s + 3];
            }
            rows[y] = row;
        }

        var raw = new byte[(stride + 1) * f.Height];
        var cand = new byte[5][];
        for (var i = 0; i < 5; i++) cand[i] = new byte[stride];
        var zero = new byte[stride];
        for (var y = 0; y < f.Height; y++)
        {
            var cur = rows[y];
            var prev = y == 0 ? zero : rows[y - 1];
            long best = long.MaxValue; var bestF = 0;
            for (var ft = 0; ft < 5; ft++)
            {
                long sum = 0;
                var o = cand[ft];
                for (var i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int pred = ft switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, c) };
                    var v = (byte)(cur[i] - pred);
                    o[i] = v;
                    sum += v < 128 ? v : 256 - v;
                }
                if (sum < best) { best = sum; bestF = ft; }
            }
            raw[y * (stride + 1)] = (byte)bestF;
            Buffer.BlockCopy(cand[bestF], 0, raw, y * (stride + 1) + 1, stride);
        }

        byte[] z;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) zs.Write(raw, 0, raw.Length);
            z = ms.ToArray();
        }

        using var o2 = new MemoryStream();
        o2.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, f.Width); WriteBE(ihdr, 4, f.Height);
        ihdr[8] = 8; ihdr[9] = (byte)(opaque ? 2 : 6);
        Chunk(o2, "IHDR", ihdr);
        Chunk(o2, "IDAT", z);
        Chunk(o2, "IEND", Array.Empty<byte>());
        return o2.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(System.IO.Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        crc ^= 0xFFFFFFFF;
        var c = new byte[4]; WriteBE(c, 0, (int)crc); s.Write(c);
    }

    private static readonly uint[] Table = MakeTable();
    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static Frame Load(string path)
    {
        var dec = new System.Windows.Media.Imaging.PngBitmapDecoder(new Uri(Path.GetFullPath(path)),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var f = new Frame(src.PixelWidth, src.PixelHeight);
        src.CopyPixels(f.Bgra, src.PixelWidth * 4, 0);
        return f;
    }
}

