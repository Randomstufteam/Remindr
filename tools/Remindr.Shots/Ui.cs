using System.Diagnostics;
using System.Windows.Automation;

namespace Remindr.Shots;

/// <summary>A running Remindr (or installer) process driven only through UI Automation patterns. No keyboard or mouse input is ever sent.</summary>
internal sealed class AppWindow : IDisposable
{
    public Process Process { get; }
    public IntPtr Hwnd { get; private set; }
    public AutomationElement Root { get; private set; } = null!;

    private AppWindow(Process p) => Process = p;

    public static AppWindow Launch(string exe, string dataDir, params string[] args) => Launch(exe, dataDir, false, args);

    public static AppWindow Launch(string exe, string dataDir, bool reducedMotion, params string[] args)
    {
        Demo.EnsureSafe(dataDir);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["REMINDR_DATA_DIR"] = dataDir;   // the app only ever sees the temp folder
        if (reducedMotion) psi.Environment["REMINDR_REDUCED_MOTION"] = "1";
        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + exe);
        var w = new AppWindow(p);
        w.WaitForWindow();
        return w;
    }

    private void WaitForWindow()
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(40))
        {
            if (Process.HasExited) throw new InvalidOperationException("The app exited early.");
            Process.Refresh();
            var h = Process.MainWindowHandle;
            if (h != IntPtr.Zero && Native.IsWindowVisible(h))
            {
                Hwnd = h;
                Root = AutomationElement.FromHandle(h);
                return;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("No window appeared.");
    }

    // ---- finding ----
    public AutomationElement? Find(string? name = null, string? id = null, ControlType? type = null, AutomationElement? within = null, bool exact = true)
    {
        var conds = new List<Condition>();
        if (id is not null) conds.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (name is not null && exact) conds.Add(new PropertyCondition(AutomationElement.NameProperty, name));
        if (type is not null) conds.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        Condition c = conds.Count == 0 ? Condition.TrueCondition : conds.Count == 1 ? conds[0] : new AndCondition(conds.ToArray());
        var scope = within ?? Root;
        if (name is null || exact) return scope.FindFirst(TreeScope.Descendants, c);
        foreach (AutomationElement e in scope.FindAll(TreeScope.Descendants, c))
            if (e.Current.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    public AutomationElement Need(string? name = null, string? id = null, ControlType? type = null, AutomationElement? within = null, int timeoutMs = 8000, bool exact = true)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var e = Find(name, id, type, within, exact);
            if (e is not null) return e;
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"UI element not found: name='{name}' id='{id}' type='{type?.ProgrammaticName}'");
            Thread.Sleep(120);
        }
    }

    public bool Exists(string? name = null, string? id = null, ControlType? type = null, int timeoutMs = 600, bool exact = true)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            if (Find(name, id, type, null, exact) is not null) return true;
            Thread.Sleep(100);
        } while (sw.ElapsedMilliseconds < timeoutMs);
        return false;
    }

    // ---- acting (patterns only) ----
    public static void Activate(AutomationElement e)
    {
        if (e.TryGetCurrentPattern(InvokePattern.Pattern, out var inv)) { ((InvokePattern)inv).Invoke(); return; }
        if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel)) { ((SelectionItemPattern)sel).Select(); return; }
        if (e.TryGetCurrentPattern(TogglePattern.Pattern, out var tog)) { ((TogglePattern)tog).Toggle(); return; }
        if (e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var ex)) { ((ExpandCollapsePattern)ex).Expand(); return; }
        // Look one level down for a pattern-capable child (custom item templates wrap a button).
        foreach (AutomationElement ch in e.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (ch.TryGetCurrentPattern(InvokePattern.Pattern, out var i2)) { ((InvokePattern)i2).Invoke(); return; }
            if (ch.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var s2)) { ((SelectionItemPattern)s2).Select(); return; }
        }
        throw new InvalidOperationException("No usable UIA pattern on " + Describe(e));
    }

    public void Click(string? name = null, string? id = null, ControlType? type = null, AutomationElement? within = null, int timeoutMs = 8000, bool exact = true)
        => Activate(Need(name, id, type, within, timeoutMs, exact));

    public static void SetText(AutomationElement e, string text)
    {
        if (e.TryGetCurrentPattern(ValuePattern.Pattern, out var v)) ((ValuePattern)v).SetValue(text);
        else throw new InvalidOperationException("No ValuePattern on " + Describe(e));
    }

    public static void Toggle(AutomationElement e, bool on)
    {
        var t = (TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern);
        if ((t.Current.ToggleState == ToggleState.On) != on) t.Toggle();
    }

    public static string Describe(AutomationElement e)
    {
        var c = e.Current;
        return $"{c.ControlType.ProgrammaticName.Replace("ControlType.", "")} name='{c.Name}' id='{c.AutomationId}' rect={c.BoundingRectangle}";
    }

    /// <summary>Prints the UIA tree (for finding names while writing scenes).</summary>
    public void Dump(AutomationElement? from = null, int maxDepth = 12, string[]? skipIds = null)
    {
        void Walk(AutomationElement e, int d)
        {
            if (d > maxDepth) return;
            if (skipIds is not null && skipIds.Contains(e.Current.AutomationId)) return;
            var c = e.Current;
            var pats = string.Join(",", e.GetSupportedPatterns().Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "").Replace("Identifiers.Pattern", "")));
            Console.WriteLine($"{new string(' ', d * 2)}{c.ControlType.ProgrammaticName.Replace("ControlType.", "")} '{c.Name}' #{c.AutomationId} {(c.IsOffscreen ? "(off) " : "")}[{pats}]");
            foreach (AutomationElement ch in e.FindAll(TreeScope.Children, Condition.TrueCondition)) Walk(ch, d + 1);
        }
        Walk(from ?? Root, 0);
    }

    public void Settle(int ms = 600) => Thread.Sleep(ms);

    public void Close()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.CloseMainWindow();
                if (!Process.WaitForExit(4000)) Process.Kill(true);
            }
        }
        catch (Exception) { /* already gone */ }
    }

    public void Dispose() => Close();
}


