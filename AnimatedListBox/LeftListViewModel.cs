using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using YourApp.Controls;
using YourApp.Services;
using YourApp.ViewModels;

namespace YourApp.Examples;

/// <summary>
/// View model for the left animated list.
///
/// SELECTION FLOW:
///   User clicks row in UI:
///     SelectedItem setter → SelectionService.SelectedModel updated
///     → OnExternalSelectionChanged fires on both VMs
///     → This VM finds the wrapper and marks IsSelected = true
///     → Other VM finds no wrapper (item is on this side) and clears
///
///   VM sets SelectedItem programmatically (e.g. service notifies selection changed):
///     RaisePropertyChanged(SelectedItem) → ProgrammaticSelectedItem binding pushes to control
///     → OnProgrammaticSelectedItemChanged on control handles page jump + SelectedItem set
///
/// XAML BINDINGS REQUIRED:
///   SelectedItem="{Binding SelectedItem, Mode=OneWayToSource}"
///   ProgrammaticSelectedItem="{Binding SelectedItem, Mode=OneWay}"
///   CurrentPage="{Binding CurrentPage, Mode=TwoWay}"
/// </summary>
public class LeftListViewModel : ReactiveObject
{
    private readonly SelectionService _selectionService;
    private readonly SourceCache<ItemModel, Guid> _cache;

    public ReadOnlyObservableCollection<LeftItemVm> Items { get; }

    private LeftItemVm? _selectedItem;

    /// <summary>
    /// The currently selected wrapper VM.
    /// Setter is called when the user clicks a row (via OneWayToSource binding on SelectedItem).
    /// Getter is observed by ProgrammaticSelectedItem (via OneWay binding) to push selection
    /// into the control when the service changes selection externally.
    /// </summary>
    public LeftItemVm? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;

            // Update service when the user clicks a row.
            // OnExternalSelectionChanged will also fire from this, but the guard
            // inside prevents a feedback loop (service already has this model).
            _selectionService.SelectedModel = value?.Model;
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }
    }

    private int _currentPage;
    public int CurrentPage
    {
        get => _currentPage;
        set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    public LeftListViewModel(SourceCache<ItemModel, Guid> cache, SelectionService selectionService)
    {
        _cache = cache;
        _selectionService = selectionService;

        cache.Connect()
            .Filter(m => !m.IsOnRight)
            .Transform(m => new LeftItemVm(m))
            .Sort(SortExpressionComparer<LeftItemVm>.Ascending(x => x.Order))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var items)
            .Subscribe();
        Items = items;

        // React to external selection changes (from the service or the other list).
        selectionService
            .WhenAnyValue(x => x.SelectedModel)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnExternalSelectionChanged);
    }

    private void OnExternalSelectionChanged(ItemModel? model)
    {
        // Clear IsSelected on the previous wrapper without notifying the service
        // (we're reacting to a service change, not initiating one).
        if (_selectedItem != null)
        {
            _selectedItem.IsSelected = false;
        }

        if (model == null)
        {
            _selectedItem = null;
            this.RaisePropertyChanged(nameof(SelectedItem));
            return;
        }

        // Guard: if this VM was the one that just set the service value (user click),
        // the service model already matches our current selection — avoid a feedback loop.
        if (_selectedItem?.Model == model)
            return;

        var wrapper = Items.FirstOrDefault(x => x.Id == model.Id);

        if (wrapper == null)
        {
            // Item is not in this list (it's on the right side) — clear.
            _selectedItem = null;
            this.RaisePropertyChanged(nameof(SelectedItem));
            return;
        }

        wrapper.IsSelected = true;
        _selectedItem = wrapper;

        // Raising SelectedItem notifies the ProgrammaticSelectedItem OneWay binding,
        // which pushes the value to the control. The control handles the page jump
        // in OnProgrammaticSelectedItemChanged before setting ListBox.SelectedItem.
        this.RaisePropertyChanged(nameof(SelectedItem));
    }
}
