using System.Windows.Controls;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Views.Pages;

public partial class TodayPage : Page
{
    public TodayPage(TodayViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        // Pages are singletons: refresh (and replay the entrance) every time the page is shown.
        Loaded += (_, _) =>
        {
            viewModel.SettleDelay = Anim.MotionEnabled ? TimeSpan.FromMilliseconds(450) : TimeSpan.Zero;
            viewModel.Reload(animateAll: true);
        };
    }
}
