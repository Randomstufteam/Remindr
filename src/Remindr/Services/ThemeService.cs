using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Remindr.Data.Repositories;
using Remindr.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Remindr.Services;

public sealed class ThemeService
{
    public const string SettingKey = "theme";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly SettingsRepo _settings;
    private Window? _window;
    private ResourceDictionary? _glass;

    public AppTheme Current { get; private set; } = AppTheme.System;

    private Color? _accent;
    /// <summary>Accent the WPF-UI theme dictionary was last reloaded for (SetAccent skips a repeat reload for it).</summary>
    private Color? _reloadedAccent;

    public ThemeService(SettingsRepo settings)
    {
        _settings = settings;
        // The OS-theme watcher re-applies themes on its own; put the profile accent and glass brushes back afterwards.
        ApplicationThemeManager.Changed += (theme, _) =>
        {
            ReapplyAccent();
            UpdateGlass(theme);
        };
        // Windows' "Transparency effects" switch raises this; re-check the registry on any user-preference change.
        SystemEvents.UserPreferenceChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => UpdateGlass(ApplicationThemeManager.GetAppTheme())));
    }

    /// <summary>True unless Windows Settings > Personalization > Colors > Transparency effects is off.</summary>
    public static bool TransparencyEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("EnableTransparency") is not int v || v != 0;
            }
            catch (Exception) { return true; }
        }
    }

    /// <summary>Acrylic on Windows 11 22H2+ with transparency on; otherwise Mica (Windows 10 has no Mica and stays opaque).</summary>
    public static WindowBackdropType EffectiveBackdrop =>
        TransparencyEnabled && Environment.OSVersion.Version.Build >= 22621 && WindowBackdrop.IsSupported(WindowBackdropType.Acrylic)
            ? WindowBackdropType.Acrylic
            : WindowBackdropType.Mica;

    /// <summary>Sets the per-profile accent colour (null = Windows accent) and applies it now.</summary>
    public void SetAccent(Color? color)
    {
        // Same colour (e.g. re-entering the same profile): the brushes are already right, and theme changes re-apply it
        // via ApplicationThemeManager.Changed, so skip the theme reload and its backdrop flash.
        if (color == _accent && (color is null || _reloadedAccent == color)) return;
        _accent = color;
        if (color is null) ApplicationAccentColorManager.ApplySystemAccent();
        else
        {
            // Accent-derived brushes in WPF-UI's theme dictionaries (primary buttons, nav selection bar) only pick up the new
            // colour when the theme dictionary is reloaded, which is why a theme switch "fixed" them: reload it, then re-apply.
            if (_window?.IsLoaded == true)
            {
                ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme(), EffectiveBackdrop, false);
                _reloadedAccent = color;
            }
            ReapplyAccent();
            UpdateGlass(ApplicationThemeManager.GetAppTheme());
        }
    }

    private void ReapplyAccent()
    {
        if (_accent is not { } c) return;
        var theme = ApplicationThemeManager.GetAppTheme();
        // Keep white-on-accent text readable in Light theme: darken bright colours.
        if (theme == ApplicationTheme.Light && Remindr.Converters.ColorUtil.Luminance(c) > 0.3)
            c = Color.FromRgb((byte)(c.R * 0.75), (byte)(c.G * 0.75), (byte)(c.B * 0.75));
        ApplicationAccentColorManager.Apply(c, theme, false);
    }

    /// <summary>Loads the saved theme and applies it. Call before showing the window.</summary>
    public void ApplySaved()
    {
        var saved = _settings.Get(SettingKey);
        Current = Enum.TryParse<AppTheme>(saved, out var t) ? t : AppTheme.System;
        ApplyCurrent();
    }

    /// <summary>Remembers the main window so System mode can watch OS theme changes.</summary>
    public void AttachWindow(Window window)
    {
        _window = window;
        UpdateGlass(ApplicationThemeManager.GetAppTheme());
        // SystemThemeWatcher needs an HWND; UnWatch throws on a window that is not loaded yet.
        if (window.IsLoaded) UpdateWatcher();
        else window.Loaded += (_, _) => UpdateWatcher();
    }

    public void Set(AppTheme theme)
    {
        if (theme == Current) return;
        Current = theme;
        _settings.Set(SettingKey, theme.ToString());
        ApplyCurrent();
    }

    /// <summary>Applies a theme now without saving it (first-run setup previews it live and saves only when setup is finished).</summary>
    public void Preview(AppTheme theme)
    {
        if (theme == Current) return;
        Current = theme;
        ApplyCurrent();
    }

    private void ApplyCurrent()
    {
        var backdrop = EffectiveBackdrop;
        switch (Current)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, backdrop, _accent is null);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, backdrop, _accent is null);
                break;
            default:
                // Refreshes the cached OS theme and maps Glow/CapturedMotion to Dark (same path SystemThemeWatcher uses).
                ApplicationThemeManager.ApplySystemTheme(_accent is null);
                break;
        }
        ReapplyAccent();
        UpdateGlass(ApplicationThemeManager.GetAppTheme());
        UpdateWatcher();
    }

    /// <summary>Swaps the Glass.* brush dictionary (Light/Dark, opaque when transparency is off) and the window backdrop.</summary>
    private void UpdateGlass(ApplicationTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var dark = theme != ApplicationTheme.Light;
        var name = TransparencyEnabled
            ? (dark ? "Glass.Dark" : "Glass.Light")
            : (dark ? "Glass.Opaque.Dark" : "Glass.Opaque.Light");
        var uri = new Uri($"pack://application:,,,/Styles/{name}.xaml", UriKind.Absolute);

        if (_glass is null || _glass.Source != uri)
        {
            var fresh = new ResourceDictionary { Source = uri };
            var merged = app.Resources.MergedDictionaries;
            if (_glass is not null) merged.Remove(_glass);
            merged.Add(fresh);
            _glass = fresh;
        }

        if (_window is FluentWindow fw && fw.WindowBackdropType != EffectiveBackdrop)
            fw.WindowBackdropType = EffectiveBackdrop;

        GlassChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Raised at the end of UpdateGlass (theme or Windows transparency changed). Static so views need no service lookup.</summary>
    public static event EventHandler? GlassChanged;

    private void UpdateWatcher()
    {
        if (_window is null || !_window.IsLoaded) return;
        if (Current == AppTheme.System)
            SystemThemeWatcher.Watch(_window, EffectiveBackdrop, false);
        else
            SystemThemeWatcher.UnWatch(_window);
    }
}
