using System.Windows;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Remindr.Views.Dialogs;

/// <summary>New/edit/delete reminder dialogs (WPF-UI ContentDialog) and the repo work behind them.</summary>
public sealed class ReminderDialogs
{
    private readonly IContentDialogService _dialogs;
    private readonly ReminderRepo _reminders;
    private readonly CategoryRepo _categories;
    private readonly ProfileSession _session;
    private readonly IClock _clock;

    public ReminderDialogs(IContentDialogService dialogs, ReminderRepo reminders, CategoryRepo categories,
        ProfileSession session, IClock clock)
    {
        _dialogs = dialogs;
        _reminders = reminders;
        _categories = categories;
        _session = session;
        _clock = clock;
    }

    private ContentDialog NewDialog()
    {
        var host = _dialogs.GetDialogHost()
                   ?? throw new InvalidOperationException("Dialog host has not been set.");
        return new ContentDialog(host);
    }

    /// <summary>True while any ContentDialog is open on the shared host (a second one would replace it and orphan its task).</summary>
    private bool DialogOpen => _dialogs.GetDialogHost() is { Content: not null };

    /// <summary>Shows the create (existing = null) or edit dialog and saves. Returns the stored reminder, or null if cancelled.</summary>
    /// <param name="defaultDate">Date to prefill for a new reminder (null = today).</param>
    public async Task<Reminder?> ShowEditAsync(Reminder? existing, DateOnly? defaultDate = null)
    {
        if (_session.Current is not { } profile || DialogOpen) return null;

        var vm = new ReminderEditViewModel(existing, defaultDate, _clock.Now, _categories.ListByProfile(profile.Id));

        var dialog = NewDialog();
        var view = new ReminderEditDialogView(vm);
        dialog.Title = Loc.Get(existing is null ? "Reminder_Dialog_NewTitle" : "Reminder_Dialog_EditTitle");
        dialog.Content = view;
        dialog.PrimaryButtonText = Loc.Get(existing is null ? "Common_Add" : "Common_Save");
        dialog.CloseButtonText = Loc.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;
        // Esc must never close the whole dialog while the picture popup is open: the dialog's own preview handler sees the
        // key first (wherever focus is) and closes just the popup. The view swallows the matching key-up.
        dialog.PreviewKeyDown += (_, e) => view.HandleEscDown(e);
        dialog.PreviewKeyUp += (_, e) => view.HandleEscUp(e);
        DialogButtons.BindPrimaryEnabled(dialog, vm, () => vm.IsValid);
        // A primary press while invalid (e.g. UIA Invoke on the hidden button) must not discard the input: keep the dialog
        // open (same mechanism as the PIN dialog) instead of closing and re-showing it, so nothing can cycle.
        dialog.Closing += (_, e) =>
        {
            if (e.Result == ContentDialogResult.Primary && !vm.IsValid) e.Cancel = true;
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !vm.IsValid) return null;
        // The profile changed while the dialog was open: don't save into the wrong profile.
        if (_session.Current?.Id != profile.Id) return null;

        var target = existing?.Clone() ?? new Reminder();
        vm.ApplyTo(target);
        if (existing is null) return _reminders.Create(target);
        return _reminders.Update(target) ? _reminders.Get(target.Id) : null;
    }

    /// <summary>Asks for confirmation only (the caller deletes, after the card has slid out). Returns true if the user confirmed.</summary>
    public async Task<bool> ConfirmDeleteAsync(Reminder reminder)
    {
        if (DialogOpen) return false;
        var dialog = NewDialog();
        dialog.Title = Loc.Get("Reminder_Delete_Title");
        var message = new System.Windows.Controls.TextBlock
        {
            Text = Loc.Format("Reminder_Delete_Message", reminder.Title),
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

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

