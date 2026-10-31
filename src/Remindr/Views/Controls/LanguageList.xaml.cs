using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Remindr.Services;

namespace Remindr.Views.Controls;

/// <summary>One row of the language list. Its native name carries its own language, direction and font so it is shaped correctly inside an English list.</summary>
public sealed class LanguageRow : INotifyPropertyChanged
{
    private const string BaseFont = "Segoe UI Variable Text";
    private bool _isCurrent;

    public LanguageRow(LanguageInfo info, bool isCurrent)
    {
        Info = info;
        _isCurrent = isCurrent;
        Xml = info.Code == LocalizationService.PseudoCode ? XmlLanguage.GetLanguage("en-US") : SafeLanguage(info.Code);
        Flow = info.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Font = new FontFamily(FontPolicy.Chain(BaseFont, info.Code));
        TagVisibility = info.IsMachineTranslated ? Visibility.Visible : Visibility.Collapsed;
    }

    private static XmlLanguage SafeLanguage(string code)
    {
        try { return XmlLanguage.GetLanguage(code); }
        catch (ArgumentException) { return XmlLanguage.GetLanguage("en-US"); }
    }

    public LanguageInfo Info { get; }
    public string Code => Info.Code;
    public string NativeName => Info.NativeName;
    public string EnglishName => Info.EnglishName;
    public XmlLanguage Xml { get; }
    public FlowDirection Flow { get; }
    public FontFamily Font { get; }
    public Visibility TagVisibility { get; }

    /// <summary>What a screen reader says: "Deutsch (German)", plus the tag when the translation is machine-made.</summary>
    public string AutomationName => Info.ToString() + (Info.IsMachineTranslated ? ", " + Loc.Get("Language_MachineTranslated") : "");

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The language picker shared by Settings (inside a popup) and the first-run setup Language step: a search box (native name, English name
/// or code; accents and case ignored; 150 ms debounce), a virtualised list with the current or suggested language pinned first, and a
/// Help translate link. Type to search; Down/Up move between the box and the list; Enter or a click chooses (the app switches language
/// live); Esc closes when <see cref="ClosesOnEscape"/>. Pure logic is in <see cref="LanguageSearch"/>.
/// </summary>
public partial class LanguageList : UserControl
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    public static readonly DependencyProperty SelectedCodeProperty = DependencyProperty.Register(
        nameof(SelectedCode), typeof(string), typeof(LanguageList), new PropertyMetadata("en", (d, _) => ((LanguageList)d).MarkCurrent()));

    public static readonly DependencyProperty PinnedCodeProperty = DependencyProperty.Register(
        nameof(PinnedCode), typeof(string), typeof(LanguageList), new PropertyMetadata(null, (d, _) => ((LanguageList)d).Refresh()));

    public static readonly DependencyProperty ClosesOnEscapeProperty = DependencyProperty.Register(
        nameof(ClosesOnEscape), typeof(bool), typeof(LanguageList), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchAutomationIdProperty = DependencyProperty.Register(
        nameof(SearchAutomationId), typeof(string), typeof(LanguageList),
        new PropertyMetadata("LanguageSearch", (d, e) => System.Windows.Automation.AutomationProperties.SetAutomationId(((LanguageList)d).SearchBox, (string)e.NewValue)));

    private readonly DispatcherTimer _timer = new() { Interval = Debounce };
    private IReadOnlyList<LanguageInfo> _languages = LocalizationService.Available.Where(l => l.Code != LocalizationService.PseudoCode).ToList();
    private List<LanguageRow> _rows = new();

    public LanguageList()
    {
        InitializeComponent();
        System.Windows.Automation.AutomationProperties.SetAutomationId(SearchBox, "LanguageSearch");
        _timer.Tick += (_, _) => { _timer.Stop(); Refresh(); };
        Loaded += (_, _) => { Loc.CultureChanged += OnCultureChanged; Refresh(); };
        Unloaded += (_, _) => { Loc.CultureChanged -= OnCultureChanged; _timer.Stop(); };
    }

    /// <summary>The language currently used (shown with a check mark).</summary>
    public string SelectedCode { get => (string)GetValue(SelectedCodeProperty); set => SetValue(SelectedCodeProperty, value); }

    /// <summary>The language listed first when nothing is searched (the current one in Settings, the Windows display language in setup).</summary>
    public string? PinnedCode { get => (string?)GetValue(PinnedCodeProperty); set => SetValue(PinnedCodeProperty, value); }

    /// <summary>Esc raises <see cref="CloseRequested"/> (a popup). Off, Esc only clears the search so the host (setup's Back) still gets it when the box is empty.</summary>
    public bool ClosesOnEscape { get => (bool)GetValue(ClosesOnEscapeProperty); set => SetValue(ClosesOnEscapeProperty, value); }

    public string SearchAutomationId { get => (string)GetValue(SearchAutomationIdProperty); set => SetValue(SearchAutomationIdProperty, value); }

    /// <summary>Enter chooses (default). Setup turns it off and calls <see cref="ActivateCandidate"/> itself, so a second Enter can go on to the next page.</summary>
    public bool EnterChooses { get; set; } = true;

    /// <summary>Hide the Help translate link (it is shown by default).</summary>
    public bool ShowHelpTranslate { get => HelpButton.Visibility == Visibility.Visible; set => HelpButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

    /// <summary>The languages to list (defaults to every language that has a shipped translation).</summary>
    public IReadOnlyList<LanguageInfo> Languages
    {
        get => _languages;
        set { _languages = value; Refresh(); }
    }

    /// <summary>A language was chosen (Enter or a click).</summary>
    public event Action<string>? LanguageChosen;

    /// <summary>Esc was pressed in a host that closes on it.</summary>
    public event Action? CloseRequested;

    /// <summary>The rows shown now (for tests and screenshots).</summary>
    public IReadOnlyList<LanguageRow> Rows => _rows;

    public string SearchText { get => SearchBox.Text; set { SearchBox.Text = value; Refresh(); } }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>Clears the search and scrolls back to the current language (call when a popup opens).</summary>
    public void Reset()
    {
        SearchBox.Text = "";
        Refresh();
    }

    /// <summary>
    /// For hosts that use Enter for something else (setup's Next): chooses what Enter would choose here (the best match when the search box
    /// has focus, else the highlighted row) when that is a different language than the current one. False when there is nothing new to choose.
    /// </summary>
    public bool ActivateCandidate()
    {
        FlushSearch();
        var candidate = SearchBox.IsKeyboardFocused ? _rows.FirstOrDefault() : Box.SelectedItem as LanguageRow;
        if (candidate is null || string.Equals(candidate.Code, SelectedCode, StringComparison.OrdinalIgnoreCase)) return false;
        Choose(candidate);
        if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; Refresh(); }
        return true;
    }

    /// <summary>Esc for hosts that use it for Back: clears a non-empty search instead. False when the search was already empty.</summary>
    public bool ClearSearch()
    {
        if (SearchBox.Text.Length == 0) return false;
        SearchBox.Text = "";
        Refresh();
        FocusSearch();
        return true;
    }

    // ---- list -------------------------------------------------------------------------------------------

    private void OnCultureChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        if (Box is null) return;
        _timer.Stop();
        var query = SearchBox.Text;
        var languages = string.IsNullOrWhiteSpace(query)
            ? LanguageSearch.Arrange(_languages, PinnedCode ?? SelectedCode)
            : LanguageSearch.Filter(query, _languages);
        _rows = languages.Select(l => new LanguageRow(l, string.Equals(l.Code, SelectedCode, StringComparison.OrdinalIgnoreCase))).ToList();
        Box.ItemsSource = _rows;
        NoMatch.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var current = _rows.FirstOrDefault(r => r.IsCurrent);
        Box.SelectedItem = current;
        if (current is not null) Box.ScrollIntoView(current);
        else if (_rows.Count > 0) Box.ScrollIntoView(_rows[0]);
    }

    private void MarkCurrent()
    {
        foreach (var r in _rows) r.IsCurrent = string.Equals(r.Code, SelectedCode, StringComparison.OrdinalIgnoreCase);
    }

    private void Choose(LanguageRow row)
    {
        // SetCurrentValue keeps a host's binding (Settings binds SelectedCode to the current language).
        SetCurrentValue(SelectedCodeProperty, row.Code);
        LanguageChosen?.Invoke(row.Code);
    }

    // ---- search box -------------------------------------------------------------------------------------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _timer.Stop();
        _timer.Start();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                FlushSearch();
                FocusRow(Box.SelectedItem as LanguageRow ?? _rows.FirstOrDefault());
                e.Handled = true;
                break;
            case Key.Enter when EnterChooses:
                // Enter takes the best match at once (no waiting for the debounce).
                FlushSearch();
                if (_rows.Count > 0) Choose(_rows[0]);
                e.Handled = true;
                break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0 && !ClosesOnEscape) { SearchBox.Text = ""; Refresh(); e.Handled = true; }
                else if (ClosesOnEscape) { CloseRequested?.Invoke(); e.Handled = true; }
                break;
        }
    }

    private void FlushSearch()
    {
        if (_timer.IsEnabled) Refresh();
    }

    // ---- list keyboard and mouse ------------------------------------------------------------------------

    private void List_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when EnterChooses:
            case Key.Space:
                if (Box.SelectedItem is LanguageRow row) { Choose(row); e.Handled = true; }
                break;
            case Key.Up when Box.SelectedIndex <= 0:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.Escape:
                if (ClosesOnEscape) { CloseRequested?.Invoke(); e.Handled = true; }
                else if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; Refresh(); FocusSearch(); e.Handled = true; }
                break;
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        // Typing while the list has focus goes to the search box.
        if (Box.IsKeyboardFocusWithin && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]) && e.Text[0] != ' ')
        {
            SearchBox.Text += e.Text;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            FocusSearch();
            e.Handled = true;
        }
        base.OnPreviewTextInput(e);
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Box, d) is ListBoxItem { DataContext: LanguageRow row })
            Choose(row);
    }

    private void FocusRow(LanguageRow? row)
    {
        if (row is null) return;
        Box.SelectedItem = row;
        Box.ScrollIntoView(row);
        Box.UpdateLayout();
        (Box.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem)?.Focus();
    }

    // ---- help translate ---------------------------------------------------------------------------------

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var code = (Box.SelectedItem as LanguageRow)?.Code ?? SelectedCode;
        AppInfo.Open(FeedbackLinks.TranslationUrl(code));
    }
}
