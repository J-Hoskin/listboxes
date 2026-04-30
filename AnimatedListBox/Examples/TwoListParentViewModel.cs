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
/// EXAMPLE — adapt to your actual model/VM names.
///
/// Derives from TransferCoordinator so it can stamp entry/exit reasons before
/// mutating the cache. Both lists share the same coordinator instance (this).
/// </summary>
public class TwoListParentViewModel : TransferCoordinator
{
    private readonly SourceCache<ItemModel, Guid> _cache;

    public ReadOnlyObservableCollection<LeftItemVm> LeftItemsBindable { get; }
    public ReadOnlyObservableCollection<RightItemVm> RightItemsBindable { get; }

    public ICommand TransferLeftToRightCommand { get; }
    public ICommand TransferRightToLeftCommand { get; }

    public TwoListParentViewModel(SourceCache<ItemModel, Guid> cache)
    {
        _cache = cache;

        cache.Connect()
            .Filter(m => !m.IsOnRight)
            .Transform(m => new LeftItemVm(m))
            .Sort(SortExpressionComparer<LeftItemVm>.Ascending(x => x.Order))
            .Bind(out var leftBindable)
            .Subscribe();
        LeftItemsBindable = leftBindable;

        cache.Connect()
            .Filter(m => m.IsOnRight)
            .Transform(m => new RightItemVm(m))
            .Sort(SortExpressionComparer<RightItemVm>.Ascending(x => x.Order))
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

    private void TransferLeftToRight(IAnimatedItem item)
    {
        // Stamp BEFORE mutating — both controls consume their reason when DynamicData
        // propagates the cache change synchronously on the UI thread.
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
// Stub model + VMs — replace with your real types
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
/// Minimal ICommand implementation for the example. Replace with your project's
/// ReactiveCommand / RelayCommand / etc. if you have one.
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
