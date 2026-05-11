using System;
using System.Collections.ObjectModel;
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
/// SELECTION:
///   SelectedModel is a public property that delegates to SelectionService.
///   The AnimatedListBox binds to it TwoWay — when the user clicks a row, the
///   control pushes the raw model out through SelectedModel. When the service
///   changes SelectedModel from elsewhere in the app, the control receives it
///   inbound, finds the matching wrapper, and jumps to the correct page.
///
///   No IsSelected property needed on wrapper VMs. No split bindings. No
///   manual subscription to SelectionService needed — the two-way binding
///   on the control handles both directions.
///
/// XAML:
///   SelectedModel="{Binding SelectedModel, Mode=TwoWay}"
/// </summary>
public class LeftListViewModel : ReactiveObject
{
    private readonly SelectionService _selectionService;

    public ReadOnlyObservableCollection<LeftItemVm> Items { get; }

    private int _currentPage;
    public int CurrentPage
    {
        get => _currentPage;
        set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    /// <summary>
    /// Delegates directly to SelectionService.SelectedModel.
    /// The AnimatedListBox binds to this TwoWay so both directions are handled
    /// by the control internally — no manual subscription or IsSelected management needed.
    /// </summary>
    public ItemModel? SelectedModel
    {
        get => _selectionService.SelectedModel;
        set => _selectionService.SelectedModel = value;
    }

    public LeftListViewModel(SourceCache<ItemModel, Guid> cache, SelectionService selectionService)
    {
        _selectionService = selectionService;

        cache.Connect()
            .Filter(m => !m.IsOnRight)
            .Transform(m => new LeftItemVm(m))
            .Sort(SortExpressionComparer<LeftItemVm>.Ascending(x => x.Order))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var items)
            .Subscribe();
        Items = items;

        // When SelectionService.SelectedModel changes from anywhere in the app,
        // raise SelectedModel so the TwoWay binding pushes it to the control.
        // The control then handles finding the wrapper and jumping to the right page.
        selectionService
            .WhenAnyValue(x => x.SelectedModel)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedModel)));
    }
}
