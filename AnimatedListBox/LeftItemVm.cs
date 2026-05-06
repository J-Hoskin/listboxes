using System;
using ReactiveUI;
using YourApp.Controls;
using YourApp.ViewModels;

namespace YourApp.Examples;

/// <summary>
/// Wrapper VM for items displayed in the left AnimatedListBox.
/// Implements IOrderedAnimatedItem for the control, and ReactiveObject
/// so IsSelected can drive the selection highlight binding in the template.
/// </summary>
public class LeftItemVm : ReactiveObject, IOrderedAnimatedItem
{
    /// <summary>
    /// Internal accessor to the underlying model. Used by LeftListViewModel
    /// to update SelectionService.SelectedModel when the user clicks this item.
    /// </summary>
    internal ItemModel Model { get; }

    public Guid Id    => Model.Id;
    public string Name => Model.Name;
    public int Order  => Model.Order;

    private bool _isSelected;

    /// <summary>
    /// Drives the selection highlight in the item template via BoolToBrushConverter.
    /// Set by LeftListViewModel.OnExternalSelectionChanged — not by the item itself.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public LeftItemVm(ItemModel model)
    {
        Model = model;
    }
}
