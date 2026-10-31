# Translating Remindr

Remindr can be shown in 176 languages. Most of them were translated by machine and have not been checked by a person, so they are tagged **Machine-translated** in the language picker. A native speaker can make a big difference, and even a handful of fixes helps.

## The quick way: report a fix

In the app, open **Settings > Language > Help translate** (or the same link in the first-run language step). It opens a short GitHub form with your language filled in. Say where you saw the text, what it says now and what it should say.

## Editing the files yourself

All text lives in `src/Remindr/Resources`:

- `Strings.resx` is the English source (the neutral file). Every key is in it.
- `Strings.<code>.resx` is one language, for example `Strings.de.resx` or `Strings.pt-BR.resx`. The code is the language code from the list in `src/Remindr/Services/LanguageCatalog.cs`.

Each entry looks like this:

```xml
<data name="Toast_Snooze" xml:space="preserve">
  <value>Später erinnern</value>
</data>
```

Change only the text inside `<value>`. Keep the `name` as it is. The English file has a `<comment>` on many entries: read it, it says where the text appears and how long it may be.

### Rules

1. **Placeholders stay.** `{0}` and `{1}` are filled in by the app (a name, a number, a date). Keep every one that the English text has, in any order your grammar needs. A test fails if one is missing or added.
2. **Never translate** `Remindr` or `Remind UI`. Keep `OK`, `PIN`, `Ctrl+N`, `Esc` and file names as they are.
3. **Do not copy the English text.** A test fails when a value is identical to the English one, unless the word really is the same in your language (like "Agenda" in many). In that case tell us, and we add it to a reviewed list in `tests/Remindr.Tests/TranslationFreezeTests.cs`.
4. **Date patterns** (`Fmt_...`) are not text. They are .NET date and time patterns (`MMMM yyyy`, `ddd d MMM`). Reorder or add literal words in quotes if your language needs it, but keep the letters valid.
5. **Keep it short.** Buttons and chips are narrow. If the English is one word, aim for one word.
6. Use the same word for the same thing everywhere (Reminder, Important, Snooze, Profile, Category, Calendar). The `docs/l10n-glossary-*.md` files list the terms chosen for the first 31 languages.
7. Save the file as UTF-8 without a byte-order mark, like the existing ones.

### Plural keys

Some texts depend on a count, for example `Calendar_DayCaption_One` and `Calendar_DayCaption_Other` ("1 reminder" and "5 reminders"). The app picks the key from the Unicode CLDR plural category of your language for that number:

| Suffix | Used for |
|---|---|
| `_Zero` | exactly 0 ("No reminders"). Optional in languages that do not need it. |
| `_One`, `_Two`, `_Few`, `_Many` | the categories your language has (for example Russian: One for 1, 21, 31; Few for 2-4; Many for 0, 5-20) |
| `_Other` | everything else, and the fallback when a category has no key |
| `_ZeroForm` | only for a language whose CLDR "zero" category covers more than 0 (Latvian: 10, 20, 11-19) |

English only has `_One` and `_Other`. If your language has more categories, **add the extra keys next to the existing ones** with the same placeholders. `https://www.unicode.org/cldr/charts/46/supplemental/language_plural_rules.html` lists the categories and example numbers for every language. A test checks that your file has a key for every category your language can produce.

## Adding or finishing a language

1. Copy `Strings.resx` to `Strings.<code>.resx` (the code must be one from `LanguageCatalog.cs`).
2. Translate every `<value>`. Delete the `<comment>` lines if you like; they are only notes for translators.
3. Run `dotnet test`. It checks that the file has every key, the placeholders match, nothing is empty, no English text was left behind, and every plural form is present.
4. Open a pull request, or attach the file to the translation issue.

When a language file exists the language appears in the picker automatically. It keeps the Machine-translated tag until a native speaker has reviewed it (`Reviewed` in `LanguageCatalog.cs`).

## How the language list works (for developers)

- `LanguageCatalog.cs` lists all 176 languages with native name, English name and direction. A language is **offered** in Settings and in setup only when its `Strings.<code>.resx` exists (`LocalizationService.Available`), so nobody sees a language that would show only English.
- `PluralRules.Generated.cs` is generated from the pinned CLDR file `tools/cldr/plurals.xml` by `tools/gen-plurals.ps1`. Do not edit it by hand.
- Dates always use the Gregorian calendar for display, even for languages whose default calendar is another one (Persian, Thai), so the month grid never changes calendar.
- Fonts: `FontPolicy.cs` chooses fallback fonts per script. Start the Debug build with `--font-check` to see every language's native name drawn in its fallback fonts.
