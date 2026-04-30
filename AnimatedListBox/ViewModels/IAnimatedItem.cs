using System;

namespace YourApp.ViewModels;

/// <summary>
/// Implemented by item view-models displayed in an AnimatedListBox.
/// The Id must be stable across wrapper VM recreations (i.e. derived from the underlying model's id).
/// </summary>
public interface IAnimatedItem
{
    Guid Id { get; }
}

/// <summary>
/// Adds the Order property required by OrderedAnimatedListBox.
/// </summary>
public interface IOrderedAnimatedItem : IAnimatedItem
{
    int Order { get; }
}
