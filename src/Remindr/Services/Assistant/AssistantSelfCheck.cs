using System.Diagnostics;
using System.IO;
using System.Text;

namespace Remindr.Services.Assistant;

/// <summary>
/// <c>Remindr.exe --assistant-check &lt;result-file&gt; &lt;model-file&gt; [auto|on|off]</c>: loads the model the way the app does, answers one phrase and writes what happened
/// (device, timings, the guarded call, or the error) to the file, then exits. It proves the native llama.cpp files load from the
/// installed (single-file) app without opening a window, and needs no profile or database.
/// </summary>
public static class AssistantSelfCheck
{
    public const string Arg = "--assistant-check";

    /// <param name="gpu">auto, on or off (the "Use graphics card" setting); the report names the device the model really ended up on.</param>
    public static int Run(string resultFile, string modelPath, GpuMode gpu = GpuMode.Auto)
    {
        if (modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) return VoiceCheck(resultFile, modelPath);
        var report = new StringBuilder();
        var code = 0;
        try
        {
            var path = modelPath;
            report.AppendLine("gpu setting: " + GpuPolicy.Format(gpu));
            report.AppendLine("base directory: " + AppContext.BaseDirectory);
            report.AppendLine("model: " + path + " (exists: " + File.Exists(path) + ")");
            var sw = Stopwatch.StartNew();
            using var model = AssistantEngine.Load(path, gpu);
            report.AppendLine($"loaded in {sw.Elapsed.TotalSeconds:F1}s, device: {model.Device ?? "CPU"}{(model.NoteKey is { } n ? " (note: " + n + ")" : "")}");
            foreach (var line in AssistantEngine.CapturedGpuLines) report.AppendLine("llama.cpp: " + line);
            sw.Restart();
            const string phrase = "remind me to call grandma sunday 5pm, it's important";
            var (raw, final) = AssistantResolver.ResolveAsync(phrase, p => model.InferAsync(p)).GetAwaiter().GetResult();
            report.AppendLine($"answered in {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine("raw: " + raw);
            report.AppendLine("guarded: " + final);
            report.AppendLine(final.Contains("call grandma") && final.Contains("sunday") ? "RESULT: OK" : "RESULT: UNEXPECTED ANSWER");
        }
        catch (Exception ex)
        {
            report.AppendLine("RESULT: FAILED " + ex);
            foreach (var line in AssistantEngine.CapturedGpuLines) report.AppendLine("llama.cpp: " + line);
            code = 2;
        }
        File.WriteAllText(resultFile, report.ToString());
        return code;
    }

    /// <summary>A Kokoro .onnx file: loads it and the shipped voices the way the app does and renders one reply, without playing it.</summary>
    private static int VoiceCheck(string resultFile, string modelPath)
    {
        var report = new StringBuilder("base directory: " + AppContext.BaseDirectory + Environment.NewLine);
        var code = 0;
        try
        {
            var speaker = new NaturalSpeaker(() => modelPath, new WindowsSpeaker());
            var sw = Stopwatch.StartNew();
            const string text = "Done. I set a reminder to call grandma on Sunday at 5 PM.";
            var pcm = speaker.RenderAsync(modelPath, text, null).GetAwaiter().GetResult();
            report.AppendLine($"rendered {pcm.Length / 48000.0:F1}s of audio in {sw.Elapsed.TotalSeconds:F1}s (first use, includes loading)");
            sw.Restart();
            pcm = speaker.RenderAsync(modelPath, text, KokoroCatalog.Prefix + "am_michael").GetAwaiter().GetResult();
            report.AppendLine($"again with Michael: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine("peak RAM MB: " + Process.GetCurrentProcess().PeakWorkingSet64 / 1048576);
            report.AppendLine(pcm.Length > 48000 ? "RESULT: OK" : "RESULT: NO AUDIO");
            speaker.Unload();
        }
        catch (Exception ex) { report.AppendLine("FAILED: " + ex); code = 1; }
        File.WriteAllText(resultFile, report.ToString());
        return code;
    }
}
