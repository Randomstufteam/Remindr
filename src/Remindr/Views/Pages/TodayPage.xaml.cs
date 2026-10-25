using System.Windows.Controls;
using Remindr.ViewModels;

namespace Remindr.Views.Pages;

public partial class TodayPage : Page
{
    public TodayPage(TodayViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
