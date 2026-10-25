using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Remindr.Services;

/// <summary>
/// One Remindr per user. The first process owns a named mutex and listens on a named pipe; a later process sends its
/// arguments down the pipe, waits for the "ok" the running instance answers once its UI thread has handled them, and exits.
/// No answer in time (the first instance is frozen or still shutting down) makes <see cref="SendToRunningAsync"/> return false.
/// Both names include the user's SID, and the pipe is current-user-only, so another account's Remindr is never reached.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HandleTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _server;

    public SingleInstance(string id)
    {
        _mutexName = @"Local\Remindr.App." + id;
        _pipeName = "Remindr.App." + id;
    }

    /// <summary>The id for this user: the Windows SID, plus a short hash of the data folder when REMINDR_DATA_DIR moves it (so test data folders do not collide with the real app).</summary>
    public static string DefaultId()
    {
        string sid;
        try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName; }
        catch { sid = Environment.UserName; }
        var paths = AppPaths.Current;
        if (!paths.IsOverridden) return sid;
        // Case and a trailing slash do not make a different folder.
        var dir = paths.DataDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dir));
        return sid + "." + Convert.ToHexString(hash, 0, 4);
    }

    /// <summary>True when this process is the first instance (it now owns the mutex). Any failure counts as "first": better two windows than none.</summary>
    public bool TryBecomePrimary()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out var created);
            if (created) return true;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Creating the single-instance mutex");
            return true;
        }
    }

    /// <summary>
    /// After a failed hand-over: waits up to <paramref name="timeout"/> for the running copy to go away (it is closing, e.g. for an
    /// update, or it crashed) and takes over. A mutex its owner left without releasing (process or thread ended) counts as taken over.
    /// </summary>
    public bool WaitToBecomePrimary(TimeSpan timeout)
    {
        try
        {
            _mutex ??= new Mutex(false, _mutexName);
            if (_mutex.WaitOne(timeout)) return true;
        }
        catch (AbandonedMutexException) { return true; }
        catch (Exception ex) { ErrorLog.Write(ex, "Waiting for the single-instance mutex"); return false; }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>
    /// Starts listening. <paramref name="handler"/> gets each decoded message (on a pool thread) and returns when it was handled;
    /// the sender is told "ok" only if that happens within a couple of seconds.
    /// </summary>
    public void StartServer(Func<string[], Task> handler)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _server = Task.Run(() => ServerLoop(handler, ct));
    }

    private async Task ServerLoop(Func<string[], Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                try
                {
                    using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    read.CancelAfter(ReadTimeout);
                    var line = await ReadLineAsync(pipe, SingleInstanceRules.MaxMessageBytes, read.Token).ConfigureAwait(false);
                    var args = SingleInstanceRules.Decode(line);
                    if (args is null) continue;
                    await handler(args).WaitAsync(HandleTimeout, ct).ConfigureAwait(false);
                    await pipe.WriteAsync(Ok, ct).ConfigureAwait(false);
                    await pipe.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
                {
                    if (ct.IsCancellationRequested) return;
                    // A client that was too slow, vanished or got no answer: drop it and listen again.
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "Single-instance pipe");
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private static readonly byte[] Ok = Encoding.ASCII.GetBytes("ok\n");

    /// <summary>
    /// Sends <paramref name="args"/> to the running instance and waits for its "ok". False on any failure or when nothing
    /// answers within <paramref name="timeout"/> (default 3 s). Never throws.
    /// </summary>
    public async Task<bool> SendToRunningAsync(IEnumerable<string> args, TimeSpan? timeout = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? SendTimeout);
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            var data = Encoding.UTF8.GetBytes(SingleInstanceRules.Encode(args) + "\n");
            await pipe.WriteAsync(data, cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
            var reply = await ReadLineAsync(pipe, 16, cts.Token).ConfigureAwait(false);
            return reply == "ok";
        }
        catch { return false; }
    }

    /// <summary>Reads up to a newline or <paramref name="maxBytes"/> (whichever first); null when nothing arrived or the line was too long.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count <= maxBytes)
        {
            if (await stream.ReadAsync(one, ct).ConfigureAwait(false) == 0) break;
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(one[0]);
        }
        return null;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* shutting down */ }
        // Do not release the mutex here: Dispose may run on another thread than the one that created it. The OS frees it at exit.
        try { _mutex?.Dispose(); } catch { /* shutting down */ }
        _mutex = null;
    }
}
