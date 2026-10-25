using System.Windows;
using System.Windows.Controls;
using Remindr.Services;

namespace Remindr.Views.Controls;

/// <summary>
/// "New PIN" plus "Repeat PIN" boxes with the digits-only filter and the mismatch message. Reused by the PIN dialog and first-run setup.
/// The panel only reports what was typed; saving the PIN is up to the host.
/// </summary>
public partial class PinSetPanel : UserControl
{
    public PinSetPanel()
    {
        InitializeComponent();
        DigitsOnly.Attach(NewBox);
        DigitsOnly.Attach(ConfirmBox);
    }

    /// <summary>Raised whenever either box changes.</summary>
    public event EventHandler? StateChanged;

    public string NewPin => NewBox.Password;
    public string RepeatPin => ConfirmBox.Password;

    /// <summary>True when a complete 4-digit PIN was typed twice the same way.</summary>
    public bool IsComplete => PinHasher.IsValidPin(NewBox.Password) && NewBox.Password == ConfirmBox.Password;

    /// <summary>Label the second box "Repeat new PIN" (used when changing an existing PIN) instead of "Repeat PIN".</summary>
    public bool RepeatsNewPin
    {
        get => RepeatNewLabel.Visibility == Visibility.Visible;
        set
        {
            RepeatNewLabel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            RepeatLabel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            ConfirmBox.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
                new System.Windows.Data.Binding(value ? "[Pin_RepeatNew]" : "[Pin_Repeat]") { Source = Loc.Instance, Mode = System.Windows.Data.BindingMode.OneWay });
        }
    }

    public void FocusFirst() => NewBox.Focus();

    /// <summary>Empties both boxes.</summary>
    public void Clear()
    {
        NewBox.Clear();
        ConfirmBox.Clear();
    }

    /// <summary>Loads values into the boxes (setup restores what was typed when you come back to the page).</summary>
    public void SetValues(string newPin, string repeatPin)
    {
        if (NewBox.Password != newPin) NewBox.Password = newPin;
        if (ConfirmBox.Password != repeatPin) ConfirmBox.Password = repeatPin;
    }

    private void Box_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (MismatchText is null) return;
        MismatchText.Text = (NewBox.Password.Length == 4 && ConfirmBox.Password.Length == 4 && NewBox.Password != ConfirmBox.Password)
            ? Loc.Get("Pin_Error_Mismatch")
            : "";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
