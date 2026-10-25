using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Remindr.Converters;
using Remindr.ViewModels;

namespace Remindr.Views.Controls;

public partial class ProfilePickerView : UserControl
{
    private readonly Ellipse[] _dots;
    private bool _blobColorsPending;
    private int _dotsFilled;

    /// <summary>Short windows: smaller tiles and heading (the tile list scrolls vertically if it still does not fit).</summary>
    private void ApplyCompact(bool compact)
    {
        Resources["TileW"] = compact ? 136.0 : 168.0;
        Resources["TileH"] = compact ? 180.0 : 220.0;
        Resources["TileAvatar"] = compact ? 88.0 : 120.0;
        Resources["PickerHeadingSize"] = compact ? 26.0 : 32.0;
    }

    public ProfilePickerView()
    {
        InitializeComponent();
        _dots = new[] { Dot0, Dot1, Dot2, Dot3 };
        DigitsOnly.Attach(PinBox);
        SizeChanged += (_, e) => ApplyCompact(e.NewSize.Height < 640);

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProfilePickerViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnVmChanged;
                oldVm.PinRejected -= OnPinRejected;
                oldVm.Tiles.CollectionChanged -= OnTilesChanged;
            }
            if (e.NewValue is ProfilePickerViewModel vm)
            {
                vm.PropertyChanged += OnVmChanged;
                vm.PinRejected += OnPinRejected;
                vm.Tiles.CollectionChanged += OnTilesChanged;
                ScheduleBlobColors();
            }
        };

        IsVisibleChanged += (_, _) => OnVisibilityChanged();
        TileList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            // Containers for freshly (re)loaded tiles: play the staggered entrance before the first frame is drawn.
            if (IsVisible && TileList.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                PlayEntrance();
        };
    }

    private ProfilePickerViewModel Vm => (ProfilePickerViewModel)DataContext;

    // ---- visibility, entrance ------------------------------------------------------------------------

    private void OnVisibilityChanged()
    {
        if (IsVisible)
        {
            // Containers may already exist (tiles were reloaded while hidden); the generator event only covers fresh ones.
            PlayEntrance();
        }
    }

    /// <summary>Header and tiles fade and slide up, StaggerMs apart.</summary>
    private void PlayEntrance()
    {
        if (!Anim.MotionEnabled) return;
        Anim.FadeIn(Header, 0);
        for (var i = 0; i < TileList.Items.Count; i++)
            if (TileList.ItemContainerGenerator.ContainerFromIndex(i) is UIElement container)
                Anim.FadeIn(container, i + 1);
    }

    // ---- blobs -----------------------------------------------------------------------------------------

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleBlobColors();

    private void ScheduleBlobColors()
    {
        if (_blobColorsPending) return;
        _blobColorsPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _blobColorsPending = false;
            ApplyBlobColors();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>One colour per profile, cycled over the four blobs; a lone profile uses its colour and a lighter tint of it.</summary>
    private void ApplyBlobColors()
    {
        if (DataContext is not ProfilePickerViewModel vm) return;
        var colors = vm.Tiles.OfType<ProfileTileViewModel>().Select(t => ColorUtil.Parse(t.Color)).ToList();
        if (colors.Count == 0) return;
        if (colors.Count == 1)
        {
            var c = colors[0];
            var tint = Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
            colors = new List<Color> { c, tint, c, tint };
        }
        Blobs.SetColors(colors);

        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.35);
    }

    // ---- PIN prompt ----------------------------------------------------------------------------------

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm && vm.PinInput != PinBox.Password)
            vm.PinInput = PinBox.Password;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The VM resets PinInput to "" after a wrong PIN or when the prompt closes; clear the box to match.
        if (e.PropertyName == nameof(ProfilePickerViewModel.PinInput) && Vm.PinInput.Length == 0 && PinBox.Password.Length != 0)
            PinBox.Clear();

        if (e.PropertyName == nameof(ProfilePickerViewModel.PinLength))
            UpdateDots(Vm.PinLength);

        if (e.PropertyName == nameof(ProfilePickerViewModel.IsPinPromptOpen) && Vm.IsPinPromptOpen)
        {
            UpdateDots(0);
            PlayPinCardIn();
            Dispatcher.BeginInvoke(new Action(() => { PinBox.Focus(); Keyboard.Focus(PinBox); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(ProfilePickerViewModel.PinError) && Vm.PinError is not null)
            Dispatcher.BeginInvoke(new Action(() => PinBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnPinRejected(object? sender, EventArgs e) => Anim.Shake(PinShake);

    /// <summary>Dim layer fades in; the card fades and scales from 0.96 to 1.</summary>
    private void PlayPinCardIn()
    {
        var dur = Anim.Dur("DurNormal");
        PinPrompt.BeginAnimation(OpacityProperty, null);
        PinScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PinScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PinScale.ScaleX = PinScale.ScaleY = 1;
        PinPrompt.Opacity = 1;
        if (!Anim.MotionEnabled || dur.TimeSpan == TimeSpan.Zero) return;

        var fade = new DoubleAnimation(0, 1, dur) { EasingFunction = Anim.EaseOut };
        fade.Completed += (_, _) => { PinPrompt.BeginAnimation(OpacityProperty, null); PinPrompt.Opacity = 1; };
        PinPrompt.BeginAnimation(OpacityProperty, fade);

        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var grow = new DoubleAnimation(0.96, 1, dur) { EasingFunction = Anim.EaseOut };
            grow.Completed += (_, _) => { PinScale.BeginAnimation(prop, null); PinScale.SetValue(prop, 1.0); };
            PinScale.BeginAnimation(prop, grow);
        }
    }

    /// <summary>Fills the first <paramref name="count"/> dots with the accent colour; a newly filled dot pops.</summary>
    private void UpdateDots(int count)
    {
        for (var i = 0; i < _dots.Length; i++)
        {
            var dot = _dots[i];
            var filled = i < count;
            if (filled) dot.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
            else dot.Fill = Brushes.Transparent;

            if (filled && i >= _dotsFilled) Pop(dot);
        }
        _dotsFilled = count;
    }

    private static void Pop(Ellipse dot)
    {
        var scale = (ScaleTransform)dot.RenderTransform;
        var dur = Anim.Dur("DurFast");
        if (!Anim.MotionEnabled || dur.TimeSpan == TimeSpan.Zero) return;
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var a = new DoubleAnimation(0.6, 1, dur) { EasingFunction = Anim.EaseOut };
            a.Completed += (_, _) => { scale.BeginAnimation(prop, null); scale.SetValue(prop, 1.0); };
            scale.BeginAnimation(prop, a);
        }
    }

    /// <summary>Arrow keys move between tiles (the ScrollViewer around them would otherwise swallow them).</summary>
    private void TileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var dir = e.Key switch
        {
            Key.Left => FocusNavigationDirection.Left,
            Key.Right => FocusNavigationDirection.Right,
            Key.Up => FocusNavigationDirection.Up,
            Key.Down => FocusNavigationDirection.Down,
            _ => (FocusNavigationDirection?)null
        };
        if (dir is { } d && Keyboard.FocusedElement is UIElement focused)
        {
            focused.MoveFocus(new TraversalRequest(d));
            e.Handled = true;
        }
    }

    /// <summary>Marks a tile as keyboard-focused only when focus arrived from the keyboard (not a mouse click).</summary>
    private void Tile_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        var tile = (Button)sender;
        TileMotion.SetKeyFocused(tile, tile.IsKeyboardFocused && InputManager.Current.MostRecentInputDevice is KeyboardDevice);
    }

    // ---- tile menu -----------------------------------------------------------------------------------

    private static ContextMenu Menu(FrameworkElement tile) => (ContextMenu)tile.FindResource("TileMenu");

    private void Dots_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        var menu = Menu(this);
        menu.DataContext = btn.DataContext;
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ProfileTileViewModel? TileOf(object sender) => (sender as FrameworkElement)?.DataContext as ProfileTileViewModel;

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t) Vm.EditCommand.Execute(t);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t) Vm.DeleteCommand.Execute(t);
    }
}
