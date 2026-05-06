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
/// Tests ghost item behaviour — items mid-exit that should not interfere with
/// newly arriving items, and the re-entry race guard.
/// </summary>
[Collection("Avalonia")]
public class GhostItemTests
{
    private static (TestAnimatedListBox lb, ObservableCollection<TestItem> source)
        MakeSetup(int itemCount = 4, int pageSize = 4)
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>();
        for (int i = 0; i < itemCount; i++)
            source.Add(new TestItem($"Item{i}", i));

        var lb = new TestAnimatedListBox
        {
            PageSize = pageSize,
            FullItems = source
        };
        return (lb, source);
    }

    // -------------------------------------------------------------------------
    // New item inserts at correct position despite ghosts
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NewItem_InsertsAtCorrectPosition_WhenGhostPresent()
    {
        var (lb, source) = MakeSetup(itemCount: 3, pageSize: 4);

        // Remove item 0 — becomes a ghost
        var removedItem = source[0];
        source.RemoveAt(0);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Add a new item before snapping — ghost still present
        var newItem = new TestItem("New", 99);
        source.Add(newItem);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Snap to clear ghost
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Ghost should be gone, new item should be present
        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == removedItem.Id);
        Assert.Contains(lb.VisiblePage, x => x is TestItem t && t.Id == newItem.Id);
    }

    // -------------------------------------------------------------------------
    // Re-entry race — item removed then re-added before exit completes
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ReEntry_ItemRemovedThenReAdded_AppearsCorrectly()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);
        var item = source[0];

        // Remove — item becomes ghost
        source.RemoveAt(0);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Re-add same item before exit animation completes
        source.Insert(0, item);
        lb.SnapAllAnimations(); // forces exit to complete
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Item should be present exactly once
        var count = lb.VisiblePage.Count(x => x is TestItem t && t.Id == item.Id);
        Assert.Equal(1, count);
    }

    [AvaloniaFact]
    public async Task ReEntry_ItemNotDuplicated_AfterSnapAndReAdd()
    {
        var (lb, source) = MakeSetup(itemCount: 2, pageSize: 4);
        var item = source[0];

        source.Remove(item);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        source.Add(item);
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        var count = lb.VisiblePage.Count(x => x is TestItem t && t.Id == item.Id);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // Multiple items removed simultaneously
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task BulkRemove_AllItemsEventuallyLeaveVisiblePage()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);

        source.Clear();
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Empty(lb.VisiblePage);
    }

    [AvaloniaFact]
    public async Task BulkAdd_AllItemsAppearOnCorrectPages()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>();
        var lb = new TestAnimatedListBox { PageSize = 4, FullItems = source };

        for (int i = 0; i < 8; i++)
            source.Add(new TestItem($"Item{i}", i));

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal(2, lb.PageCount);
    }

    // -------------------------------------------------------------------------
    // Coordinator null does not crash on ghost cleanup
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NullCoordinator_GhostCleanup_DoesNotThrow()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("A", 0), new("B", 1), new("C", 2), new("D", 3)
        };
        var lb = new TestAnimatedListBox
        {
            PageSize = 4,
            Coordinator = null,
            FullItems = source
        };

        source.RemoveAt(0);
        var ex = await Record.ExceptionAsync(async () =>
        {
            lb.SnapAllAnimations();
            await Dispatcher.UIThread.InvokeAsync(() => { });
        });

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // FullItems swap while ghost present
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task FullItemsSwap_WhileGhostPresent_ClearsGhost()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);
        var item = source[0];

        source.RemoveAt(0);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Swap source while item is ghost
        var newSource = new ObservableCollection<TestItem> { new("Fresh", 0) };
        lb.FullItems = newSource;

        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Ghost from old source must be gone
        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == item.Id);
        Assert.Equal(1, lb.VisiblePage.Count);
    }
}
