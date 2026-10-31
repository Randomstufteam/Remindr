using Remindr.Shots;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2: real pixels for sizing and capture
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) { Usage(); return 0; }
        var o = Options.Parse(args);
        if (o.Probe) return Scenes.Probe(o);
        var rc = 0;
        if (o.Shots) rc |= ShotScenes.Run(o);
        if (o.Hero) rc |= Hero.Run(o);
        if (o.Gif) rc |= Tour.Run(o);
        if (o.L10n) rc |= L10n.Run(o);
        return rc;
    }

    private static void Usage() => Console.WriteLine("""
        Remindr.Shots - demo screenshots, hero banner and tour GIF (fictional demo data in %TEMP% only)

          dotnet run --project tools\Remindr.Shots -c Release -- --shots [--only name,name] [--out docs\screenshots]
          dotnet run --project tools\Remindr.Shots -c Release -- --hero
          dotnet run --project tools\Remindr.Shots -c Release -- --gif
          dotnet run --project tools\Remindr.Shots -c Debug -- --l10n [--langs vi,my,ar] [--out dir]   (Debug app build; default out docs\l10n-check, gitignored)
          --all runs shots, hero and gif.  --exe <path> uses another Remindr.exe.  --scratch <dir> keeps raw frames (default %TEMP%\remindr-shots).
        """);
}

