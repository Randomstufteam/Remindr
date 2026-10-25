using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Remindr.Views.Controls;

/// <summary>Restricts a TextBox or PasswordBox to digits: typed text, space, and paste.</summary>
public static class DigitsOnly
{
    public static void Attach(Control box)
    {
        box.PreviewTextInput += (_, e) => e.Handled = !IsDigits(e.Text);
        // Space does not raise PreviewTextInput in a TextBox/PasswordBox.
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Space) e.Handled = true; };
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string s || !IsDigits(s))
                e.CancelCommand();
        });

        // PasswordBox may not raise the Pasting event for every paste path, so strip anything that slipped in.
        if (box is PasswordBox pb)
            pb.PasswordChanged += (_, _) =>
            {
                var digits = new string(pb.Password.Where(c => c is >= '0' and <= '9').ToArray());
                if (digits != pb.Password) pb.Password = digits;
            };
    }

    private static bool IsDigits(string s) => s.Length > 0 && s.All(c => c is >= '0' and <= '9');
}
