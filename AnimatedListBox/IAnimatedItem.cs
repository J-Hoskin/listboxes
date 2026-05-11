using System;

namespace YourApp.ViewModels;

/// <summary>
/// Implemented by item view-models displayed in an AnimatedListBox.
/// The Id must be stable across wrapper VM recreations (i.e. derived from the underlying model's id).
/// GetModel returns the underlying raw model so the control can push it to SelectedModel
/// without knowing the concrete wrapper or model type.
/// </summary>
public interface IAnimatedItem
{
    Guid Id { get; }

    /// <summary>
    /// Returns the underlying raw model this wrapper represents.
    /// Used by AnimatedListBox to extract the model when the user selects an item,
    /// so it can be pushed to the SelectedModel property for two-way binding.
    /// </summary>
    object? GetModel();
}

/// <summary>
/// Adds the Order property required by OrderedAnimatedListBox.
/// </summary>
public interface IOrderedAnimatedItem : IAnimatedItem
{
    int Order { get; }
}
