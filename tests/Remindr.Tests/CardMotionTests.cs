using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Tests;

/// <summary>Remind UI 0.4 slice 7: the order of "animate, then change the data" for complete and delete, and the glow target.</summary>
[Collection("AnimGate")] // Anim.Lookup is process-wide state
public class CardMotionTests
{
    private sealed class Rig : IDisposable
    {
        public ReminderEnv Env { get; } = new();
        public List<string> Log { get; } = new();
        public bool Gate { get; set; } = true;
        public bool Confirm { get; set; } = true;
        public int Refreshes { get; private set; }
        public ReminderActionHandler Handler { get; }
        public ReminderItemViewModel Item { get; }
        public Reminder Reminder { get; }

        public Rig(bool leaves = true)
        {
            Reminder = Env.Repo.Create(ReminderEnv.R("Dentist", "2026-10-27", "15:00"));
            Handler = new ReminderActionHandler(Env.Repo, _ => Task.FromResult(Confirm), r => Task.FromResult<Reminder?>(r),
                () => { Refreshes++; Log.Add("refresh"); }, leavesOnComplete: leaves)
            {
                EffectsEnabled = () => Gate,
                Delay = t => { Log.Add("wait " + (int)t.TotalMilliseconds); return Task.CompletedTask; },
                ExitDelay = TimeSpan.FromMilliseconds(180)
            };
            Item = new ReminderItemViewModel(Reminder, null, "3:00 PM", false, Handler);
        }

        public void Dispose() => Env.Dispose();
    }

    [Fact]
    public async Task Delete_WaitsForTheSlideThenDeletes_Once()
    {
        using var r = new Rig();
        bool? exitingDuringWait = null;
        r.Handler.Delay = t => { exitingDuringWait = r.Item.IsExiting; r.Log.Add("wait"); return Task.CompletedTask; };

        await r.Handler.DeleteAsync(r.Item);

        Assert.True(exitingDuringWait);                  // the card was told to slide out before the wait
        Assert.Null(r.Env.Repo.Get(r.Reminder.Id));      // and the row is gone afterwards
        Assert.Equal(0, r.Refreshes);                    // the repo's own change event rebuilds the list, no extra refresh
    }

    [Fact]
    public async Task Delete_GateOff_DeletesAtOnceWithoutWaiting()
    {
        using var r = new Rig { Gate = false };
        await r.Handler.DeleteAsync(r.Item);
        Assert.Empty(r.Log);
        Assert.False(r.Item.IsExiting);
        Assert.Null(r.Env.Repo.Get(r.Reminder.Id));
    }

    [Fact]
    public async Task Delete_Cancelled_NeverSlidesOrDeletes()
    {
        using var r = new Rig { Confirm = false };
        await r.Handler.DeleteAsync(r.Item);
        Assert.Empty(r.Log);
        Assert.False(r.Item.IsExiting);
        Assert.NotNull(r.Env.Repo.Get(r.Reminder.Id));
    }

    [Fact]
    public async Task Delete_SecondClickWhileSliding_IsIgnored()
    {
        using var r = new Rig();
        var release = new TaskCompletionSource();
        var asked = 0;
        var handler = new ReminderActionHandler(r.Env.Repo, _ => { asked++; return Task.FromResult(true); },
            x => Task.FromResult<Reminder?>(x), () => { }, leavesOnComplete: true)
        {
            EffectsEnabled = () => true,
            Delay = _ => release.Task,
            ExitDelay = TimeSpan.FromMilliseconds(180)
        };
        var item = new ReminderItemViewModel(r.Reminder, null, "", false, handler);

        var first = handler.DeleteAsync(item);
        await handler.DeleteAsync(item);   // returns at once: the first delete is still sliding
        Assert.Equal(1, asked);
        Assert.True(item.IsExiting);
        Assert.NotNull(r.Env.Repo.Get(r.Reminder.Id)); // not deleted before the slide ends

        release.SetResult();
        await first;
        Assert.Null(r.Env.Repo.Get(r.Reminder.Id));
    }

    [Fact]
    public async Task Delete_RowAlreadyGone_RestoresTheCardAndRefreshes()
    {
        using var r = new Rig();
        r.Env.Repo.Delete(r.Reminder.Id);   // deleted elsewhere while the slide ran
        await r.Handler.DeleteAsync(r.Item);
        Assert.False(r.Item.IsExiting);
        Assert.Equal(1, r.Refreshes);
    }

    [Fact]
    public async Task Complete_FadesJustBeforeTheRefresh_AndKeepsTheSettleTotal()
    {
        using var r = new Rig();
        var fadingWhenRefreshed = false;
        ReminderItemViewModel? item = null;
        var refreshed = new TaskCompletionSource();
        var handler = new ReminderActionHandler(r.Env.Repo, _ => Task.FromResult(true), x => Task.FromResult<Reminder?>(x),
            () => { fadingWhenRefreshed = item!.IsSettling; refreshed.TrySetResult(); }, leavesOnComplete: true)
        {
            EffectsEnabled = () => true,
            Delay = t => { r.Log.Add("wait " + (int)t.TotalMilliseconds + (item!.IsSettling ? " fading" : "")); return Task.CompletedTask; },
            ExitDelay = TimeSpan.FromMilliseconds(180)
        };
        item = new ReminderItemViewModel(r.Reminder, null, "", false, handler);

        item.IsCompleted = true;
        await refreshed.Task;

        Assert.True(r.Env.Repo.Get(r.Reminder.Id)!.IsCompleted);   // the data change is immediate
        Assert.True(fadingWhenRefreshed);
        Assert.Equal(new[] { "wait 270", "wait 180" }, r.Log.Select(l => l.Replace(" fading", "")).ToArray());
    }

    [Fact]
    public async Task Complete_GateOff_RefreshesAtOnceWithNoFade()
    {
        using var r = new Rig { Gate = false };
        r.Item.IsCompleted = true;
        await Task.Delay(50);
        Assert.Equal(new[] { "refresh" }, r.Log);
        Assert.False(r.Item.IsSettling);
    }

    [Fact]
    public async Task Complete_InTheCalendar_DoesNotFadeTheCard()
    {
        using var r = new Rig(leaves: false);
        r.Item.IsCompleted = true;
        await Task.Delay(50);
        Assert.False(r.Item.IsSettling);
        Assert.Equal(new[] { "wait 450", "refresh" }, r.Log);
    }

    [Fact]
    public async Task Complete_ThenUntickInTime_CancelsTheFade()
    {
        using var r = new Rig();
        r.Handler.Delay = _ => Task.CompletedTask;
        r.Item.IsCompleted = true;
        await Task.Delay(50);
        Assert.True(r.Item.IsSettling);
        r.Handler.Delay = _ => new TaskCompletionSource().Task;   // the new settle is still waiting
        r.Item.IsCompleted = false;      // untick: the fade is cancelled at once and the card is restored
        Assert.False(r.Item.IsSettling);
        Assert.False(r.Env.Repo.Get(r.Reminder.Id)!.IsCompleted);
    }

    [Fact]
    public async Task Complete_TickThenUntick_SecondSettleDoesNotFadeTheCardThatStays()
    {
        using var r = new Rig();
        r.Item.IsCompleted = true;
        r.Item.IsCompleted = false;   // back where the list placed it: it stays in its group
        await Task.Delay(50);
        Assert.False(r.Item.IsSettling);
        Assert.Equal(new[] { "wait 450", "refresh" }, r.Log.Skip(r.Log.Count - 2).ToArray());
    }

    // ---- glow target ----------------------------------------------------------------------------------------

    [Fact]
    public void Highlight_PicksTheSavedReminder_AndNothingOnCancel()
    {
        Assert.Null(ReminderHighlight.TargetId(null));
        Assert.Null(ReminderHighlight.TargetId(new Reminder { Id = 0 }));
        Assert.Equal(7, ReminderHighlight.TargetId(new Reminder { Id = 7 }));

        using var r = new Rig();
        var other = new ReminderItemViewModel(new Reminder { Id = r.Reminder.Id + 1, Title = "x" }, null, "", false, r.Handler);
        var n = ReminderHighlight.Apply(new[] { other, r.Item }, r.Reminder);
        Assert.Equal(1, n);
        Assert.Equal(1, r.Item.GlowToken);
        Assert.Equal(0, other.GlowToken);
        Assert.Equal(0, ReminderHighlight.Apply(new[] { other, r.Item }, null));
        Assert.Equal(1, r.Item.GlowToken);
    }

    [Fact]
    public void Card_StrikeLine_IsUndrawnAtRest()
    {
        // StrikeWhen only reacts to a change; an open card binds the default false, so the XAML itself must start the line at ScaleX 0.
        var doc = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(SourceTree.AppDir(), "Views", "Controls", "ReminderCard.xaml"));
        var line = doc.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "Anim.StrikeWhen"));
        var scale = line.Descendants().Single(e => e.Name.LocalName == "ScaleTransform");
        Assert.Equal("0", (string?)scale.Attribute("ScaleX"));
    }

    // ---- right-to-left (laid out, as for SlideSwap) ------------------------------------------------------------

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public void Rtl_DeleteSlidesToTheTrailingEdge_AndStrikeGrowsFromTheLeadingEdge(FlowDirection flow) => Sta.Run(() =>
    {
        var root = new Grid { Width = 300, Height = 100 };
        var host = new Grid { FlowDirection = flow };
        root.Children.Add(host);
        var card = new Border { Width = 100, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(card);
        var titleGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        var title = new TextBlock { Text = "Dentist", FontSize = 15 };
        var line = new System.Windows.Shapes.Rectangle { Height = 1.5, RenderTransformOrigin = new Point(0, 0.5) };
        titleGrid.Children.Add(title);
        titleGrid.Children.Add(line);
        host.Children.Add(titleGrid);
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));

        double Left(FrameworkElement e) => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)).Left;
        var rest = Left(card);
        Anim.SetExitWhen(card, true);   // not loaded: end state written directly
        root.UpdateLayout();
        var trailing = flow == FlowDirection.LeftToRight ? 24 : -24;
        Assert.Equal(rest + trailing, Left(card), 3);

        var full = titleGrid.TransformToAncestor(root).TransformBounds(new Rect(0, 0, titleGrid.ActualWidth, titleGrid.ActualHeight));
        line.RenderTransform = new ScaleTransform(0.5, 1);   // half drawn
        root.UpdateLayout();
        var half = line.TransformToAncestor(root).TransformBounds(new Rect(0, 0, line.ActualWidth, line.ActualHeight));
        if (flow == FlowDirection.LeftToRight) Assert.Equal(full.Left, half.Left, 3);
        else Assert.Equal(full.Right, half.Right, 3);
        Assert.Equal(full.Width / 2, half.Width, 3);
    });

    // ---- gate off writes end states ---------------------------------------------------------------------------

    [Fact]
    public void Behaviours_NotLoaded_WriteEndStatesDirectly() => Sta.Run(() =>
    {
        var card = new Border();
        Anim.SetExitWhen(card, true);
        Assert.Equal(0.0, card.Opacity);
        var move = Assert.IsType<TranslateTransform>(card.RenderTransform);
        Assert.Equal(24.0, move.X);
        Anim.SetExitWhen(card, false);
        Assert.Equal(1.0, card.Opacity);
        Assert.True(ReferenceEquals(card.RenderTransform, Transform.Identity));

        Anim.SetSettleWhen(card, true);
        Assert.Equal(0.0, card.Opacity);
        Assert.Equal(8.0, ((TranslateTransform)card.RenderTransform).Y);
        Assert.Equal(Visibility.Collapsed, card.Visibility);   // a faded card never keeps its space while a later refresh waits
        Anim.SetSettleWhen(card, false);
        Assert.Equal(1.0, card.Opacity);
        Assert.Equal(Visibility.Visible, card.Visibility);
        Anim.SetExitWhen(card, true);
        Assert.Equal(Visibility.Visible, card.Visibility);     // delete removes the row itself; the slide only fades
        Anim.SetExitWhen(card, false);

        var icon = new Border { Opacity = 0 };
        Anim.SetShowWhen(icon, true);
        Assert.Equal(1.0, icon.Opacity);
        Anim.SetShowWhen(icon, false);
        Assert.Equal(0.0, icon.Opacity);
        var outline = new Border();
        Anim.SetHideWhen(outline, true);
        Assert.Equal(0.0, outline.Opacity);

        var grid = new Grid();
        var title = new TextBlock { Text = "Dentist" };
        var line = new System.Windows.Shapes.Rectangle();
        grid.Children.Add(title);
        grid.Children.Add(line);
        Anim.SetStrikeWhen(line, true);
        Assert.Equal(1.0, ((ScaleTransform)line.RenderTransform).ScaleX);
        Anim.SetStrikeWhen(line, false);
        Assert.Equal(0.0, ((ScaleTransform)line.RenderTransform).ScaleX);
    });
}
