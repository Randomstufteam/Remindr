using System;
using System.IO;

namespace Remindr.Setup.Core;

/// <summary>Appends to %TEMP%\Remindr-setup.log. Never throws.</summary>
public sealed class FileSetupLog : ISetupLog
{
    private readonly object _gate = new object();

    public FileSetupLog(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Remindr-setup.log");
    }

    public string Path { get; }

    public void Info(string message) => Write("INFO  " + message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR " + message + (ex is null ? "" : Environment.NewLine + ex));

    private void Write(string line)
    {
        try
        {
            lock (_gate)
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
        }
        catch { /* logging must never break setup */ }
    }
}
