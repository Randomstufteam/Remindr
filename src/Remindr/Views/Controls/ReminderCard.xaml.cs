using System.Windows.Controls;

namespace Remindr.Views.Controls;

/// <summary>A reminder card (DataContext = ReminderItemViewModel). All the markup lives in ReminderCard.xaml.</summary>
public partial class ReminderCard : UserControl
{
    public ReminderCard() => InitializeComponent();
}
