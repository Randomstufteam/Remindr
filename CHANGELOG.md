# Changelog

## v0.4.0 (2026-10-31)

> [!WARNING]
> Windows may show **"Windows protected your PC"** when you run `Remindr-Installer.exe`. Remindr isn't code-signed yet. Click **More info**, then **Run anyway**.

### Added
- **Windows notifications** for due reminders, with Complete, Snooze and Dismiss buttons. Snooze for 5 minutes, an hour, or until tomorrow.
- **Important reminders nag.** They alert you a day and an hour ahead, then every 10 minutes until you act on them.
- **Tray icon and background running.** Close the window and Remindr stays in the tray, so alerts still arrive.
- **Start with Windows**, an option in Settings that starts Remindr hidden in the tray when you sign in.
- **What's New**, a short tour of each release that shows once after an update.
- **176 languages**, up from 32, with a searchable language picker and full right-to-left layout. All of the new languages are machine-translated, many are unreviewed and some may read roughly. [Help translate](docs/TRANSLATING.md).

### Remind UI 0.4
- Cards lift, ticks pop, the calendar slides and empty pages float. With Windows animations off, everything stays still.
- The installer gets the same look.

### Fixed
- Text in taller scripts, such as Tibetan and Burmese, is no longer cut off in the side menu, the welcome greeting and reminder notes.
- Times now read back correctly in the edit dialog in every language, including Finnish and Greek.
- Traditional Chinese now uses the 24-hour clock, so afternoon times can't be mistaken for morning ones.

## v0.3.1 (2026-10-25)

### Improved
- **Faster start-up and lower memory use.** The app is now precompiled, skips start-up work it never needed, and runs without a background memory-cleanup thread.

### Fixed
- Setup no longer freezes ("not responding") when you click **Launch Remindr** at the end.

## v0.3.0 (2026-10-25)

The first public release.

> [!WARNING]
> Windows may show **"Windows protected your PC"** when you run `Remindr-Installer.exe`. Remindr isn't code-signed yet. Click **More info**, then **Run anyway**.

### Added
- **Profiles** for everyone in the family, each with a color, a picture (176 to choose from) and an optional 4-digit PIN.
- **Reminders** with a title, notes, date, time or all-day, category, an Important flag and a picture label (120 pictures).
- **All Reminders** list grouped by date, with complete, edit and delete.
- **Calendar** with Month (category dots and a day panel), Week and Agenda views. Double-click a day to add a reminder.
- **First-run welcome**: a greeting in many languages, then a 5-step setup (language, appearance, profile, PIN, done).
- **32 languages**, switchable live, with full right-to-left layout for Arabic and Hebrew. Translations are machine-assisted; corrections are welcome.
- **Remind UI**, Remindr's glass design language, in Light, Dark or System theme.
- **Animated installer** (`Remindr-Installer.exe`): per-user install with no admin prompt, a Start menu shortcut, an entry in Settings > Apps, in-place updates, and an uninstaller that keeps your data unless you ask (and then moves it to the Recycle Bin).
- **Update check** at startup: a small card appears when a newer release is out, and downloads are verified with SHA-256. You can turn it off in Settings.
- **Report a bug** and **Suggest a feature** links in Settings.

### Not yet
Notifications, the tray icon, start with Windows, recurring reminders, quick add, search and the offline AI assistant are planned for later versions.
