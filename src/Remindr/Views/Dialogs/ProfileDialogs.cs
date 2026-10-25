using System.Windows;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Controls;
using Remindr.Views.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Remindr.Views.Dialogs;

/// <summary>Create/edit/delete profile dialogs (WPF-UI ContentDialog) and the repo work behind them.</summary>
public sealed class ProfileDialogs
{
    private readonly IContentDialogService _dialogs;
    private readonly ProfileRepo _profiles;

    private readonly AvatarService _avatars;

    public ProfileDialogs(IContentDialogService dialogs, ProfileRepo profiles, AvatarService avatars)
    {
        _dialogs = dialogs;
        _profiles = profiles;
        _avatars = avatars;
    }

    private ContentDialog NewDialog()
    {
        var host = _dialogs.GetDialogHost()
                   ?? throw new InvalidOperationException("Dialog host has not been set.");
        return new ContentDialog(host);
    }

    /// <summary>Shows the create (existing = null) or edit dialog. Returns the saved profile, or null if cancelled.</summary>
    public async Task<Profile?> ShowEditAsync(Profile? existing)
    {
        var used = _profiles.List().Select(p => p.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultColor = ProfileEditViewModel.Palette.FirstOrDefault(c => !used.Contains(c))
                           ?? ProfileEditViewModel.Palette[0];
        var vm = new ProfileEditViewModel(existing, defaultColor);

        var dialog = NewDialog();
        dialog.Title = Loc.Get(existing is null ? "Profile_Dialog_NewTitle" : "Profile_Dialog_EditTitle");
        dialog.Content = new ProfileEditDialogView(vm);
        dialog.PrimaryButtonText = Loc.Get(existing is null ? "Common_Create" : "Common_Save");
        dialog.CloseButtonText = Loc.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;
        DialogButtons.BindPrimaryEnabled(dialog, vm, () => vm.IsValid);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !vm.IsValid) return null;

        var chooser = vm.Chooser;
        if (existing is null)
        {
            var created = _profiles.Create(vm.FinalName, vm.Color, vm.FinalInitial,
                vm.PinAction == PinAction.Set ? vm.NewPin : null,
                chooser.HasUpload ? null : chooser.Avatar);
            // An uploaded picture needs the new profile's id for its file name, so it is imported after Create.
            return chooser.HasUpload ? ApplyAvatar(created, chooser) : created;
        }

        _profiles.Update(existing.Id, vm.FinalName, vm.Color, vm.FinalInitial);
        if (vm.PinAction == PinAction.Set) _profiles.SetPin(existing.Id, vm.NewPin);
        else if (vm.PinAction == PinAction.Remove) _profiles.ClearPin(existing.Id);
        return ApplyAvatar(_profiles.Get(existing.Id) ?? existing, chooser);
    }

    /// <summary>Quick "Change picture" dialog (Settings > Profile): just the chooser. Returns the updated profile, or null if cancelled.</summary>
    public async Task<Profile?> ShowAvatarAsync(Profile profile)
    {
        var chooser = new AvatarChooserViewModel(profile.Avatar, profile.Color, profile.AvatarInitial);
        var panel = new System.Windows.Controls.StackPanel { Width = 460 };
        Anim.SetFadeInOnLoad(panel, true);
        Anim.SetSlide(panel, 0);
        panel.Children.Add(new ProfileAvatar
        {
            Diameter = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 16),
            Color = chooser.Color,
            Initial = chooser.Initial
        });
        var preview = (ProfileAvatar)panel.Children[0];
        void Sync()
        {
            preview.PreviewImage = chooser.PreviewImage;
            preview.Avatar = chooser.Avatar;
        }
        Sync();
        chooser.PropertyChanged += (_, _) => Sync();
        panel.Children.Add(new AvatarChooserView { DataContext = chooser });

        var dialog = NewDialog();
        dialog.Title = Loc.Get("Profile_Dialog_ChangePictureTitle");
        dialog.Content = panel;
        dialog.PrimaryButtonText = Loc.Get("Common_Save");
        dialog.CloseButtonText = Loc.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return ApplyAvatar(profile, chooser);
    }

    /// <summary>Stores the chooser's result (imports an uploaded file, or sets/clears a built-in) and returns the fresh profile.</summary>
    private Profile ApplyAvatar(Profile profile, AvatarChooserViewModel chooser)
    {
        try
        {
            if (chooser.UploadPath is { } path)
            {
                var value = _avatars.ImportFile(profile.Id, path);
                _profiles.SetAvatar(profile.Id, value);
            }
            else if (chooser.Avatar != profile.Avatar)
            {
                _profiles.SetAvatar(profile.Id, chooser.Avatar);
                // Leaving an uploaded picture behind for a built-in/initial: drop the now-unused file.
                if (profile.Avatar?.StartsWith(AvatarCatalog.FilePrefix, StringComparison.Ordinal) == true)
                    AvatarService.DeleteFile(_avatars.DirectoryPath, profile.Id);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Saving profile picture");
        }
        return _profiles.Get(profile.Id) ?? profile;
    }

    /// <summary>
    /// Set / Change / Remove the PIN of <paramref name="profile"/>. Change and Remove require the current PIN; a wrong one shows
    /// an inline error and keeps the dialog open. Returns true if the PIN was changed.
    /// </summary>
    public async Task<bool> ShowPinAsync(Profile profile, PinMode mode)
    {
        // Verify against the stored hash, not a possibly stale copy.
        var fresh = _profiles.Get(profile.Id) ?? profile;
        var view = new PinDialogView(mode, fresh);

        var dialog = NewDialog();
        dialog.Title = Loc.Get(mode switch { PinMode.Set => "Pin_Set", PinMode.Change => "Pin_Change", _ => "Pin_Remove" });
        dialog.Content = view;
        dialog.PrimaryButtonText = Loc.Get(mode == PinMode.Remove ? "Pin_Remove" : "Common_Save");
        dialog.CloseButtonText = Loc.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = view.IsComplete;
        view.StateChanged += (_, _) => dialog.IsPrimaryButtonEnabled = view.IsComplete;

        // Cancel the close (dialog stays open) when the current PIN is wrong.
        dialog.Closing += (_, e) =>
        {
            if (e.Result == ContentDialogResult.Primary && !view.Validate()) e.Cancel = true;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        if (mode == PinMode.Remove) _profiles.ClearPin(fresh.Id);
        else _profiles.SetPin(fresh.Id, view.NewPin);
        return true;
    }

    /// <summary>Confirms and deletes. Returns true if deleted. The last remaining profile cannot be deleted.</summary>
    public async Task<bool> ConfirmDeleteAsync(Profile profile)
    {
        var dialog = NewDialog();
        if (_profiles.Count() <= 1)
        {
            dialog.Title = Loc.Get("Profile_Delete_LastTitle");
            dialog.Content = Loc.Get("Profile_Delete_LastMessage");
            dialog.CloseButtonText = Loc.Get("Common_Ok");
            await dialog.ShowAsync();
            return false;
        }

        dialog.Title = Loc.Format("Profile_Delete_Title", profile.Name);
        var message = new System.Windows.Controls.TextBlock
        {
            Text = Loc.Format("Profile_Delete_Message", profile.Name),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380
        };
        Anim.SetFadeInOnLoad(message, true);
        Anim.SetSlide(message, 0);
        dialog.Content = message;
        dialog.PrimaryButtonText = Loc.Get("Common_Delete");
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = Loc.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Close;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        return _profiles.Delete(profile.Id);
    }
}

