namespace Remindr.Shots;

internal static class Scenes
{
    /// <summary>Launches the app on demo data, puts it over the backdrop, saves one grab and prints the UIA tree. For writing scenes.</summary>
    public static int Probe(Options o)
    {
        var dir = Demo.NewTempDir("probe");
        if (o.ProbeSetup) return ProbeSetup(o, dir);
        if (o.ProbeEmpty) Demo.SeedEmpty(dir, o.ProbeTheme, o.ProbeLang); else Demo.SeedProfiles(dir, o.ProbeTheme, o.ProbeLang);
        if (o.EmptyProfile is not null) Demo.AddEmptyProfile(dir, o.EmptyProfile);
        using var bd = new Backdrop();
        bd.Start(o.ProbeTheme == "Dark");
        using var app = AppWindow.Launch(o.AppExe, dir, o.Reduced, o.AppArgs);
        Shooter.Place(app, o.W, o.H);
        app.Settle(o.WaitMs);
        foreach (var s in o.Steps)
        {
            Console.WriteLine("step " + s);
            if (s.StartsWith("expand:")) ((System.Windows.Automation.ExpandCollapsePattern)app.Need(s[7..], type: System.Windows.Automation.ControlType.ComboBox).GetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern)).Expand();
            else if (s.StartsWith("nav:")) Flow.NavIndex(app, s[4] == 'f', s[5] - '0');
            else if (s.StartsWith("edit:")) { Flow.EditCard(app, s[5..]); }
            else if (s.StartsWith("id:")) AppWindow.Activate(app.Need(id: s[3..]));
            else if (s.StartsWith("part:")) AppWindow.Activate(app.Need(s[5..], exact: false));
            else AppWindow.Activate(app.Need(s));
            app.Settle(900);
        }
        var f = Shooter.GrabStable(app);
        Directory.CreateDirectory(o.Scratch);
        var p = Path.Combine(o.Scratch, "probe.png");
        Png.Save(f, p);
        Console.WriteLine($"saved {p} {f.Width}x{f.Height} dpi={Native.GetDpiForWindow(app.Hwnd)}");
        app.Dump(o.DumpFrom is null ? null : app.Need(id: o.DumpFrom), 12, o.DumpSkip);
        return 0;
    }

    public static int ProbeSetup(Options o, string dir)
    {
        using var bd = new Backdrop();
        bd.Start(false);
        using var app = AppWindow.Launch(o.SetupExe, dir, o.Reduced, "--demo");
        app.Settle(o.WaitMs);
        foreach (var s in o.Steps) { Console.WriteLine("step " + s); AppWindow.Activate(app.Need(s)); app.Settle(900); }
        Native.SetWindowPos(app.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0003);
        var f = Shooter.Grab(app, false);
        var p = Path.Combine(o.Scratch, "probe.png");
        Png.Save(f, p);
        Console.WriteLine($"saved {p} {f.Width}x{f.Height}");
        app.Dump(null, 12, o.DumpSkip);
        return 0;
    }

}






internal static class Hero { public static int Run(Options o) => 0; }


