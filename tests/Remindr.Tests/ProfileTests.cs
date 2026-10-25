using Dapper;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

public class PinHasherTests
{
    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var (h, s) = PinHasher.Hash("1234");
        Assert.True(PinHasher.Verify("1234", h, s));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var (h, s) = PinHasher.Hash("1234");
        Assert.False(PinHasher.Verify("1235", h, s));
        Assert.False(PinHasher.Verify("", h, s));
        Assert.False(PinHasher.Verify(null, h, s));
    }

    [Fact]
    public void Hash_SamePinTwice_UsesDifferentSalts()
    {
        var a = PinHasher.Hash("1234");
        var b = PinHasher.Hash("1234");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.Equal(16, Convert.FromBase64String(a.Salt).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    public void Hash_NonFourDigitPin_Throws(string pin)
    {
        Assert.Throws<ArgumentException>(() => PinHasher.Hash(pin));
        Assert.False(PinHasher.IsValidPin(pin));
    }
}

public class AvatarContrastTests
{
    [Theory]
    [InlineData("#3B82F6", true)]  // blue
    [InlineData("#8B5CF6", true)]  // violet
    [InlineData("#EF4444", true)]  // red
    [InlineData("#F59E0B", false)] // amber
    [InlineData("#10B981", false)] // emerald
    [InlineData("#FFFFFF", false)]
    public void ForegroundFor_PicksReadableTextColour(string hex, bool expectWhite)
    {
        var fg = Remindr.Converters.ColorUtil.ForegroundFor(hex).Color;
        Assert.Equal(expectWhite, fg == System.Windows.Media.Colors.White);
    }
}

public class ProfileRepoTests
{
    private static (TempDb t, ProfileRepo repo) Setup()
    {
        var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        return (t, new ProfileRepo(t.Db));
    }

    [Fact]
    public void Create_SeedsThreeDefaultCategories()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var p = repo.Create("Alex", "#EF4444", "A");

        var cats = new CategoryRepo(t.Db).ListByProfile(p.Id);
        Assert.Equal(new[] { "School", "Work", "Personal" }, cats.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { "#3B82F6", "#F59E0B", "#10B981" }, cats.Select(c => c.Color).ToArray());
    }

    [Fact]
    public void Delete_CascadesCategories_AndLeavesOtherProfiles()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var a = repo.Create("A", "#EF4444", "A");
        var b = repo.Create("B", "#10B981", "B");

        repo.Delete(a.Id);

        using var conn = t.Db.Open();
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM categories WHERE profile_id=@id", new { id = a.Id }));
        Assert.Equal(3, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM categories WHERE profile_id=@id", new { id = b.Id }));
        Assert.Null(repo.Get(a.Id));
    }

    [Fact]
    public void Delete_LastRemainingProfile_IsRefused()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var only = repo.Create("Solo", "#EF4444", "S");

        Assert.False(repo.Delete(only.Id));
        Assert.NotNull(repo.Get(only.Id));
        Assert.Equal(1, repo.Count());
    }

    [Fact]
    public void SetPin_StoresHashNotPlaintext_AndClearRemovesIt()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var p = repo.Create("A", "#EF4444", "A");
        repo.SetPin(p.Id, "4821");

        using (var conn = t.Db.Open())
        {
            var row = conn.QuerySingle<(string? h, string? s)>("SELECT pin_hash, pin_salt FROM profiles WHERE id=@id", new { id = p.Id });
            Assert.False(string.IsNullOrEmpty(row.h));
            Assert.False(string.IsNullOrEmpty(row.s));
            Assert.DoesNotContain("4821", row.h!);
            Assert.DoesNotContain("4821", row.s!);
        }

        var loaded = repo.Get(p.Id)!;
        Assert.True(loaded.HasPin);
        Assert.True(PinHasher.Verify("4821", loaded.PinHash, loaded.PinSalt));
        Assert.False(PinHasher.Verify("0000", loaded.PinHash, loaded.PinSalt));

        repo.ClearPin(p.Id);
        Assert.False(repo.Get(p.Id)!.HasPin);
    }

    [Fact]
    public void Update_ChangesNameColorInitial()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var p = repo.Create("A", "#EF4444", "A");
        repo.Update(p.Id, "Zed", "#14B8A6", "Z");
        var u = repo.Get(p.Id)!;
        Assert.Equal(("Zed", "#14B8A6", "Z"), (u.Name, u.Color, u.AvatarInitial));
    }

    [Fact]
    public void EnsureDefaultProfile_FirstRunCreatesMeWithCategories_ThenIsNoOp()
    {
        var (t, repo) = Setup();
        using var _ = t;

        Assert.True(repo.EnsureDefaultProfile());
        Assert.False(repo.EnsureDefaultProfile());

        var all = repo.List();
        Assert.Single(all);
        Assert.Equal("Me", all[0].Name);
        Assert.Equal(3, new CategoryRepo(t.Db).ListByProfile(all[0].Id).Count);
    }

    [Fact]
    public void ProfileSession_SwitchTo_SavesLastProfileAndRaisesEvent()
    {
        var (t, repo) = Setup();
        using var _ = t;
        var settings = new SettingsRepo(t.Db);
        var session = new ProfileSession(repo, settings);
        var p = repo.Create("A", "#EF4444", "A");
        var raised = 0;
        session.CurrentProfileChanged += (_, _) => raised++;

        session.SwitchTo(p);

        Assert.Equal(p.Id, session.Current!.Id);
        Assert.Equal(p.Id, session.LastProfileId);
        Assert.Equal(p.Id.ToString(), settings.Get("last_profile_id"));
        Assert.Equal(1, raised);
    }
}
