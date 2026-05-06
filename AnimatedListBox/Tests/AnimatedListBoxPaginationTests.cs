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
public class AnimatedListBoxPaginationTests
{
    private static ObservableCollection<TestItem> MakeSource(int count)
    {
        var items = new ObservableCollection<TestItem>();
        for (int i = 0; i < count; i++)
            items.Add(new TestItem($"Item{i}", i));
        return items;
    }

    private static TestAnimatedListBox MakeListBox(int pageSize = 4)
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
        var lb = new TestAnimatedListBox { PageSize = pageSize };
        return lb;
    }

    // -------------------------------------------------------------------------
    // Initial population
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public void InitialPopulation_ShowsFirstPage()
    {
        var source = MakeSource(8);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal("Item0", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("Item3", ((TestItem)lb.VisiblePage[3]).Name);
    }

    [AvaloniaFact]
    public void InitialPopulation_SetsPageCountCorrectly()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(9);

        Assert.Equal(3, lb.PageCount);
    }

    [AvaloniaFact]
    public void InitialPopulation_FewerItemsThanPageSize_ShowsAll()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(2);

        Assert.Equal(2, lb.VisiblePage.Count);
    }

    [AvaloniaFact]
    public void InitialPopulation_EmptySource_ShowsNothing()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(0);

        Assert.Empty(lb.VisiblePage);
    }

    [AvaloniaFact]
    public void InitialPopulation_ExactPageSize_ShowsAll()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(4);

        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal(1, lb.PageCount);
    }

    // -------------------------------------------------------------------------
    // Page navigation
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public void NextPage_ShowsCorrectItems()
    {
        var source = MakeSource(8);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        lb.CurrentPage = 1;

        Assert.Equal("Item4", ((TestItem)lb.VisiblePage[0]).Name);
        Assert.Equal("Item7", ((TestItem)lb.VisiblePage[3]).Name);
    }

    [AvaloniaFact]
    public void PreviousPage_ShowsCorrectItems()
    {
        var source = MakeSource(8);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;
        lb.CurrentPage = 1;

        lb.CurrentPage = 0;

        Assert.Equal("Item0", ((TestItem)lb.VisiblePage[0]).Name);
    }

    [AvaloniaFact]
    public void NextPageCommand_IncrementsPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);

        lb.NextPageCommand.Execute(null);

        Assert.Equal(1, lb.CurrentPage);
    }

    [AvaloniaFact]
    public void PreviousPageCommand_DecrementsPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);
        lb.CurrentPage = 1;

        lb.PreviousPageCommand.Execute(null);

        Assert.Equal(0, lb.CurrentPage);
    }

    [AvaloniaFact]
    public void NextPageCommand_CannotExecute_OnLastPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);
        lb.CurrentPage = 1;

        Assert.False(lb.NextPageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void PreviousPageCommand_CannotExecute_OnFirstPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);

        Assert.False(lb.PreviousPageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void NextPageCommand_CanExecute_WhenNotOnLastPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);

        Assert.True(lb.NextPageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CurrentPage_ClampsToValidRange_WhenSourceShrinks()
    {
        var source = MakeSource(8);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;
        lb.CurrentPage = 1;

        // Remove page 2 items — only 4 items remain, only 1 page
        source.Remove(source.Last());
        source.Remove(source.Last());
        source.Remove(source.Last());
        source.Remove(source.Last());

        Assert.Equal(0, lb.CurrentPage);
    }

    [AvaloniaFact]
    public void LastPage_ShowsRemainingItems_WhenNotFullPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(6);
        lb.CurrentPage = 1;

        Assert.Equal(2, lb.VisiblePage.Count);
    }

    // -------------------------------------------------------------------------
    // Source mutations — visible items update
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AddItem_AppearsOnCurrentPage_WhenPageNotFull()
    {
        var source = MakeSource(2);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        source.Add(new TestItem("NewItem", 2));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(3, lb.VisiblePage.Count);
    }

    [AvaloniaFact]
    public async Task AddItem_DoesNotAppearOnCurrentPage_WhenPageFull()
    {
        var source = MakeSource(4);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        source.Add(new TestItem("NewItem", 4));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(4, lb.VisiblePage.Count);
        Assert.Equal(2, lb.PageCount);
    }

    [AvaloniaFact]
    public async Task RemoveItem_DisappearsFromCurrentPage()
    {
        var source = MakeSource(4);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        source.RemoveAt(0);
        lb.SnapAllAnimations(); // force exit animation to complete
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(3, lb.VisiblePage.Count);
    }

    [AvaloniaFact]
    public async Task RemoveAllItems_ClearsVisiblePage()
    {
        var source = MakeSource(4);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = source;

        source.Clear();
        lb.SnapAllAnimations();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Empty(lb.VisiblePage);
    }

    // -------------------------------------------------------------------------
    // FullItems rebind
    // -------------------------------------------------------------------------

    [AvaloniaFact]
    public void RebindFullItems_ShowsNewSource_Immediately()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(4);

        var newSource = new ObservableCollection<TestItem>
        {
            new("NewA", 0), new("NewB", 1)
        };
        lb.FullItems = newSource;

        Assert.Equal(2, lb.VisiblePage.Count);
        Assert.Equal("NewA", ((TestItem)lb.VisiblePage[0]).Name);
    }

    [AvaloniaFact]
    public void RebindFullItems_OldSourceChanges_NotReflected()
    {
        var oldSource = MakeSource(4);
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = oldSource;

        var newSource = new ObservableCollection<TestItem> { new("NewA", 0) };
        lb.FullItems = newSource;

        oldSource.Add(new TestItem("ShouldNotAppear", 99));

        Assert.Equal(1, lb.VisiblePage.Count);
        Assert.Equal("NewA", ((TestItem)lb.VisiblePage[0]).Name);
    }

    [AvaloniaFact]
    public void RebindFullItems_ResetsCurrentPage()
    {
        var lb = MakeListBox(pageSize: 4);
        lb.FullItems = MakeSource(8);
        lb.CurrentPage = 1;

        lb.FullItems = MakeSource(4);

        Assert.Equal(0, lb.CurrentPage);
    }
}
