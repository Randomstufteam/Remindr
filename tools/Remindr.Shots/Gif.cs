namespace Remindr.Shots;

/// <summary>One picture of the animation (already at output size) and how long it stays up.</summary>
internal sealed record GifFrame(Frame Pixels, int DelayMs);

/// <summary>
/// Dependency-free animated GIF writer: one shared 255-colour palette (median cut over all frames), ordered (Bayer) dithering so an
/// unchanged source pixel always maps to the same index, and "changed rectangle + transparent unchanged pixels" frames so static
/// stretches cost almost nothing. Fully transparent source pixels (rounded window corners) stay transparent.
/// </summary>
internal static class GifWriter
{
    private const int Transparent = 255;

    // 8x8 Bayer matrix, values 0..63.
    private static readonly int[] Bayer =
    {
         0, 32,  8, 40,  2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44,  4, 36, 14, 46,  6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
         3, 35, 11, 43,  1, 33,  9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47,  7, 39, 13, 45,  5, 37,
        63, 31, 55, 23, 61, 29, 53, 21
    };

    public static void Write(string path, IReadOnlyList<GifFrame> frames, double ditherSpread = 14, int tolerance = 7)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames.");
        int w = frames[0].Pixels.Width, h = frames[0].Pixels.Height;
        var palette = BuildPalette(frames.Select(f => f.Pixels));
        var lookup = new byte[1 << 18];
        for (var i = 0; i < lookup.Length; i++) lookup[i] = 255; // 255 = not computed (palette index 255 is never a real colour)

        byte Nearest(int r, int g, int b)
        {
            r = Math.Clamp(r, 0, 255); g = Math.Clamp(g, 0, 255); b = Math.Clamp(b, 0, 255);
            var key = ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
            var v = lookup[key];
            if (v != 255) return v;
            int rr = (r & 0xFC) | 2, gg = (g & 0xFC) | 2, bb = (b & 0xFC) | 2;
            var best = 0; var bestD = int.MaxValue;
            for (var i = 0; i < 255; i++)
            {
                int dr = palette[i * 3] - rr, dg = palette[i * 3 + 1] - gg, db = palette[i * 3 + 2] - bb;
                var d = 2 * dr * dr + 4 * dg * dg + 3 * db * db; // weighted towards green, like the eye
                if (d < bestD) { bestD = d; best = i; }
            }
            lookup[key] = (byte)best;
            return (byte)best;
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        bw.Write((ushort)w); bw.Write((ushort)h);
        bw.Write((byte)(0x80 | 0x70 | 7)); // global colour table, 8 bits, 256 entries
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write(palette);
        // looping forever
        bw.Write(new byte[] { 0x21, 0xFF, 0x0B });
        bw.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        bw.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

        var canvas = new byte[w * h];                // palette indices currently on screen
        var shownSrc = new int[w * h];               // source colour that produced each canvas pixel (r<<16|g<<8|b), -1 = transparent
        Array.Fill(shownSrc, -2);
        var first = true;

        foreach (var fr in frames)
        {
            var px = fr.Pixels.Bgra;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            var changed = new bool[w * h];
            var idx = new byte[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var p = y * w + x;
                    var s = p * 4;
                    if (px[s + 3] < 128)
                    {
                        idx[p] = Transparent;
                        if (first) { changed[p] = false; shownSrc[p] = -1; }
                        continue;
                    }
                    int b = px[s], g = px[s + 1], r = px[s + 2];
                    var src = (r << 16) | (g << 8) | b;
                    var prev = shownSrc[p];
                    if (prev >= 0)
                    {
                        int pr = prev >> 16, pg = (prev >> 8) & 255, pb = prev & 255;
                        if (Math.Abs(pr - r) + Math.Abs(pg - g) + Math.Abs(pb - b) <= tolerance) continue; // keep what is on screen
                    }
                    var t = (Bayer[(y & 7) * 8 + (x & 7)] - 31.5) / 64.0 * ditherSpread;
                    idx[p] = Nearest((int)Math.Round(r + t), (int)Math.Round(g + t), (int)Math.Round(b + t));
                    if (canvas[p] == idx[p] && prev >= 0) { shownSrc[p] = src; continue; }
                    canvas[p] = idx[p];
                    shownSrc[p] = src;
                    changed[p] = true;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            first = false;

            if (maxX < 0) { minX = minY = 0; maxX = maxY = 0; } // nothing changed: a 1x1 transparent pixel keeps the timing
            int fw = maxX - minX + 1, fh = maxY - minY + 1;
            var data = new byte[fw * fh];
            for (var y = 0; y < fh; y++)
                for (var x = 0; x < fw; x++)
                {
                    var p = (minY + y) * w + minX + x;
                    data[y * fw + x] = changed[p] ? canvas[p] : (byte)Transparent;
                }

            var delayCs = Math.Max(2, (int)Math.Round(fr.DelayMs / 10.0));
            bw.Write(new byte[] { 0x21, 0xF9, 0x04, 0x05 }); // graphic control: disposal 1 (leave in place) + transparency
            bw.Write((ushort)delayCs);
            bw.Write((byte)Transparent); bw.Write((byte)0);
            bw.Write((byte)0x2C);
            bw.Write((ushort)minX); bw.Write((ushort)minY); bw.Write((ushort)fw); bw.Write((ushort)fh);
            bw.Write((byte)0);
            bw.Write((byte)8);
            WriteLzw(bw, data);
        }
        bw.Write((byte)0x3B);
    }

    private static void WriteLzw(BinaryWriter bw, byte[] data)
    {
        const int minCode = 8, clear = 256, eoi = 257;
        var dict = new Dictionary<int, int>(8192);
        var codeSize = minCode + 1;
        var next = eoi + 1;
        var bitBuf = 0; var bitCnt = 0;
        var block = new List<byte>(255 + 4);
        void Emit(int code)
        {
            bitBuf |= code << bitCnt; bitCnt += codeSize;
            while (bitCnt >= 8)
            {
                block.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCnt -= 8;
                if (block.Count == 255) { bw.Write((byte)255); bw.Write(block.ToArray()); block.Clear(); }
            }
        }
        Emit(clear);
        int cur = data[0];
        for (var i = 1; i < data.Length; i++)
        {
            var c = data[i];
            var key = (cur << 8) | c;
            if (dict.TryGetValue(key, out var found)) { cur = found; continue; }
            Emit(cur);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear);
                dict.Clear(); next = eoi + 1; codeSize = minCode + 1;
            }
            cur = c;
        }
        Emit(cur);
        Emit(eoi);
        if (bitCnt > 0) block.Add((byte)(bitBuf & 0xFF));
        if (block.Count > 0) { bw.Write((byte)block.Count); bw.Write(block.ToArray()); }
        bw.Write((byte)0);
    }

    /// <summary>Median-cut palette of 255 colours (entry 255 is the transparent index) from a sample of every frame.</summary>
    private static byte[] BuildPalette(IEnumerable<Frame> frames)
    {
        // Histogram on 5 bits per channel, but remember the real colour sums so pure white / near-black stay exact.
        var hist = new Dictionary<int, (long N, long R, long G, long B)>();
        foreach (var f in frames)
        {
            var b = f.Bgra;
            for (var i = 0; i < b.Length; i += 4 * 5)
            {
                if (b[i + 3] < 128) continue;
                var key = ((b[i + 2] >> 3) << 10) | ((b[i + 1] >> 3) << 5) | (b[i] >> 3);
                var e = hist.GetValueOrDefault(key);
                hist[key] = (e.N + 1, e.R + b[i + 2], e.G + b[i + 1], e.B + b[i]);
            }
        }
        var boxes = new List<List<KeyValuePair<int, (long N, long R, long G, long B)>>> { hist.ToList() };
        static int Ch(int key, int c) => c == 0 ? key >> 10 : c == 1 ? (key >> 5) & 31 : key & 31;
        while (boxes.Count < 255)
        {
            var bi = -1; double bs = 0; var bc = 0;
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                var pop = Math.Sqrt(boxes[i].Sum(k => k.Value.N));
                for (var c = 0; c < 3; c++)
                {
                    int lo = 31, hi = 0;
                    foreach (var kv in boxes[i]) { var v = Ch(kv.Key, c); if (v < lo) lo = v; if (v > hi) hi = v; }
                    var score = (hi - lo) * pop * (c == 1 ? 1.2 : 1.0);
                    if (score > bs) { bs = score; bi = i; bc = c; }
                }
            }
            if (bi < 0) break;
            var box = boxes[bi];
            box.Sort((a, b) => Ch(a.Key, bc).CompareTo(Ch(b.Key, bc)));
            var half = box.Sum(k => k.Value.N) / 2; long acc = 0; var cut = box.Count - 1;
            for (var i = 0; i < box.Count - 1; i++) { acc += box[i].Value.N; if (acc >= half) { cut = i + 1; break; } }
            boxes[bi] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }
        var pal = new byte[256 * 3];
        for (var i = 0; i < boxes.Count && i < 255; i++)
        {
            long n = 0, r = 0, g = 0, b = 0;
            foreach (var kv in boxes[i]) { n += kv.Value.N; r += kv.Value.R; g += kv.Value.G; b += kv.Value.B; }
            pal[i * 3] = (byte)(r / n); pal[i * 3 + 1] = (byte)(g / n); pal[i * 3 + 2] = (byte)(b / n);
        }
        return pal;
    }
}

