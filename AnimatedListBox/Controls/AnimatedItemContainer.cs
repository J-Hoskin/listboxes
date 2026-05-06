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
    /// DiffVisible before ApplyFlip fires. PrepareContainerForItemOverride reads this
    /// before ResetVisuals clears it, then calls OnPendingFlipDelta.
    /// </summary>
    public (double X, double Y)? PendingFlipDelta { get; set; }

    public AnimatedItemContainer()
    {
        RenderTransform = Translate;
        Opacity = 1.0;
    }

    /// <summary>
    /// Synchronously snaps all visual state to neutral. Called on recycling and snap.
    /// Resets Z-index, translate, opacity, and clears PendingFlipDelta.
    /// </summary>
    public void ResetVisuals()
    {
        Translate.X = 0;
        Translate.Y = 0;
        Opacity = 1.0;
        PendingFlipDelta = null;
        Panel.SetZIndex(this, 0);
    }

    protected override Type StyleKeyOverride => typeof(ListBoxItem);
}
