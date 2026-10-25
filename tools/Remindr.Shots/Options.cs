namespace Remindr.Shots;

internal sealed class Options
{
    public bool Shots, Hero, Gif, Probe;
    public string[] Only = Array.Empty<string>();
    public string? Exe;
    public string Root = FindRoot();
    public string Scratch = Path.Combine(Path.GetTempPath(), "remindr-shots", "frames");
    public string OutDir = Path.Combine(Path.GetTempPath(), "remindr-shots", "out");
    public string Docs => Path.Combine(Root, "docs");
    public string ProbeTheme = "Light", ProbeLang = "en";
    public bool ProbeEmpty; public string[] Steps = Array.Empty<string>(); public string? DumpFrom; public bool Reduced; public bool ProbeSetup; public int WaitMs = 2500; public string[] AppArgs = Array.Empty<string>(); public int W = Shooter.LogicalW, H = Shooter.LogicalH; public string? EmptyProfile; public string[]? DumpSkip;

    public static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Remindr.sln"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    public string AppExe => Exe ?? FirstExisting(
        Path.Combine(Root, "src", "Remindr", "bin", "Release", "net8.0-windows10.0.19041.0", "Remindr.exe"),
        Path.Combine(Root, "src", "Remindr", "bin", "Debug", "net8.0-windows10.0.19041.0", "Remindr.exe"));

    public string SetupExe => FirstExisting(Path.Combine(Root, "publish", "stub", "Remindr.Setup.exe"),
        Path.Combine(Root, "installer", "Remindr.Setup", "bin", "Release", "net48", "Remindr.Setup.exe"),
        Path.Combine(Root, "installer", "Remindr.Setup", "bin", "Debug", "net48", "Remindr.Setup.exe"));

    private static string FirstExisting(params string[] paths) =>
        paths.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new FileNotFoundException("Build the app first (dotnet build). Looked in: " + string.Join("; ", paths));

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (var i = 0; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--shots": o.Shots = true; break;
                case "--hero": o.Hero = true; break;
                case "--gif": o.Gif = true; break;
                case "--all": o.Shots = o.Hero = o.Gif = true; break;
                case "--probe": o.Probe = true; break;
                case "--steps": o.Steps = a[++i].Split('|'); break;
                case "--dumpskip": o.DumpSkip = a[++i].Split('|'); break;
                case "--setup": o.ProbeSetup = true; break;
                case "--reduced": o.Reduced = true; break;
                case "--wait": o.WaitMs = int.Parse(a[++i]); break;
                case "--size": { var wh = a[++i].Split('x'); o.W = int.Parse(wh[0]); o.H = int.Parse(wh[1]); break; }
                case "--emptyprofile": o.EmptyProfile = a[++i]; break;
                case "--appargs": o.AppArgs = a[++i].Split('|', StringSplitOptions.RemoveEmptyEntries); break;
                case "--dumpfrom": o.DumpFrom = a[++i]; break;
                case "--empty": o.ProbeEmpty = true; break;
                case "--theme": o.ProbeTheme = a[++i]; break;
                case "--lang": o.ProbeLang = a[++i]; break;
                case "--only": o.Only = a[++i].Split(',', StringSplitOptions.RemoveEmptyEntries); break;
                case "--exe": o.Exe = a[++i]; break;
                case "--out": o.OutDir = Path.GetFullPath(a[++i]); break;
                case "--scratch": o.Scratch = a[++i]; break;
            }
        }
        return o;
    }

    public bool Wants(string name) => Only.Length == 0 || Only.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase));
}





