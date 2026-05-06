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

[Collection("Avalonia")]
public class OrderedAnimatedListBoxTests
{
    private static (TestOrderedAnimatedListBox lb, ObservableCollection<TestItem> source)
        MakeSetup(int itemCount = 4, int pageSize = 4)
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>();
        for (int i = 0; i < itemCount; i++)
            source.Add(new TestItem($"Item{i}", i));

        var lb = new TestOrderedAnimatedListBox
        {
            PageSize = pageSize,
            FullItems = source
        };
        return (lb, source);
    }

    // -------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public void Items_DisplayedInOrderProperty_NotInsertionOrder()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("C", order: 2),
            new("A", order: 0),
            new("B", order: 1),
        };
        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };

        Assert.Equal("A", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("B", ((TestItem)lb.VisiblePage[1]).Name);
        Assert.Equal("C", ((TestItem)lb.VisiblePage[2]).Name);
    }

    [AvaloniaFact]
    public async Task OrderChange_ReordersVisibleItems()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);

        // Swap first and last items' order
        var first = source.First(x => x.Order == 0);
        var last = source.First(x => x.Order == 3);

        first.Order = 3;
        last.Order = 0;

        // Trigger collection change notification
        var idx1 = source.IndexOf(first);
        source.Move(idx1, idx1); // force notification

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(last, lb.VisiblePage[0]);
        Assert.Equal(first, lb.VisiblePage[3]);
    }

    [AvaloniaFact]
    public void InitialOrder_UsesOrderProperty_NotCollectionOrder()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("Second", order: 1),
            new("First", order: 0),
        };
        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };

        Assert.Equal("First", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("Second", ((TestItem)lb.VisiblePage[1]).Name);
    }

    // -------------------------------------------------------------------------
    // Page-edge entry directions (tested via presence on correct page)
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ItemWithHighOrder_AppearsOnNextPage_NotCurrentPage()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);

        var newItem = new TestItem("LateArrival", order: 99);
        source.Add(newItem);
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Item with order 99 should be on page 2, not page 1
        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == newItem.Id);
        Assert.Equal(2, lb.PageCount);
    }

    [AvaloniaFact]
    public async Task ItemWithLowOrder_AppearsOnCurrentPage_WhenPageHasRoom()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("B", order: 1),
            new("C", order: 2),
            new("D", order: 3),
        };
        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };

        var newItem = new TestItem("A", order: 0);
        source.Add(newItem);
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Item with order 0 should be first on page 1
        Assert.Equal(newItem, lb.VisiblePage[0]);
    }

    // -------------------------------------------------------------------------
    // FLIP generation guard — rapid order changes don't double-animate
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task RapidOrderChanges_CollapseToFinalOrder()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("A", order: 0),
            new("B", order: 1),
            new("C", order: 2),
            new("D", order: 3),
        };
        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };

        var a = source.First(x => x.Name == "A");
        var d = source.First(x => x.Name == "D");

        // Rapid swap and re-swap
        a.Order = 3; d.Order = 0;
        source.Move(0, 0); // force update

        a.Order = 0; d.Order = 3;
        source.Move(0, 0); // force another update

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Final state: back to original order
        Assert.Equal("A", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("D", ((TestItem)lb.VisiblePage[3]).Name);
    }

    // -------------------------------------------------------------------------
    // Pagination with ordering
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public void OrderedPagination_SecondPage_ShowsCorrectOrderedItems()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>();
        for (int i = 7; i >= 0; i--) // insert in reverse order
            source.Add(new TestItem($"Item{i}", i));

        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };
        lb.CurrentPage = 1;

        // Page 2 should show items 4-7 in order regardless of insertion order
        Assert.Equal("Item4", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("Item7", ((TestItem)lb.VisiblePage[3]).Name);
    }

    [AvaloniaFact]
    public async Task ItemMovesFromPage2ToPage1_AppearsOnPage1()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var source = new ObservableCollection<TestItem>
        {
            new("A", 0), new("B", 1), new("C", 2), new("D", 3),
            new("E", 4)
        };
        var lb = new TestOrderedAnimatedListBox { PageSize = 4, FullItems = source };

        // Move E (on page 2) to order -1 (should now be first on page 1)
        var e = source.First(x => x.Name == "E");
        e.Order = -1;
        source.Move(4, 4); // force update

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(e, lb.VisiblePage[0]);
    }
}
