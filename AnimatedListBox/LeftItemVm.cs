using System;
using ReactiveUI;
using YourApp.Controls;
using YourApp.ViewModels;

namespace YourApp.Examples;

public class LeftItemVm : ReactiveObject, IOrderedAnimatedItem
{
    internal ItemModel Model { get; }

    public Guid Id    => Model.Id;
    public string Name => Model.Name;
    public int Order  => Model.Order;

    /// <summary>
    /// Returns the underlying model so AnimatedListBox can push it to SelectedModel
    /// without knowing the concrete wrapper or model type.
    /// </summary>
    public object? GetModel() => Model;

    public LeftItemVm(ItemModel model)
    {
        Model = model;
    }
}
