using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace YourApp.Controls;

/// <summary>
/// ListBoxItem subclass with a TranslateTransform we own, so we can animate it without
/// fighting other transforms and so we can guarantee a clean reset on recycling.
/// </summary>
public class AnimatedItemContainer : ListBoxItem
{
    public TranslateTransform Translate { get; } = new();

    public AnimatedItemContainer()
    {
        RenderTransform = Translate;
        // Default neutral state.
        Opacity = 1.0;
    }

    /// <summary>
    /// Synchronously snap to neutral state. Called when the container is recycled
    /// or when an animation is cancelled, to guarantee clean state.
    /// </summary>
    public void ResetVisuals()
    {
        Translate.X = 0;
        Translate.Y = 0;
        Opacity = 1.0;
    }

    protected override Type StyleKeyOverride => typeof(ListBoxItem);
}
