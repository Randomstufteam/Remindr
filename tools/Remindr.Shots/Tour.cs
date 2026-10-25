using System.Diagnostics;
using System.Windows.Automation;

namespace Remindr.Shots;

/// <summary>Grabs the app window on a background thread while the main thread drives the UI; identical neighbours are merged into one longer-lived frame.</summary>
internal sealed class Recorder
{
    private readonly AppWindow _app;
    private readonly int _w, _h;
    private readonly List<(Frame F, long T)> _frames = new();
    private readonly Stopwatch _sw = new();
    private Thread? _thread;
    private volatile bool _run;
    private long _endMs;

    public Recorder(AppWindow app, int width = 900)
    {
        _app = app; _w = width; _h = (int)Math.Round(width * 800.0 / 1280.0);
    }

    public void Start()
    {
        Shooter.Raise(_app);
        _run = true;
        _sw.Restart();
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
    }

    private void Loop()
    {
        while (_run)
        {
            var t0 = _sw.ElapsedMilliseconds;
            try
            {
                var f = Shooter.Grab(_app, false, _w, _h);
                f.RoundCorners(8.0 * _w / 1280.0);
                lock (_frames)
                {
                    if (_frames.Count == 0 || f.MeanDiff(_frames[^1].F) > 0.06) _frames.Add((f, t0));
                }
            }
            catch (Exception) { /* window busy while it re-layouts: try again */ }
            var spent = _sw.ElapsedMilliseconds - t0;
            if (spent < 70) Thread.Sleep((int)(70 - spent));
        }
    }

    /// <summary>Stops and returns the frames; the last one stays up for <paramref name="tailMs"/>.</summary>
    public List<GifFrame> Stop(int tailMs)
    {
        _run = false;
        _thread?.Join();
        _endMs = _sw.ElapsedMilliseconds;
        var list = new List<GifFrame>();
        lock (_frames)
        {
            for (var i = 0; i < _frames.Count; i++)
            {
                var next = i + 1 < _frames.Count ? _frames[i + 1].T : _endMs + tailMs;
                list.Add(new GifFrame(_frames[i].F, (int)Math.Max(20, next - _frames[i].T)));
            }
        }
        return list;
    }
}

internal static class Tour
{
    public static int Run(Options o)
    {
        var frames = new List<GifFrame>();
        using (var rig = new Rig(o, "Light"))
        {
            Console.WriteLine("Segment 1: welcome and setup");
            using (var app = rig.OpenApp(empty: true, reduced: false))
            {
                var rec = new Recorder(app);
                rec.Start();
                Thread.Sleep(2700);                                   // greeting cycles through languages
                AppWindow.Activate(app.Need(id: "WelcomeStart"));
                var search = app.Need(id: "SetupLanguageSearch");
                Thread.Sleep(252);
                AppWindow.SetText(search, "Japan");                   // the language switches live
                Thread.Sleep(324);
                AppWindow.Activate(app.Need("Japanese", type: ControlType.ListItem, exact: false));
                Thread.Sleep(792);
                AppWindow.SetText(app.Need(id: "SetupLanguageSearch"), "Engl");
                Thread.Sleep(288);
                AppWindow.Activate(app.Need("English", type: ControlType.ListItem, exact: false));
                Thread.Sleep(360);
                AppWindow.Activate(app.Need(id: "SetupNext"));        // Appearance
                Thread.Sleep(576);
                AppWindow.Activate(app.Need(id: "SetupNext"));        // Profile
                Thread.Sleep(360);
                AppWindow.SetText(app.Need(id: "SetupName"), "Maya");
                Thread.Sleep(252);
                AppWindow.Activate(app.Need("#8B5CF6", within: app.Need(id: "SetupColors")));
                Thread.Sleep(252);
                var picker = app.Need(id: "Picker");
                AppWindow.Activate(app.Need("Family and roles", type: ControlType.Button, within: picker));
                Thread.Sleep(252);
                AppWindow.Activate(app.Need("Student", type: ControlType.Button, within: picker));
                Thread.Sleep(936);
                frames.AddRange(rec.Stop(300));
            }

            Console.WriteLine("Segment 2: calendar, edit dialog, language switch");
            using (var app = rig.OpenApp(empty: false, reduced: false))
            {
                Flow.EnterProfile(app, "Maya");
                Flow.NavIndex(app, false, 1);
                var day = DateTime.Today.AddDays(2).ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                AppWindow.Activate(app.Need(day, type: ControlType.Button, exact: false));
                Thread.Sleep(648);
                var rec = new Recorder(app);
                rec.Start();
                Thread.Sleep(792);                                   // Month + day panel
                AppWindow.Activate(app.Need("Week view"));
                Thread.Sleep(720);
                AppWindow.Activate(app.Need("Agenda view"));
                Thread.Sleep(720);
                Flow.EditCard(app, "Math test: chapter 5");           // edit dialog
                Thread.Sleep(504);
                Flow.OpenPicturePopup(app);                           // picture popup
                Thread.Sleep(324);
                AppWindow.Activate(app.Need("School", type: ControlType.Button, within: app.Need(id: "Picker")));
                Thread.Sleep(792);
                Flow.CancelDialog(app);
                Thread.Sleep(252);
                frames.AddRange(rec.Stop(600));
            }
        }

        Console.WriteLine($"{frames.Count} frames, {frames.Sum(f => f.DelayMs) / 1000.0:0.0} s");
        Directory.CreateDirectory(o.Scratch);
        // Keep a few source frames for visual checking.
        for (var i = 0; i < frames.Count; i += Math.Max(1, frames.Count / 12))
            Png.Save(frames[i].Pixels, Path.Combine(o.Scratch, $"tour-src-{i:000}.png"));
        var path = Path.Combine(o.OutDir, "tour.gif");
        Directory.CreateDirectory(o.OutDir);
        var sw = Stopwatch.StartNew();
        GifWriter.Write(path, frames);
        Console.WriteLine($"tour.gif {new FileInfo(path).Length / 1024} KB ({sw.Elapsed.TotalSeconds:0.0}s to encode)");
        return 0;
    }
}


