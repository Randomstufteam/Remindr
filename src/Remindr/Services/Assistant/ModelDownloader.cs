using System.IO;
using System.Net;
using System.Net.Http;

namespace Remindr.Services.Assistant;

public enum ModelDownloadResult { Completed, HashMismatch }

/// <summary>Not enough free disk space for the file.</summary>
public sealed class NotEnoughSpaceException : IOException
{
    public NotEnoughSpaceException() : base("Not enough free disk space for the model.") { }
}

/// <summary>
/// Downloads a model file into <c>&lt;file&gt;.part</c> and renames it only after the SHA-256 matches. Resumes a leftover .part with
/// <c>Range: bytes=n-</c> (a plain 200 answer means the server ignored it, so it starts over). Redirects are followed by hand and every hop
/// must be https on the Hugging Face allow-list (<see cref="ModelCatalog.IsAllowedHost"/>). No cookies, no credentials. Pausing is the caller
/// cancelling the token (the .part stays); Cancel is the caller deleting the .part afterwards. UI-free; tests pass a fake handler.
/// </summary>
public sealed class ModelDownloader
{
    const int MaxHops = 5;
    const long SpaceMargin = 256L << 20;
    readonly HttpClient _http;
    readonly Func<Uri, bool> _hostOk;
    readonly Func<string, long> _freeSpace;
    readonly TimeSpan _readTimeout;

    public ModelDownloader(HttpMessageHandler handler, Func<Uri, bool>? hostOk = null, Func<string, long>? freeSpace = null, TimeSpan? readTimeout = null)
    {
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Remindr");
        _hostOk = hostOk ?? ModelCatalog.IsAllowedHost;
        _freeSpace = freeSpace ?? FreeSpaceOf;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>A handler that does not follow redirects itself, so every hop is checked.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true };

    public static string PartPath(string modelsDir, ModelTier tier) => ModelCatalog.PathOf(tier, modelsDir) + ".part";

    /// <summary>Where to continue a partial file of <paramref name="partLength"/> bytes: its length, or 0 (start over) when it is empty or not smaller than the file.</summary>
    public static long ResumeOffset(long partLength, long total) => partLength > 0 && partLength < total ? partLength : 0;

    static long FreeSpaceOf(string dir) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace;

    public async Task<ModelDownloadResult> DownloadAsync(ModelTier tier, Uri url, string modelsDir, Action<long>? progress, Action? verifying, CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDir);
        var part = PartPath(modelsDir, tier);
        var offset = File.Exists(part) ? ResumeOffset(new FileInfo(part).Length, tier.Bytes + 1) : 0;
        if (offset < tier.Bytes)
        {
            if (_freeSpace(modelsDir) < tier.Bytes - offset + SpaceMargin) throw new NotEnoughSpaceException();
            await FetchAsync(tier, url, part, offset, progress, ct);
        }

        verifying?.Invoke();
        var actual = await Task.Run(() => UpdateService.HashFile(part), ct);
        if (!string.Equals(actual, tier.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            return ModelDownloadResult.HashMismatch;
        }
        File.Move(part, ModelCatalog.PathOf(tier, modelsDir), overwrite: true);
        return ModelDownloadResult.Completed;
    }

    async Task FetchAsync(ModelTier tier, Uri url, string part, long offset, Action<long>? progress, CancellationToken ct)
    {
        var response = await SendAsync(url, offset, ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            response.Dispose();
            offset = 0;
            response = await SendAsync(url, 0, ct);
        }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            // 206 from where we asked: append. Anything else (a plain 200): the server sent the whole file, start over.
            if (!(response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset)) offset = 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            file.SetLength(offset);
            file.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[81920];
            var done = offset;
            int read;
            while ((read = await ReadAsync(source, buffer, ct)) > 0)
            {
                done += read;
                if (done > tier.Bytes) throw new InvalidDataException("The download is larger than expected.");
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Invoke(done);
            }
            await file.FlushAsync(ct);
            if (done != tier.Bytes) throw new IOException("The download ended early.");
        }
    }

    // A stalled connection (cable pulled) would otherwise hang forever: no data for a while counts as a lost connection.
    async Task<int> ReadAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(_readTimeout);
        try { return await source.ReadAsync(buffer, stall.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("The connection timed out."); }
    }

    async Task<HttpResponseMessage> SendAsync(Uri url, long offset, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (!_hostOk(current)) throw new UpdateSecurityException("Redirect to a host that is not allowed: " + current.Host);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Too many redirects.");
    }
}
