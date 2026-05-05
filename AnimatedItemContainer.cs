using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace YourApp.Controls;

/// <summary>
/// ListBoxItem subclass with an owned TranslateTransform for animation, guaranteeing
/// a clean reset on recycling with no interference from other transforms.
/// </summary>
public class AnimatedItemContainer : ListBoxItem
{
    public TranslateTransform Translate { get; } = new();

    /// <summary>
    /// Set by OrderedAnimatedListBox.ApplyFlip when a container is repositioned during
    /// DiffVisible before ApplyFlip fires. PrepareContainerForItemOverride reads and
    /// clears this (via ResetVisuals) to start a FLIP animation from the correct offset
    /// rather than a standard entry animation.
    /// Must be read BEFORE calling ResetVisuals — ResetVisuals sets it to null.
    /// </summary>
    public (double X, double Y)? PendingFlipDelta { get; set; }

    public AnimatedItemContainer()
    {
        RenderTransform = Translate;
        Opacity = 1.0;
    }

    /// <summary>
    /// Synchronously snaps all visual state to neutral. Called on recycling and snap.
    /// Clears PendingFlipDelta so stale FLIP deltas never leak to new items.
    /// </summary>
    public void ResetVisuals()
    {
        Translate.X = 0;
        Translate.Y = 0;
        Opacity = 1.0;
        PendingFlipDelta = null;
    }

    protected override Type StyleKeyOverride => typeof(ListBoxItem);
}
