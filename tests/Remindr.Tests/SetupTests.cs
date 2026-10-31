using System.IO;
using Dapper;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;

namespace Remindr.Tests;

public class SetupViewModelTests
{
    private sealed class Rig : IDisposable
    {
        public TempDb T { get; } = new();
        public ProfileRepo Profiles { get; }
        public SettingsRepo Settings { get; }
        public List<string> PreviewedLanguages { get; } = new();
        public List<AppTheme> PreviewedThemes { get; } = new();
        public SetupViewModel Vm { get; }

        public Rig(string language = "en", AppTheme theme = AppTheme.System)
        {
            new DbInitializer(T.Db).Initialize();
            Profiles = new ProfileRepo(T.Db, Path.Combine(T.Dir, "avatars"));
            Settings = new SettingsRepo(T.Db);
            Vm = new SetupViewModel(Profiles, language, theme, PreviewedLanguages.Add, PreviewedThemes.Add);
        }

        /// <summary>Walks to the PIN page with a valid name.</summary>
        public void ToPinPage(string name = "Sam")
        {
            Assert.True(Vm.Next());                 // Language -> Appearance
            Assert.True(Vm.Next());                 // Appearance -> Profile
            Vm.Name = name;
            Assert.True(Vm.Next());                 // Profile -> Pin
            Assert.Equal(SetupStep.Pin, Vm.Step);
        }

        public int Count(string table) { using var c = T.Db.Open(); return c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}"); }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Steps_RunInOrder_LanguageAppearanceProfilePinDone()
    {
        using var r = new Rig();
        Assert.Equal(SetupStep.Language, r.Vm.Step);
        Assert.Equal(0, r.Vm.StepIndex);
        Assert.Equal(5, r.Vm.Count);

        Assert.True(r.Vm.Next());
        Assert.Equal(SetupStep.Appearance, r.Vm.Step);
        Assert.True(r.Vm.Next());
        Assert.Equal(SetupStep.Profile, r.Vm.Step);
        r.Vm.Name = "Sam";
        Assert.True(r.Vm.Next());
        Assert.Equal(SetupStep.Pin, r.Vm.Step);
        Assert.True(r.Vm.Next());
        Assert.Equal(SetupStep.Done, r.Vm.Step);
        Assert.Equal(4, r.Vm.StepIndex);
        Assert.False(r.Vm.Next());                 // nothing after Done
        Assert.False(r.Vm.NextIsVisible);
    }

    [Fact]
    public void Back_IsNotOfferedOnTheFirstPage_AndKeepsEverythingEntered()
    {
        using var r = new Rig();
        Assert.False(r.Vm.CanBack);
        Assert.False(r.Vm.Back());

        r.Vm.LanguageCode = "de";
        r.Vm.Next();
        r.Vm.Theme = AppTheme.Dark;
        r.Vm.Next();
        r.Vm.Name = "Sam";
        r.Vm.Color = "#EF4444";
        r.Vm.Avatar = "builtin:panda";
        r.Vm.Next();
        r.Vm.Pin = "4821";
        r.Vm.PinConfirm = "4821";

        // Back to the start and forward again: nothing was lost.
        Assert.True(r.Vm.Back());                      // Pin -> Profile
        Assert.True(r.Vm.Back());                      // Profile -> Appearance
        Assert.True(r.Vm.Back());                      // Appearance -> Language
        Assert.False(r.Vm.CanBack);
        Assert.Equal("de", r.Vm.LanguageCode);
        Assert.Equal(AppTheme.Dark, r.Vm.Theme);
        Assert.Equal("Sam", r.Vm.Name);
        Assert.Equal("#EF4444", r.Vm.Color);
        Assert.Equal("builtin:panda", r.Vm.Avatar);
        Assert.Equal("4821", r.Vm.Pin);
        Assert.True(r.Vm.Next());
        Assert.True(r.Vm.Next());
        Assert.True(r.Vm.CanNext);                     // the name is still there
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Sam", true)]
    [InlineData("  Sam  ", true)]
    [InlineData("123456789012345678901234567890", true)]      // 30 characters
    [InlineData("1234567890123456789012345678901", false)]    // 31
    public void ProfilePage_NeedsANonBlankName(string name, bool canNext)
    {
        using var r = new Rig();
        r.Vm.Next();
        r.Vm.Next();
        r.Vm.Name = name;
        Assert.Equal(SetupStep.Profile, r.Vm.Step);
        Assert.Equal(canNext, r.Vm.CanNext);
        Assert.Equal(canNext, r.Vm.Next());
        Assert.Equal(canNext ? SetupStep.Pin : SetupStep.Profile, r.Vm.Step);
    }

    [Fact]
    public void Initial_FollowsTheName()
    {
        using var r = new Rig();
        Assert.Equal("?", r.Vm.Initial);
        r.Vm.Name = " sam";
        Assert.Equal("S", r.Vm.Initial);
        Assert.Equal("sam", r.Vm.DisplayName);
    }

    [Theory]
    [InlineData("", "", true)]            // no PIN at all is fine (same as Skip)
    [InlineData("12", "12", false)]       // partial
    [InlineData("1234", "", false)]       // not repeated
    [InlineData("1234", "1235", false)]   // mismatch
    [InlineData("1234", "1234", true)]
    public void PinPage_AcceptsNothingOrACompleteMatchingPin(string pin, string confirm, bool canNext)
    {
        using var r = new Rig();
        r.ToPinPage();
        r.Vm.Pin = pin;
        r.Vm.PinConfirm = confirm;
        Assert.Equal(canNext, r.Vm.CanNext);
        Assert.Equal(pin == confirm && pin.Length == 4, r.Vm.HasPin);
    }

    [Fact]
    public void SkipPin_LeavesNoPinHash_AndMovesToDone()
    {
        using var r = new Rig();
        r.ToPinPage();
        Assert.True(r.Vm.CanSkip);
        r.Vm.Pin = "12";                 // half typed, then skipped
        Assert.True(r.Vm.Skip());
        Assert.Equal(SetupStep.Done, r.Vm.Step);
        Assert.False(r.Vm.HasPin);

        var p = r.Vm.Complete();
        using var c = r.T.Db.Open();
        Assert.Null(c.ExecuteScalar<string?>("SELECT pin_hash FROM profiles WHERE id=@id", new { id = p.Id }));
        Assert.Null(c.ExecuteScalar<string?>("SELECT pin_salt FROM profiles WHERE id=@id", new { id = p.Id }));
        Assert.False(p.HasPin);
    }

    [Fact]
    public void Skip_IsOnlyOfferedOnThePinPage()
    {
        using var r = new Rig();
        Assert.False(r.Vm.CanSkip);
        Assert.False(r.Vm.Skip());
        Assert.Equal(SetupStep.Language, r.Vm.Step);
    }

    [Fact]
    public void Complete_CreatesOneProfileWithThreeCategories_AndSavesLanguageThemeAndLastProfile()
    {
        using var r = new Rig();
        r.Vm.LanguageCode = "de";
        r.Vm.Next();
        r.Vm.Theme = AppTheme.Dark;
        r.Vm.Next();
        r.Vm.Name = "Sam";
        r.Vm.Color = "#EF4444";
        r.Vm.Avatar = "builtin:panda";
        r.Vm.Next();
        r.Vm.Pin = "4821";
        r.Vm.PinConfirm = "4821";
        r.Vm.Next();

        var p = r.Vm.Complete();

        Assert.Equal(1, r.Count("profiles"));
        Assert.Equal(3, r.Count("categories"));
        Assert.Equal("Sam", p.Name);
        Assert.Equal("#EF4444", p.Color);
        Assert.Equal("S", p.AvatarInitial);
        Assert.Equal("builtin:panda", p.Avatar);
        Assert.True(p.HasPin);
        Assert.True(PinHasher.Verify("4821", p.PinHash, p.PinSalt));
        Assert.Equal("de", r.Settings.Get("language"));
        Assert.Equal("Dark", r.Settings.Get("theme"));
        Assert.Equal(p.Id.ToString(), r.Settings.Get("last_profile_id"));
        Assert.Equal(SetupStep.Done, r.Vm.Step);
        Assert.False(r.Vm.CanBack);                        // the profile exists now
        Assert.Same(p, r.Vm.Created);
    }

    [Theory]
    [InlineData("en", "School", "Work", "Personal")]
    [InlineData("de", "Schule", "Arbeit", "Privat")]
    [InlineData("ja", "学校", "仕事", "個人")]
    [InlineData("ar", "المدرسة", "العمل", "شخصي")]
    public void Complete_NamesTheDefaultCategoriesInTheChosenLanguage(string language, string school, string work, string personal)
    {
        using var r = new Rig();
        r.Vm.LanguageCode = language;
        r.Vm.Next();
        r.Vm.Next();
        r.Vm.Name = "Sam";
        var p = r.Vm.Complete();

        var cats = new CategoryRepo(r.T.Db).ListByProfile(p.Id);
        Assert.Equal(new[] { school, work, personal }, cats.Select(c => c.Name).ToArray());
        // Colours and order do not change with the language.
        Assert.Equal(new[] { "#3B82F6", "#F59E0B", "#10B981" }, cats.Select(c => c.Color).ToArray());
    }

    [Fact]
    public void Complete_WithoutPin_SetsNoPin_AndWithoutAvatarUsesTheInitial()
    {
        using var r = new Rig();
        r.Vm.Name = "Sam";
        var p = r.Vm.Complete();
        Assert.False(p.HasPin);
        Assert.Null(p.Avatar);
    }

    [Fact]
    public void Complete_IsIdempotent_AndNeedsAName()
    {
        using var r = new Rig();
        Assert.Throws<InvalidOperationException>(() => r.Vm.Complete());
        Assert.Equal(0, r.Count("profiles"));

        r.Vm.Name = "Sam";
        var first = r.Vm.Complete();
        var second = r.Vm.Complete();
        Assert.Same(first, second);
        Assert.Equal(1, r.Count("profiles"));
        Assert.Equal(3, r.Count("categories"));
    }

    [Fact]
    public void AbandoningBeforeComplete_WritesNothing()
    {
        using var r = new Rig();
        r.Vm.LanguageCode = "fr";
        r.Vm.Next();
        r.Vm.Theme = AppTheme.Dark;
        r.Vm.Next();
        r.Vm.Name = "Sam";
        r.Vm.Color = "#10B981";
        r.Vm.Next();
        r.Vm.Pin = "1234";
        r.Vm.PinConfirm = "1234";
        // ...and the window is closed here, without Complete().

        Assert.Equal(0, r.Count("profiles"));
        Assert.Equal(0, r.Count("categories"));
        Assert.Equal(0, r.Count("app_settings"));
        Assert.Null(r.Settings.Get("language"));
        Assert.Null(r.Settings.Get("theme"));
        Assert.Null(r.Settings.Get("last_profile_id"));
        Assert.True(SetupViewModel.ShouldRun(r.Profiles));        // so setup shows again next launch
    }

    [Fact]
    public void LanguageAndTheme_ArePreviewedLive_ButOnlyThroughTheCallbacks()
    {
        using var r = new Rig(language: "en", theme: AppTheme.System);
        r.Vm.LanguageCode = "es";
        r.Vm.LanguageCode = "ja";
        r.Vm.Theme = AppTheme.Light;
        Assert.Equal(new[] { "es", "ja" }, r.PreviewedLanguages);
        Assert.Equal(new[] { AppTheme.Light }, r.PreviewedThemes);
        Assert.Equal("日本語", r.Vm.LanguageNativeName);
    }

    [Fact]
    public void Constructor_StartsFromTheCurrentLanguageAndTheme_WithoutPreviewing()
    {
        using var r = new Rig(language: "pt-BR", theme: AppTheme.Dark);
        Assert.Equal("pt-BR", r.Vm.LanguageCode);
        Assert.Equal(AppTheme.Dark, r.Vm.Theme);
        Assert.Empty(r.PreviewedLanguages);
        Assert.Empty(r.PreviewedThemes);

        // An unknown language falls back to English.
        using var r2 = new Rig(language: "xx");
        Assert.Equal("en", r2.Vm.LanguageCode);
    }

    [Fact]
    public void ShouldRun_IsTrueOnlyWhenThereAreNoProfiles()
    {
        using var r = new Rig();
        Assert.True(SetupViewModel.ShouldRun(r.Profiles));
        r.Profiles.Create("Alex", "#EF4444", "A");
        Assert.False(SetupViewModel.ShouldRun(r.Profiles));
    }

    [Fact]
    public void ProgressDots_FollowTheStep_AndAreNamedStepNOfFive()
    {
        using var r = new Rig();
        Assert.Equal(5, r.Vm.Dots.Count);
        Assert.Equal("Step 1 of 5", r.Vm.Dots[0].AutomationName);
        Assert.Equal("Step 5 of 5", r.Vm.Dots[4].AutomationName);
        Assert.True(r.Vm.Dots[0].IsCurrent);

        r.Vm.Next();
        r.Vm.Next();
        Assert.True(r.Vm.Dots[0].IsDone);
        Assert.True(r.Vm.Dots[1].IsDone);
        Assert.True(r.Vm.Dots[2].IsCurrent);
        Assert.False(r.Vm.Dots[3].IsCurrent || r.Vm.Dots[3].IsDone);
    }

    [Fact]
    public void SetupLanguages_AreTheShippedLanguages_NeverThePseudoLocale()
    {
        var shipped = LocalizationService.Available.Count(l => l.Code != LocalizationService.PseudoCode);
        Assert.Equal(shipped, SetupViewModel.SetupLanguages.Count);
        Assert.True(shipped >= 32);
        Assert.DoesNotContain(SetupViewModel.SetupLanguages, l => l.Code == LocalizationService.PseudoCode);
        // Only languages with text: a catalog entry whose translation has not landed is not offered.
        Assert.All(SetupViewModel.SetupLanguages, l => Assert.True(LocalizationService.IsTranslated(l.Code), l.Code));
    }

    [Theory]
    [InlineData("deutsch", "de")]
    [InlineData("german", "de")]
    [InlineData("espanol", "es")]          // accents ignored
    [InlineData("ESPAÑOL", "es")]          // case ignored
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("日本", "ja")]
    [InlineData("suomi", "fi")]
    public void FilterLanguages_MatchesNativeEnglishNamesAndCodes(string search, string expectedFirst)
    {
        var found = SetupViewModel.FilterLanguages(search, SetupViewModel.SetupLanguages);
        Assert.Equal(expectedFirst, found[0].Code);
    }

    [Theory]
    [InlineData("portugu")]
    [InlineData("port")]
    public void FilterLanguages_Portuguese_FindsBothAndNothingElse(string search) =>
        Assert.Equal(new[] { "pt-BR", "pt-PT" }, SetupViewModel.FilterLanguages(search, SetupViewModel.SetupLanguages).Select(l => l.Code).OrderBy(c => c).ToArray());

    [Theory]
    [InlineData("", -1)]
    [InlineData("  ", -1)]
    [InlineData("zzzz", 0)]
    public void FilterLanguages_EmptyListsAll_NoMatchListsNone(string search, int expected)
    {
        var all = SetupViewModel.SetupLanguages.Count;
        Assert.Equal(expected < 0 ? all : expected, SetupViewModel.FilterLanguages(search, SetupViewModel.SetupLanguages).Count);
    }

    [Fact]
    public void SuggestedLanguage_IsTheOneSetupStartedWith_AndStaysWhenTheChoiceChanges()
    {
        using var r = new Rig();
        var suggested = r.Vm.SuggestedLanguageCode;
        r.Vm.LanguageCode = "fr";
        Assert.Equal(suggested, r.Vm.SuggestedLanguageCode);
        Assert.Equal("fr", r.Vm.LanguageCode);
    }

    [Fact]
    public void CreateFirst_WhenAProfileAlreadyExists_Throws_AndAddsNothing()
    {
        using var r = new Rig();
        r.Profiles.CreateFirst("Sam", "#EF4444", "S", null, null, new[] { "a", "b", "c" }, new Dictionary<string, string>());
        var cats = r.Count("categories");
        Assert.Throws<FirstRunAlreadyCompletedException>(() =>
            r.Profiles.CreateFirst("Eve", "#EF4444", "E", null, null, new[] { "a", "b", "c" }, new Dictionary<string, string>()));
        Assert.Equal(1, r.Count("profiles"));
        Assert.Equal(cats, r.Count("categories"));
    }

    [Fact]
    public void CreateFirst_IsOneTransaction_ABadSettingRollsBackTheProfile()
    {
        using var r = new Rig();
        // A NULL value violates app_settings.value NOT NULL, so the whole first-run write must fail and leave nothing behind.
        var bad = new Dictionary<string, string> { ["language"] = null! };
        Assert.ThrowsAny<Exception>(() =>
            r.Profiles.CreateFirst("Sam", "#EF4444", "S", null, null, new[] { "a", "b", "c" }, bad));
        Assert.Equal(0, r.Count("profiles"));
        Assert.Equal(0, r.Count("categories"));
        Assert.Equal(0, r.Count("app_settings"));
    }
}

public class AppPathsTests
{
    [Fact]
    public void WithoutTheVariable_DataLivesInAppDataRemindr()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Remindr");
        foreach (var value in new string?[] { null, "", "   " })
        {
            var p = AppPaths.FromEnvironmentValue(value);
            Assert.Equal(expected, p.DataDir);
            Assert.False(p.IsOverridden);
        }
    }

    [Fact]
    public void WithTheVariable_EveryPathMovesIntoThatFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "remindr-paths-" + Guid.NewGuid().ToString("N"));
        var p = AppPaths.FromEnvironmentValue(dir);
        Assert.True(p.IsOverridden);
        Assert.Equal(dir, p.DataDir);
        Assert.Equal(Path.Combine(dir, "remindr.db"), p.DbFile);
        Assert.Equal(Path.Combine(dir, "avatars"), p.AvatarsDir);
        Assert.Equal(Path.Combine(dir, "error.log"), p.ErrorLogFile);
    }

    [Fact]
    public void RelativeAndPaddedValues_BecomeAbsolutePaths()
    {
        var p = AppPaths.FromEnvironmentValue("  some-folder  ");
        Assert.True(Path.IsPathRooted(p.DataDir));
        Assert.EndsWith("some-folder", p.DataDir);
        Assert.True(p.IsOverridden);
    }

    [Fact]
    public void AnUnusableValue_FallsBackToTheDefault()
    {
        var p = AppPaths.FromEnvironmentValue("C:\\bad\0path");
        Assert.False(p.IsOverridden);
    }

    [Fact]
    public void DbAvatarsAndErrorLog_AllHonourTheSameFolder()
    {
        // The static defaults are computed from AppPaths.Current (the process environment).
        Assert.Equal(AppPaths.Current.DbFile, Db.DefaultPath);
        Assert.Equal(AppPaths.Current.AvatarsDir, AvatarService.DefaultDirectory);
        Assert.Equal(AppPaths.Current.ErrorLogFile, ErrorLog.FilePath);
        Assert.Equal(AppPaths.Current.DataDir, Path.GetDirectoryName(Db.DefaultPath));
        Assert.Equal(AppPaths.Current.DataDir, Path.GetDirectoryName(AvatarService.DefaultDirectory));
        Assert.Equal(AppPaths.Current.DataDir, Path.GetDirectoryName(ErrorLog.FilePath));
    }

    [Fact]
    public void AnOverriddenFolder_GetsTheDatabaseCreatedInIt_AndNothingInAppData()
    {
        var dir = Path.Combine(Path.GetTempPath(), "remindr-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = AppPaths.FromEnvironmentValue(dir);
            var db = new Db(p.DbFile);
            new DbInitializer(db).Initialize();
            Assert.True(File.Exists(p.DbFile));

            var profiles = new ProfileRepo(db, p.AvatarsDir);
            Assert.True(SetupViewModel.ShouldRun(profiles));          // an empty folder is a first run
            var setup = new SetupViewModel(profiles, "en", AppTheme.System);
            setup.Name = "Sam";
            setup.Complete();
            Assert.False(SetupViewModel.ShouldRun(profiles));
            Assert.StartsWith(dir, db.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }
}
