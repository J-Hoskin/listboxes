using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReactiveUI;
using Xunit;
using YourApp.Controls;

namespace YourApp.Tests;

/// <summary>
/// Tests the animation pipeline: change classification, snapshot collapsing,
/// transfer queue-jumping, and page-change pipeline clearing.
/// All tested through the public API — we observe VisiblePage before/after mutations.
/// </summary>
[Collection("Avalonia")]
public class AnimatedListBoxPipelineTests
{
    private static (TestAnimatedListBox lb, ObservableCollection<TestItem> source, TestCoordinator coord)
        MakeSetup(int itemCount = 8, int pageSize = 4)
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var coord = new TestCoordinator();
        var source = new ObservableCollection<TestItem>();
        for (int i = 0; i < itemCount; i++)
            source.Add(new TestItem($"Item{i}", i));

        var lb = new TestAnimatedListBox
        {
            PageSize = pageSize,
            Coordinator = coord,
            FullItems = source
        };
        return (lb, source, coord);
    }

    // -------------------------------------------------------------------------
    // Transfer classification and queue-jump
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Transfer_ClassifiedCorrectly_WhenCoordinatorHasPendingReason()
    {
        var (lb, source, coord) = MakeSetup();

        // Stamp before mutation — as the real flow does
        var item = (TestItem)lb.VisiblePage[0];
        coord.StampTransfer(item.Id, ExitReason.ToSibling, EntryReason.FromSibling);

        // Remove item simulating transfer
        source.Remove(item);
        await Dispatcher.UIThread.InvokeAsync(() => { });
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Item should be gone from visible page
        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == item.Id);
    }

    [AvaloniaFact]
    public async Task Transfer_JumpsQueue_SnapsCurrentAnimation()
    {
        var (lb, source, coord) = MakeSetup(itemCount: 4, pageSize: 4);

        // Trigger an add to start a pipeline batch
        source.Add(new TestItem("Extra", 99));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Now stamp and trigger a transfer while pipeline may be busy
        var item = (TestItem)lb.VisiblePage[0];
        coord.StampTransfer(item.Id, ExitReason.ToSibling, EntryReason.FromSibling);
        source.Remove(item);

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Transfer item must be gone — it jumped the queue
        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == item.Id);
    }

    [AvaloniaFact]
    public async Task RapidAddRemoves_CollapseToFinalState()
    {
        var (lb, source, _) = MakeSetup(itemCount: 4, pageSize: 4);

        var extra1 = new TestItem("Extra1", 10);
        var extra2 = new TestItem("Extra2", 11);
        var extra3 = new TestItem("Extra3", 12);

        // Rapid mutations — should collapse
        source.Add(extra1);
        source.Add(extra2);
        source.Remove(extra1);
        source.Add(extra3);

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Final state: extra1 removed, extra2 and extra3 added
        // But they're on page 2 — page 1 should still be original 4 items
        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal(2, lb.PageCount);
    }

    // -------------------------------------------------------------------------
    // Page navigation clears pipeline
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PageChange_ClearsPipeline_ShowsNewPageImmediately()
    {
        var (lb, source, _) = MakeSetup(itemCount: 8, pageSize: 4);

        // Trigger some mutations to fill the pipeline
        source.Add(new TestItem("Extra", 99));

        // Navigate — should override pipeline immediately
        lb.CurrentPage = 1;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Page 2 items should be visible
        Assert.Equal("Item4", ((TestItem)lb.VisiblePage[0]).Name);
    }

    [AvaloniaFact]
    public async Task PageChange_ToSamePage_DoesNothing()
    {
        var (lb, _, _) = MakeSetup(itemCount: 8, pageSize: 4);
        var originalFirst = lb.VisiblePage[0];

        lb.CurrentPage = 0;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Same(originalFirst, lb.VisiblePage[0]);
    }

    // -------------------------------------------------------------------------
    // Coordinator — null coordinator degrades gracefully
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NullCoordinator_ItemsStillAddAndRemove()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem> { new("A", 0), new("B", 1) };
        var lb = new TestAnimatedListBox
        {
            PageSize = 4,
            Coordinator = null, // no coordinator
            FullItems = source
        };

        source.Add(new TestItem("C", 2));
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(3, lb.VisiblePage.Count);
    }

    // -------------------------------------------------------------------------
    // SnapAllAnimations
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SnapAllAnimations_RemovesGhosts_Immediately()
    {
        var (lb, source, _) = MakeSetup(itemCount: 4, pageSize: 4);

        // Remove an item — it becomes a ghost during exit animation
        source.RemoveAt(0);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Before snap: ghost may still be in visible page (exit animating)
        // After snap: ghost must be gone
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(3, lb.VisiblePage.Count);
    }

    [AvaloniaFact]
    public async Task SnapAllAnimations_AfterPageChange_LeavesNewPageIntact()
    {
        var (lb, source, _) = MakeSetup(itemCount: 8, pageSize: 4);
        lb.CurrentPage = 1;

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Page 2 items should still be visible
        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal("Item4", ((TestItem)lb.VisiblePage[0]).Name);
    }
}
