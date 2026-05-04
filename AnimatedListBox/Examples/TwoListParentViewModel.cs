using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using YourApp.Controls;
using YourApp.ViewModels;

namespace YourApp.Examples;

/// <summary>
/// Example parent view-model for two animated lists sharing a SourceCache.
///
/// Derives from TransferCoordinator so it stamps entry/exit reasons and also
/// satisfies IPeekableCoordinator (implemented by TransferCoordinator) so the
/// AnimatedListBox can correctly classify transfer events for queue-jumping.
///
/// THROTTLING:
///   The source subscriptions use Throttle(150ms) to collapse rapid service-driven
///   order changes into a single update per 150ms window. This keeps the animation
///   pipeline from being overwhelmed by high-frequency backend updates.
///
///   Transfers bypass this entirely — they come from explicit user commands, not
///   the throttled subscription, so they are always processed immediately.
///
/// IMPORTANT: StampTransfer must be called BEFORE the cache mutation so both
/// controls see the stamped reason when DynamicData propagates the change.
/// </summary>
public class TwoListParentViewModel : TransferCoordinator
{
    private static readonly TimeSpan ThrottleRate = TimeSpan.FromMilliseconds(150);

    private readonly SourceCache<ItemModel, Guid> _cache;

    public ReadOnlyObservableCollection<LeftItemVm> LeftItemsBindable { get; }
    public ReadOnlyObservableCollection<RightItemVm> RightItemsBindable { get; }

    public ICommand TransferLeftToRightCommand { get; }
    public ICommand TransferRightToLeftCommand { get; }

    public TwoListParentViewModel(SourceCache<ItemModel, Guid> cache)
    {
        _cache = cache;

        // Throttle collapses rapid service-driven updates (order changes, bulk adds)
        // into one emission per 150ms window. The Bind happens after throttle so the
        // ObservableCollection — and therefore the AnimatedListBox — only sees the
        // collapsed update, not every intermediate state.
        //
        // ObserveOn(RxApp.MainThreadScheduler) or AvaloniaScheduler.Instance if using
        // ReactiveUI — ensures the Bind drives the ObservableCollection on the UI thread.
        cache.Connect()
            .Filter(m => !m.IsOnRight)
            .Transform(m => new LeftItemVm(m))
            .Sort(SortExpressionComparer<LeftItemVm>.Ascending(x => x.Order))
            .Throttle(ThrottleRate)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Bind(out var leftBindable)
            .Subscribe();
        LeftItemsBindable = leftBindable;

        cache.Connect()
            .Filter(m => m.IsOnRight)
            .Transform(m => new RightItemVm(m))
            .Sort(SortExpressionComparer<RightItemVm>.Ascending(x => x.Order))
            .Throttle(ThrottleRate)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Bind(out var rightBindable)
            .Subscribe();
        RightItemsBindable = rightBindable;

        TransferLeftToRightCommand = new DelegateCommand(o =>
        {
            if (o is IAnimatedItem item) TransferLeftToRight(item);
        });

        TransferRightToLeftCommand = new DelegateCommand(o =>
        {
            if (o is IAnimatedItem item) TransferRightToLeft(item);
        });
    }

    /// <summary>
    /// Moves an item from the left list to the right list.
    /// Stamp happens synchronously before the cache mutation so both controls
    /// see the reason when DynamicData propagates the change on the same dispatcher tick.
    /// Note: transfers do NOT go through the throttle — they call the cache directly.
    /// </summary>
    private void TransferLeftToRight(IAnimatedItem item)
    {
        StampTransfer(item.Id, ExitReason.ToSibling, EntryReason.FromSibling);

        _cache.Edit(updater =>
        {
            var lookup = updater.Lookup(item.Id);
            if (lookup.HasValue)
            {
                lookup.Value.IsOnRight = true;
                updater.AddOrUpdate(lookup.Value);
            }
        });
    }

    private void TransferRightToLeft(IAnimatedItem item)
    {
        StampTransfer(item.Id, ExitReason.ToSibling, EntryReason.FromSibling);

        _cache.Edit(updater =>
        {
            var lookup = updater.Lookup(item.Id);
            if (lookup.HasValue)
            {
                lookup.Value.IsOnRight = false;
                updater.AddOrUpdate(lookup.Value);
            }
        });
    }
}

// ----------------------------------------------------------------------------
// Stub model and VMs — replace with your real types
// ----------------------------------------------------------------------------

public class ItemModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsOnRight { get; set; }
    public int Order { get; set; }
}

public class LeftItemVm : IOrderedAnimatedItem
{
    private readonly ItemModel _model;
    public LeftItemVm(ItemModel model) => _model = model;
    public Guid Id => _model.Id;
    public string Name => _model.Name;
    public int Order => _model.Order;
}

public class RightItemVm : IOrderedAnimatedItem
{
    private readonly ItemModel _model;
    public RightItemVm(ItemModel model) => _model = model;
    public Guid Id => _model.Id;
    public string Name => _model.Name;
    public int Order => _model.Order;
}

/// <summary>
/// Minimal ICommand for the example.
/// Replace with ReactiveCommand / CommunityToolkit RelayCommand etc. if you have one.
/// </summary>
public class DelegateCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public DelegateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
