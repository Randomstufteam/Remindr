using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>One built-in picture in a picker grid. The image is decoded lazily, so only rows that get realised cost anything.</summary>
public sealed partial class PictureOption : ObservableObject
{
    /// <summary>Thumbnails are decoded at ~2x their 40-52px display size so they stay crisp on HiDPI.</summary>
    public const int ThumbDecodeWidth = 96;

    private ImageSource? _image;
    private bool _imageLoaded;

    public PictureOption(PictureEntry entry) => Entry = entry;

    public PictureEntry Entry { get; }
    public string Label => Entry.Label;

    public ImageSource? Image
    {
        get
        {
            if (!_imageLoaded)
            {
                _image = AvatarCatalog.LoadBuiltin(Entry.Value, ThumbDecodeWidth); // cached across options and dialogs
                _imageLoaded = true;
            }
            return _image;
        }
    }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>A category chip ("All" has a null <see cref="Match"/>).</summary>
public sealed partial class PickerChip : ObservableObject
{
    public PickerChip(string label, Func<PictureEntry, bool>? match)
    {
        Label = label;
        Match = match;
    }

    public string Label { get; }
    public Func<PictureEntry, bool>? Match { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Filter state shared by the avatar chooser and the reminder picture picker: category chips plus a search text
/// over a fixed set of pictures. The picker UI binds to <see cref="Chips"/>, <see cref="Search"/> and <see cref="Items"/>.
/// </summary>
public sealed partial class PicturePickerViewModel : ObservableObject
{
    private readonly List<PictureOption> _all;

    public PicturePickerViewModel(IEnumerable<PictureEntry> entries,
        IEnumerable<(string Label, Func<PictureEntry, bool> Match)> categories, string? selectedValue)
    {
        _all = entries.Select(e => new PictureOption(e)).ToList();
        var chips = new List<PickerChip> { new(Loc.Get("Pictures_All"), null) };
        foreach (var (label, match) in categories)
            if (_all.Any(o => match(o.Entry))) chips.Add(new PickerChip(label, match));
        Chips = chips;
        SelectedChip = chips[0];
        SelectedChip.IsSelected = true;
        _items = Filter();
        SelectValue(selectedValue);
    }

    public IReadOnlyList<PickerChip> Chips { get; }

    /// <summary>Pictures matching the current chip and search, in display order.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoResults))]
    private IReadOnlyList<PictureOption> _items;

    [ObservableProperty] private string _search = "";

    public PickerChip SelectedChip { get; private set; }

    public bool NoResults => Items.Count == 0;

    /// <summary>Raised when the user picks a picture.</summary>
    public event Action<PictureOption>? Chosen;

    public void Choose(PictureOption option) => Chosen?.Invoke(option);

    public void SelectChip(PickerChip chip)
    {
        if (ReferenceEquals(chip, SelectedChip)) return;
        SelectedChip.IsSelected = false;
        SelectedChip = chip;
        chip.IsSelected = true;
        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>Marks the option with this stored value (e.g. "builtin:fox") as selected; null or unknown clears the selection.</summary>
    public void SelectValue(string? value)
    {
        foreach (var o in _all) o.IsSelected = value is not null && o.Entry.Value == value;
    }

    private void Refresh() => Items = Filter();

    private List<PictureOption> Filter()
    {
        var match = SelectedChip?.Match;
        return _all.Where(o => (match is null || match(o.Entry)) && PictureCatalog.Matches(o.Entry, Search)).ToList();
    }
}

