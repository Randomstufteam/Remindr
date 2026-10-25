using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Remindr.Setup.Core;

/// <summary>
/// Layout of Remindr-Installer.exe: [stub exe][payload.zip][16-byte footer].
/// Footer = payload offset (int64 little-endian, = length of the stub) + ASCII magic "RMDRPAY1".
/// uninstall.exe is just the first <see cref="StubLength"/> bytes (the stub without payload or footer).
/// </summary>
public sealed class PayloadInfo
{
    public const int FooterSize = 16;
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("RMDRPAY1");

    public PayloadInfo(long stubLength, long zipLength)
    {
        StubLength = stubLength;
        ZipLength = zipLength;
    }

    /// <summary>Offset where the zip starts, which is also the size of the stub.</summary>
    public long StubLength { get; }
    public long ZipLength { get; }

    public static byte[] BuildFooter(long stubLength)
    {
        var f = new byte[FooterSize];
        BitConverter.GetBytes(stubLength).CopyTo(f, 0);   // little-endian on every Windows target
        Magic.CopyTo(f, 8);
        return f;
    }

    /// <summary>Null when the file has no valid footer (e.g. the bare stub, or uninstall.exe).</summary>
    public static PayloadInfo? TryRead(Stream exe)
    {
        if (!exe.CanSeek || exe.Length < FooterSize) return null;
        exe.Seek(-FooterSize, SeekOrigin.End);
        var f = new byte[FooterSize];
        var read = 0;
        while (read < FooterSize)
        {
            var n = exe.Read(f, read, FooterSize - read);
            if (n <= 0) return null;
            read += n;
        }
        for (var i = 0; i < Magic.Length; i++)
            if (f[8 + i] != Magic[i]) return null;

        var offset = BitConverter.ToInt64(f, 0);
        var zipLength = exe.Length - FooterSize - offset;
        if (offset <= 0 || zipLength <= 0) return null;
        return new PayloadInfo(offset, zipLength);
    }
}

/// <summary>Read-only seekable window over part of another stream (lets ZipArchive read the appended zip in place).</summary>
public sealed class SubStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _pos;

    public SubStream(Stream inner, long start, long length)
    {
        _inner = inner;
        _start = start;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _pos; set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - _pos;
        if (remaining <= 0) return 0;
        _inner.Seek(_start + _pos, SeekOrigin.Begin);
        var n = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
        _pos += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : _length + offset;
        if (target < 0) throw new IOException("Seek before start.");
        _pos = target;
        return _pos;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public static class ZipExtractor
{
    /// <summary>
    /// Extracts every file entry under destDir (created if missing). Refuses entries that would land outside destDir.
    /// Returns the total uncompressed bytes written. onBytes gets the running total.
    /// </summary>
    public static long ExtractAll(ZipArchive zip, string destDir, Action<long, long>? onBytes = null)
    {
        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        long total = 0;
        foreach (var e in zip.Entries) total += e.Length;

        long done = 0;
        var buffer = new byte[81920];
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName.Replace('/', '\\')));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload entry escapes the install folder: " + entry.FullName);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var src = entry.Open())
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, n);
                    done += n;
                    onBytes?.Invoke(done, total);
                }
            }
        }
        return done;
    }
}
