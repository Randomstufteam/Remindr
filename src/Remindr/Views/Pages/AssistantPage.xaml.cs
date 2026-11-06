using System.Windows.Controls;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Views.Pages;

public partial class AssistantPage : Page
{
    public AssistantPage(AssistantViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        // Pages are singletons: opening the page starts the model in the background (first use) and focuses the text box.
        Loaded += (_, _) =>
        {
            viewModel.IsShown = true;
            viewModel.OnShown();
            InputBox.Focus();
        };
        // The Ctrl+Space hotkey works only while this page is open; leaving it drops a recording and stops speech.
        Unloaded += (_, _) => { viewModel.IsShown = false; viewModel.StopVoice(); };
        viewModel.ContentGrew += (_, _) => Scroller.ScrollToBottom();
        // The selection pill sits behind the checked Work / Chat segment.
        ModeGrid.SizeChanged += (_, _) => PlacePill(viewModel, animate: false);
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AssistantViewModel.Mode)) PlacePill(viewModel, animate: IsLoaded); };
        Loaded += (_, _) => PlacePill(viewModel, animate: false);
        // New messages scroll into view.
        viewModel.Messages.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(Scroller.ScrollToBottom), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>One selection pill, half the segment grid wide, slides to the checked segment (same as the calendar's Month/Week/Agenda).</summary>
    private void PlacePill(AssistantViewModel vm, bool animate)
    {
        var cell = ModeGrid.ActualWidth / 2;
        if (cell <= 0) return;
        ModePill.Width = cell;
        Anim.SlidePill(ModePill, vm.Mode == Services.Assistant.AssistantMode.Chat ? 1 : 0, cell, animate);
    }
}
