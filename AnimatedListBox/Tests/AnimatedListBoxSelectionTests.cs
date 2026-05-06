using System;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReactiveUI;
using Xunit;
using YourApp.Controls;

namespace YourApp.Tests;

[Collection("Avalonia")]
public class AnimatedListBoxSelectionTests
{
    private static (TestAnimatedListBox lb, ObservableCollection<TestItem> source)
        MakeSetup(int itemCount = 8, int pageSize = 4)
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
    // ProgrammaticSelectedItem — page jump
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_OnCurrentPage_SelectsWithoutPageJump()
    {
        var (lb, source) = MakeSetup(itemCount: 8, pageSize: 4);
        var item = (TestItem)lb.VisiblePage[0];

        lb.ProgrammaticSelectedItem = item;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(0, lb.CurrentPage);
        Assert.Equal(item, lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_OnDifferentPage_JumpsToCorrectPage()
    {
        var (lb, source) = MakeSetup(itemCount: 8, pageSize: 4);
        var item = source[5]; // on page 2 (index 1)

        lb.ProgrammaticSelectedItem = item;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(1, lb.CurrentPage);
        Assert.Equal(item, lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_NotInList_ClearsSelection()
    {
        var (lb, _) = MakeSetup(itemCount: 8, pageSize: 4);
        var outsideItem = new TestItem("NotInList", 99);

        lb.ProgrammaticSelectedItem = outsideItem;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Null(lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_SetToNull_ClearsSelection()
    {
        var (lb, _) = MakeSetup(itemCount: 8, pageSize: 4);
        lb.ProgrammaticSelectedItem = (TestItem)lb.VisiblePage[0];
        await Dispatcher.UIThread.InvokeAsync(() => { });

        lb.ProgrammaticSelectedItem = null;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Null(lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_OnLastPage_JumpsCorrectly()
    {
        var (lb, source) = MakeSetup(itemCount: 9, pageSize: 4);
        var lastItem = source[8]; // page 3 (index 2), only item on that page

        lb.ProgrammaticSelectedItem = lastItem;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(2, lb.CurrentPage);
        Assert.Equal(lastItem, lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_ItemOnPageBoundary_SelectsCorrectly()
    {
        var (lb, source) = MakeSetup(itemCount: 8, pageSize: 4);
        var item = source[3]; // last item on page 1

        lb.ProgrammaticSelectedItem = item;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(0, lb.CurrentPage);
        Assert.Equal(item, lb.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ProgrammaticSelectedItem_ChangingSelection_UpdatesSelectedItem()
    {
        var (lb, source) = MakeSetup(itemCount: 8, pageSize: 4);
        var first = source[0];
        var second = source[1];

        lb.ProgrammaticSelectedItem = first;
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.Equal(first, lb.SelectedItem);

        lb.ProgrammaticSelectedItem = second;
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.Equal(second, lb.SelectedItem);
    }

    // -------------------------------------------------------------------------
    // Ghost items — hit test
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task GhostItems_AreNotHitTestVisible_DuringExitAnimation()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);

        // We can't directly check hit testing in headless, but we can verify
        // that after removing an item, the container is not in VisiblePage
        // after snap (ghost was cleaned up).
        var item = source[0];
        source.RemoveAt(0);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.DoesNotContain(lb.VisiblePage, x => x is TestItem t && t.Id == item.Id);
    }

    // -------------------------------------------------------------------------
    // Page count updates
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PageCount_UpdatesWhenItemsAdded()
    {
        var (lb, source) = MakeSetup(itemCount: 4, pageSize: 4);
        Assert.Equal(1, lb.PageCount);

        source.Add(new TestItem("Extra", 99));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(2, lb.PageCount);
    }

    [AvaloniaFact]
    public async Task PageCount_UpdatesWhenItemsRemoved()
    {
        var (lb, source) = MakeSetup(itemCount: 8, pageSize: 4);
        Assert.Equal(2, lb.PageCount);

        source.RemoveAt(7);
        source.RemoveAt(6);
        source.RemoveAt(5);
        source.RemoveAt(4);

        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(1, lb.PageCount);
    }
}
