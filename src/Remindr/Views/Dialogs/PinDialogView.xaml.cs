using System.Windows;
using System.Windows.Controls;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Controls;

namespace Remindr.Views.Dialogs;

public enum PinMode { Set, Change, Remove }

/// <summary>Content of the PIN dialog: Set (new + repeat), Change (current + new + repeat) or Remove (current).</summary>
public partial class PinDialogView : UserControl
{
    private readonly PinMode _mode;
    private readonly Profile _profile;

    public PinDialogView(PinMode mode, Profile profile)
    {
        _mode = mode;
        _profile = profile;
        InitializeComponent();

        DigitsOnly.Attach(CurrentBox);

        CurrentPanel.Visibility = mode == PinMode.Set ? Visibility.Collapsed : Visibility.Visible;
        NewPanel.Visibility = mode == PinMode.Remove ? Visibility.Collapsed : Visibility.Visible;
        // "Repeat new PIN" only when there is an old PIN to tell it apart from.
        NewPanel.RepeatsNewPin = mode == PinMode.Change;
        NewPanel.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        Intro.Text = mode switch
        {
            PinMode.Set => Loc.Get("Pin_Intro_Set"),
            PinMode.Change => Loc.Get("Pin_Intro_Change"),
            _ => Loc.Get("Pin_Intro_Remove")
        };

        Loaded += (_, _) =>
        {
            if (mode == PinMode.Set) NewPanel.FocusFirst();
            else CurrentBox.Focus();
        };
    }

    /// <summary>Raised whenever a box changes, so the dialog can enable/disable its primary button.</summary>
    public event EventHandler? StateChanged;

    public string NewPin => NewPanel.NewPin;

    /// <summary>True when every box needed for the mode is filled in correctly (current PIN is checked on Save).</summary>
    public bool IsComplete
    {
        get
        {
            var currentOk = _mode == PinMode.Set || PinHasher.IsValidPin(CurrentBox.Password);
            var newOk = _mode == PinMode.Remove || NewPanel.IsComplete;
            return currentOk && newOk;
        }
    }

    /// <summary>Checks the current PIN against the profile. On failure shows an inline error, clears the box and returns false.</summary>
    public bool Validate()
    {
        if (_mode != PinMode.Set &&
            !PinHasher.Verify(CurrentBox.Password, _profile.PinHash, _profile.PinSalt))
        {
            // Clear first: Clear() raises PasswordChanged, whose handler resets ErrorText.
            CurrentBox.Clear();
            ErrorText.Text = Loc.Get("Pin_Error_Current");
            CurrentBox.Focus();
            return false;
        }
        ErrorText.Text = "";
        return true;
    }

    private void Box_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ErrorText is null) return;
        ErrorText.Text = "";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
