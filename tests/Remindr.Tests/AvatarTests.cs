using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dapper;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;

namespace Remindr.Tests;

internal static class Sta
{
    /// <summary>WPF imaging/rendering wants an STA thread; xunit's are MTA.</summary>
    public static void Run(Action a)
    {
        Exception? error = null;
        var th = new Thread(() => { try { a(); } catch (Exception e) { error = e; } });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (error is not null) throw new Exception("STA thread failed: " + error.Message, error);
    }

    public static string PicturesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "src", "Remindr", "Assets", "Pictures");
            if (Directory.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Assets/Pictures not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Writes a PNG of the given size; <paramref name="pixel"/> returns BGRA for each x,y.</summary>
    public static void WritePng(string path, int w, int h, Func<int, int, (byte b, byte g, byte r, byte a)> pixel)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (b, g, r, a) = pixel(x, y);
                var i = (y * w + x) * 4;
                data[i] = b; data[i + 1] = g; data[i + 2] = r; data[i + 3] = a;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, data, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

public class AvatarCatalogTests
{
    private static readonly string[] OldKeys =
    {
        "person", "man", "woman", "boy", "girl", "child", "baby", "older_person", "old_man", "old_woman",
        "man_beard", "man_red_hair", "woman_curly_hair", "person_white_hair",
        "artist", "astronaut", "cook", "detective", "farmer", "firefighter", "health_worker", "pilot",
        "scientist", "singer", "student", "teacher", "technologist", "ninja", "prince", "princess",
        "cat_face", "dog_face", "mouse_face", "hamster", "rabbit_face", "fox", "bear", "panda", "koala",
        "tiger_face", "lion", "pig_face", "frog", "monkey_face", "penguin", "hatching_chick", "owl", "parrot",
        "hedgehog", "otter", "sloth", "turtle", "butterfly", "dolphin", "dragon_face", "sauropod", "unicorn",
        "alien", "ghost", "robot", "nerd_face", "smiling_face_with_sunglasses", "cowboy_hat_face"
    };

    [Fact]
    public void Catalog_Limits_AndUniqueKeys()
    {
        var all = PictureCatalog.All;
        Assert.Equal(all.Count, all.Select(e => e.Key).Distinct().Count());
        Assert.True(all.Count <= 280, $"{all.Count} unique files");
        Assert.True(AvatarCatalog.All.Count <= 200, $"{AvatarCatalog.All.Count} avatars");
        Assert.True(PictureCatalog.ForReminders().Count() <= 120, $"{PictureCatalog.ForReminders().Count()} reminder pictures");
        Assert.All(all, e => Assert.True(e.AvatarGroup is not null || e.ReminderGroup is not null, e.Key));
        Assert.Equal("Smiling Face With Sunglasses", PictureCatalog.Label("smiling_face_with_sunglasses"));
    }

    [Fact]
    public void Catalog_Categories_AreAsListed()
    {
        Assert.Equal(8, Enum.GetValues<AvatarCategory>().Length);
        Assert.Equal(new[] { "School", "Work", "Health", "Home", "Food", "Money", "Travel", "Celebrations", "Sports" },
            Enum.GetNames<ReminderCategory>());
        foreach (var c in Enum.GetValues<AvatarCategory>())
            Assert.Contains(AvatarCatalog.All, e => e.AvatarGroup == c);
        foreach (var c in Enum.GetValues<ReminderCategory>())
            Assert.InRange(PictureCatalog.ForReminders().Count(e => e.ReminderGroup == c), 9, 16);
    }

    [Fact]
    public void AllOld63Keys_StillResolve()
    {
        Assert.Equal(63, OldKeys.Length);
        foreach (var k in OldKeys)
        {
            Assert.NotNull(PictureCatalog.FromValue("builtin:" + k));
            Assert.Contains(AvatarCatalog.All, e => e.Key == k);
        }
    }

    [Fact]
    public void Search_MatchesLabelAndTags()
    {
        var cat = PictureCatalog.All.Where(e => PictureCatalog.Matches(e, "cat")).Select(e => e.Key).ToList();
        Assert.Contains("cat_face", cat);
        Assert.True(PictureCatalog.Matches(PictureCatalog.Find("fox")!, ""));
        Assert.False(PictureCatalog.Matches(PictureCatalog.Find("fox")!, "zzzz"));
    }

    [Fact]
    public void EveryCatalogKey_HasAPngInTheAssetsFolder()
    {
        var folder = Sta.PicturesFolder();
        foreach (var e in PictureCatalog.All)
            Assert.True(File.Exists(Path.Combine(folder, e.Key + ".png")), $"missing {e.Key}.png");

        // ...and every PNG in the folder is in the catalog (no orphans).
        var keys = PictureCatalog.All.Select(e => e.Key).ToHashSet();
        foreach (var f in Directory.GetFiles(folder, "*.png"))
            Assert.Contains(Path.GetFileNameWithoutExtension(f), keys);
    }

    [Fact]
    public void EveryCatalogKey_IsEmbeddedAsAResourceInTheApp()
    {
        var asm = typeof(AvatarCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream("Remindr.g.resources")!;
        using var reader = new ResourceReader(stream);
        var names = reader.Cast<System.Collections.DictionaryEntry>().Select(d => (string)d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in PictureCatalog.All)
            Assert.Contains($"assets/pictures/{e.Key}.png", names);
    }

    [Fact]
    public void Load_BuiltinKey_ReturnsImage_AndBadValuesReturnNullWithoutThrowing()
    {
        Sta.Run(() =>
        {
            Assert.Equal(AvatarCatalog.All.Count, AvatarCatalog.Available.Count);
            Assert.NotNull(AvatarCatalog.Load("builtin:fox"));
            Assert.NotNull(AvatarCatalog.Load("builtin:fox", null, 64));
            Assert.NotNull(AvatarCatalog.LoadBuiltin("builtin:fox", 32));
            Assert.Null(AvatarCatalog.Load(null));
            Assert.Null(AvatarCatalog.Load(""));
            Assert.Null(AvatarCatalog.Load("builtin:nope"));
            Assert.Null(AvatarCatalog.Load("builtin:../fox"));
            Assert.Null(AvatarCatalog.LoadBuiltin("builtin:nope", 32));
            Assert.Null(AvatarCatalog.Load("file:avatars/../../evil.png"));
            Assert.Null(AvatarCatalog.Load("file:avatars/999.png", Path.Combine(Path.GetTempPath(), "remindr-none-" + Guid.NewGuid().ToString("N"))));
            Assert.Null(AvatarCatalog.Load("garbage"));
        });
    }
}
public class MigrationV2Tests
{
    [Fact]
    public void V1Database_UpgradesToV2_KeepingData()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        using (var conn = t.Db.Open())
        {
            conn.Execute(Migrations.All[0].Sql);
            conn.Execute("INSERT INTO schema_version(version) VALUES (1)");
            conn.Execute(@"INSERT INTO profiles(name, color, avatar_initial, pin_hash, pin_salt, created_at)
                           VALUES ('Real', '#EF4444', 'R', 'h', 's', '2026-01-01T00:00:00Z')");
        }

        new DbInitializer(t.Db).Initialize();

        using var c = t.Db.Open();
        Assert.Equal(Migrations.Latest, c.ExecuteScalar<int>("SELECT version FROM schema_version"));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM schema_version"));
        var cols = c.Query<string>("SELECT name FROM pragma_table_info('profiles')").ToList();
        Assert.Contains("avatar", cols);
        var p = new ProfileRepo(t.Db).List().Single();
        Assert.Equal(("Real", "#EF4444", "R", "h"), (p.Name, p.Color, p.AvatarInitial, p.PinHash));
        Assert.Null(p.Avatar);
    }
}

public class ProfileAvatarRepoTests
{
    [Fact]
    public void Avatar_RoundTrips_ThroughCreateSetAvatarAndClear()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var repo = new ProfileRepo(t.Db, Path.Combine(t.Dir, "avatars"));

        var p = repo.Create("A", "#EF4444", "A", avatar: "builtin:fox");
        Assert.Equal("builtin:fox", repo.Get(p.Id)!.Avatar);

        repo.SetAvatar(p.Id, "builtin:panda");
        Assert.Equal("builtin:panda", repo.List().Single().Avatar);

        // Update() (name/colour/initial) must not touch the picture.
        repo.Update(p.Id, "B", "#10B981", "B");
        Assert.Equal("builtin:panda", repo.Get(p.Id)!.Avatar);

        repo.SetAvatar(p.Id, null);
        Assert.Null(repo.Get(p.Id)!.Avatar);
    }

    [Fact]
    public void Delete_RemovesTheUploadedPictureFile()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var dir = Path.Combine(t.Dir, "avatars");
        var repo = new ProfileRepo(t.Db, dir);
        repo.Create("Keep", "#EF4444", "K");
        var gone = repo.Create("Gone", "#10B981", "G");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, gone.Id + ".png");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

        Assert.True(repo.Delete(gone.Id));
        Assert.False(File.Exists(file));
    }
}

public class AvatarImportTests
{
    [Fact]
    public void ImportFile_WideImage_IsCenterCroppedTo256x256()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        var src = Path.Combine(t.Dir, "wide.png");
        // 600x200: left third red, middle third green, right third blue; the centre square is all green.
        Sta.WritePng(src, 600, 200, (x, _) => x < 200 ? ((byte)0, (byte)0, (byte)255, (byte)255)
                                          : x < 400 ? ((byte)0, (byte)255, (byte)0, (byte)255)
                                          : ((byte)255, (byte)0, (byte)0, (byte)255));
        var dir = Path.Combine(t.Dir, "avatars");

        string stored = "";
        Sta.Run(() => stored = new AvatarService(dir).ImportFile(7, src));

        Assert.Equal("file:avatars/7.png", stored);
        var outPath = Path.Combine(dir, "7.png");
        Assert.True(File.Exists(outPath));
        Sta.Run(() =>
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(outPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            Assert.Equal(256, bmp.PixelWidth);
            Assert.Equal(256, bmp.PixelHeight);
            var px = new byte[4];
            new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(10, 128, 1, 1), px, 4, 0);
            Assert.True(px[1] > 200 && px[2] < 60 && px[0] < 60, $"expected green, got BGRA {px[0]},{px[1]},{px[2]},{px[3]}");

            // Load() reads the stored value back through the same path the control uses.
            Assert.NotNull(AvatarCatalog.Load(stored, dir));
        });
    }

    [Fact]
    public void ImportFile_SmallTransparentPng_UpscalesTo256x256_KeepingTransparency()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        var src = Path.Combine(t.Dir, "small.png");
        Sta.WritePng(src, 40, 60, (_, _) => ((byte)0, (byte)0, (byte)0, (byte)0));

        byte[] png = Array.Empty<byte>();
        Sta.Run(() => png = AvatarService.ToAvatarPng(src));

        Sta.Run(() =>
        {
            var dec = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var f = dec.Frames[0];
            Assert.Equal((256, 256), (f.PixelWidth, f.PixelHeight));
            var px = new byte[4];
            new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(100, 100, 1, 1), px, 4, 0);
            Assert.Equal(0, px[3]);
        });
    }

    [Fact]
    public void ImportFile_NotAnImage_Throws()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        var src = Path.Combine(t.Dir, "bad.png");
        File.WriteAllText(src, "not an image");
        Assert.ThrowsAny<Exception>(() => Sta.Run(() => new AvatarService(Path.Combine(t.Dir, "a")).ImportFile(1, src)));
    }
}




public class PicturePickerViewModelTests
{
    private static PicturePickerViewModel AvatarPicker(string? selected = null) => Sta_Create(selected);

    private static PicturePickerViewModel Sta_Create(string? selected) =>
        new(AvatarCatalog.All,
            Enum.GetValues<AvatarCategory>().Select(c => (PictureCatalog.CategoryLabel(c), (Func<PictureEntry, bool>)(e => e.AvatarGroup == c))),
            selected);

    [Fact]
    public void Chips_AreAllPlusEveryCategory_AndAllShowsEverything()
    {
        var vm = AvatarPicker();
        Assert.Equal("All", vm.Chips[0].Label);
        Assert.Equal(1 + Enum.GetValues<AvatarCategory>().Length, vm.Chips.Count);
        Assert.Equal(AvatarCatalog.All.Count, vm.Items.Count);
        Assert.True(vm.Chips[0].IsSelected);
    }

    [Fact]
    public void SelectChip_FiltersToThatCategory()
    {
        var vm = AvatarPicker();
        var animals = vm.Chips.First(c => c.Label == "Animals");
        vm.SelectChip(animals);
        Assert.NotEmpty(vm.Items);
        Assert.All(vm.Items, o => Assert.Equal(AvatarCategory.Animals, o.Entry.AvatarGroup));
        Assert.False(vm.Chips[0].IsSelected);
    }

    [Fact]
    public void Search_FindsCatFace_AndEmptyResultSetsNoResults()
    {
        var vm = AvatarPicker();
        vm.Search = "cat";
        Assert.Contains(vm.Items, o => o.Entry.Key == "cat_face");
        vm.Search = "zzzzqq";
        Assert.True(vm.NoResults);
        vm.Search = "";
        Assert.False(vm.NoResults);
    }

    [Fact]
    public void SelectValue_MarksOnlyThatOption()
    {
        var vm = AvatarPicker("builtin:fox");
        Assert.Single(vm.Items, o => o.IsSelected);
        Assert.True(vm.Items.First(o => o.Entry.Key == "fox").IsSelected);
        vm.SelectValue(null);
        Assert.DoesNotContain(vm.Items, o => o.IsSelected);
    }

    [Fact]
    public void AvatarChooser_ChoosingAPicture_SetsAvatar_AndInitialOnlyClearsIt()
    {
        Sta.Run(() =>
        {
            var vm = new AvatarChooserViewModel("builtin:fox");
            var cat = vm.Picker.Items.First(o => o.Entry.Key == "cat_face");
            vm.Picker.Choose(cat);
            Assert.Equal("builtin:cat_face", vm.Avatar);
            Assert.True(cat.IsSelected);
            vm.UseInitialOnly();
            Assert.Null(vm.Avatar);
            Assert.False(cat.IsSelected);
        });
    }
}



public class MigrationV3AndIconTests
{
    [Fact]
    public void V2Database_UpgradesToV3_KeepingRows_AndIconIsNullByDefault()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        using (var conn = t.Db.Open())
        {
            conn.Execute(Migrations.All[0].Sql);
            conn.Execute(Migrations.All[1].Sql);
            conn.Execute("INSERT INTO schema_version(version) VALUES (2)");
            conn.Execute(@"INSERT INTO profiles(name, color, avatar_initial, created_at, avatar)
                           VALUES ('P', '#EF4444', 'P', '2026-01-01T00:00:00Z', 'builtin:fox')");
            conn.Execute(@"INSERT INTO reminders(profile_id, title, due_date, created_at, updated_at)
                           VALUES (1, 'Old reminder', '2026-10-27', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')");
        }

        new DbInitializer(t.Db).Initialize();

        using var c = t.Db.Open();
        Assert.Equal(3, c.ExecuteScalar<int>("SELECT version FROM schema_version"));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM schema_version"));
        Assert.Contains("icon", c.Query<string>("SELECT name FROM pragma_table_info('reminders')").ToList());
        Assert.Equal("Old reminder", c.ExecuteScalar<string>("SELECT title FROM reminders"));
        Assert.Null(c.ExecuteScalar<string?>("SELECT icon FROM reminders"));
        Assert.Equal("builtin:fox", c.ExecuteScalar<string>("SELECT avatar FROM profiles"));
    }

    [Fact]
    public void Icon_RoundTrips_ThroughCreateAndUpdate_IncludingNull()
    {
        using var e = new ReminderEnv();
        var r = ReminderEnv.R("Math test", "2026-10-27", "09:00");
        Assert.Null(e.Repo.Create(r).Icon);

        r.Icon = "builtin:books";
        var saved = e.Repo.Create(r);
        Assert.Equal("builtin:books", e.Repo.Get(saved.Id)!.Icon);
        Assert.Equal("builtin:books", e.Repo.ListAll().Single(x => x.Id == saved.Id).Icon);
        Assert.Equal("builtin:books", e.Repo.ListByRange(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27)).First(x => x.Id == saved.Id).Icon);

        saved.Icon = "builtin:pencil";
        Assert.True(e.Repo.Update(saved));
        Assert.Equal("builtin:pencil", e.Repo.Get(saved.Id)!.Icon);

        saved.Icon = null; // "No picture"
        Assert.True(e.Repo.Update(saved));
        Assert.Null(e.Repo.Get(saved.Id)!.Icon);

        saved.Icon = "  ";
        e.Repo.Update(saved);
        Assert.Null(e.Repo.Get(saved.Id)!.Icon);
    }

    [Fact]
    public void UnknownIconKey_IsStored_ButLoadsAsNoPicture()
    {
        using var e = new ReminderEnv();
        var r = ReminderEnv.R("X", "2026-10-27");
        r.Icon = "builtin:does_not_exist";
        var saved = e.Repo.Create(r);
        Assert.Equal("builtin:does_not_exist", e.Repo.Get(saved.Id)!.Icon);
        Sta.Run(() => Assert.Null(AvatarCatalog.LoadBuiltin(saved.Icon, 32)));
    }
}

public class ReminderEditIconTests
{
    private static ReminderEditViewModel Vm(Reminder? existing = null) =>
        new(existing, new DateOnly(2026, 10, 27), new DateTime(2026, 10, 24, 10, 0, 0), new List<Category>(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void PickingBooks_AppliesToReminder_AndNoPictureClearsIt()
    {
        var vm = Vm();
        vm.Title = "Math test";
        Assert.False(vm.HasIcon);
        Assert.Equal("Add picture", vm.IconButtonName);

        var books = vm.Picker.Items.First(o => o.Entry.Key == "books");
        vm.Picker.Choose(books);
        Assert.Equal("builtin:books", vm.Icon);
        Assert.True(books.IsSelected);
        Assert.Contains("Books", vm.IconButtonName);

        var r = new Reminder();
        vm.ApplyTo(r);
        Assert.Equal("builtin:books", r.Icon);

        // reopen with the saved reminder: Books is still the selected picture
        var vm2 = Vm(r);
        Assert.Equal("builtin:books", vm2.Icon);
        Assert.True(vm2.Picker.Items.First(o => o.Entry.Key == "books").IsSelected);

        vm2.ClearIcon();
        vm2.ApplyTo(r);
        Assert.Null(r.Icon);
        Assert.DoesNotContain(vm2.Picker.Items, o => o.IsSelected);
    }

    [Fact]
    public void UnknownIconKey_ShowsAddGlyph_ButIsKeptOnSave()
    {
        var existing = new Reminder { Title = "x", DueDate = new DateOnly(2026, 10, 27), Icon = "builtin:does_not_exist" };
        var vm = Vm(existing);
        Assert.False(vm.HasIcon);
        Assert.Equal("Add picture", vm.IconButtonName);
        var r = new Reminder();
        vm.ApplyTo(r);
        Assert.Equal("builtin:does_not_exist", r.Icon);
    }

    [Fact]
    public void ReminderPicker_HasTheNineReminderCategoriesAndAtMost120Pictures()
    {
        var vm = Vm();
        Assert.Equal(new[] { "All", "School", "Work", "Health", "Home", "Food", "Money", "Travel", "Celebrations", "Sports" },
            vm.Picker.Chips.Select(c => c.Label).ToArray());
        Assert.True(vm.Picker.Items.Count <= 120);
    }
}


public class ReminderItemIconTests
{
    private sealed class NoActions : IReminderActions
    {
        public void CompletedChanged(ReminderItemViewModel item) { }
        public void ImportantChanged(ReminderItemViewModel item) { }
        public Task EditAsync(ReminderItemViewModel item) => Task.CompletedTask;
        public Task DeleteAsync(ReminderItemViewModel item) => Task.CompletedTask;
    }

    private static ReminderItemViewModel Item(string? icon) =>
        new(new Reminder { Title = "t", Icon = icon }, null, "All day", false, new NoActions());

    [Fact]
    public void KnownKey_HasIcon_UnknownOrNullKey_DoesNotAndNeverThrows()
    {
        Assert.True(Item("builtin:books").HasIcon);
        Assert.Equal("Books", Item("builtin:books").IconLabel);
        foreach (var bad in new string?[] { null, "", "builtin:nope", "garbage", "builtin:../x" })
        {
            var i = Item(bad);
            Assert.False(i.HasIcon);
            Assert.Equal("", i.IconLabel);
        }
    }

    [Fact]
    public void PictureConverter_ReturnsImageForKnownKey_AndNullOtherwise()
    {
        Sta.Run(() =>
        {
            var c = new Remindr.Converters.PictureConverter();
            Assert.NotNull(c.Convert("builtin:books", typeof(object), "64", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Null(c.Convert("builtin:nope", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Null(c.Convert(null, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        });
    }
}
