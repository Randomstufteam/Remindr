using System.IO;
using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Remindr.Services.Assistant;

/// <summary>Speech to text with Whisper.net (whisper.cpp, CPU runtime) and the ggml-base.en model. The model loads on first use and is freed by <see cref="Unload"/>.</summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly Func<string?> _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;

    public WhisperTranscriber(Func<string?> modelPath) => _modelPath = modelPath;

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_factory is null)
            {
                var path = _modelPath();
                if (path is null || !File.Exists(path)) throw new FileNotFoundException("The voice model is not installed.");
                // CPU runtime only (Whisper.net.Runtime); no probing for GPU runtimes that are not shipped.
                RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Cpu };
                _factory = await Task.Run(() => WhisperFactory.FromPath(path), ct).ConfigureAwait(false);
            }
            await using var processor = _factory.CreateBuilder().WithLanguage("en").Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct).ConfigureAwait(false)) text.Append(segment.Text);
            return text.ToString();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Frees the model (voice turned off or the file removed).</summary>
    public void Unload()
    {
        _gate.Wait();
        try { _factory?.Dispose(); _factory = null; }
        finally { _gate.Release(); }
    }
}
