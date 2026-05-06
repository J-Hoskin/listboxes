using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;
using YourApp.Controls;

namespace YourApp.Tests;

[Collection("Avalonia")]
public class AnimatedItemContainerTests
{
    [AvaloniaFact]
    public void ResetVisuals_SetsTranslateToZero()
    {
        var container = new AnimatedItemContainer();
        container.Translate.X = 100;
        container.Translate.Y = 200;

        container.ResetVisuals();

        Assert.Equal(0, container.Translate.X);
        Assert.Equal(0, container.Translate.Y);
    }

    [AvaloniaFact]
    public void ResetVisuals_SetsOpacityToOne()
    {
        var container = new AnimatedItemContainer();
        container.Opacity = 0;

        container.ResetVisuals();

        Assert.Equal(1.0, container.Opacity);
    }

    [AvaloniaFact]
    public void ResetVisuals_ClearsPendingFlipDelta()
    {
        var container = new AnimatedItemContainer();
        container.PendingFlipDelta = (10, 20);

        container.ResetVisuals();

        Assert.Null(container.PendingFlipDelta);
    }

    [AvaloniaFact]
    public void ResetVisuals_ResetsZIndex()
    {
        var container = new AnimatedItemContainer();
        Panel.SetZIndex(container, 5);

        container.ResetVisuals();

        Assert.Equal(0, Panel.GetZIndex(container));
    }

    [AvaloniaFact]
    public void ResetVisuals_ResetsIsHitTestVisible()
    {
        var container = new AnimatedItemContainer();
        container.IsHitTestVisible = false;

        container.ResetVisuals();

        Assert.True(container.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void Constructor_InitialisesWithNeutralState()
    {
        var container = new AnimatedItemContainer();

        Assert.Equal(0, container.Translate.X);
        Assert.Equal(0, container.Translate.Y);
        Assert.Equal(1.0, container.Opacity);
        Assert.Null(container.PendingFlipDelta);
        Assert.True(container.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void Constructor_SetsRenderTransformToTranslate()
    {
        var container = new AnimatedItemContainer();

        Assert.IsType<TranslateTransform>(container.RenderTransform);
        Assert.Same(container.Translate, container.RenderTransform);
    }

    [AvaloniaFact]
    public void PendingFlipDelta_CanBeSetAndRead()
    {
        var container = new AnimatedItemContainer();
        container.PendingFlipDelta = (15.5, -30.0);

        Assert.NotNull(container.PendingFlipDelta);
        Assert.Equal(15.5, container.PendingFlipDelta.Value.X);
        Assert.Equal(-30.0, container.PendingFlipDelta.Value.Y);
    }

    [AvaloniaFact]
    public void ResetVisuals_CalledMultipleTimes_RemainsNeutral()
    {
        var container = new AnimatedItemContainer();
        container.Translate.X = 50;
        container.Opacity = 0.5;
        container.PendingFlipDelta = (1, 2);

        container.ResetVisuals();
        container.ResetVisuals();

        Assert.Equal(0, container.Translate.X);
        Assert.Equal(1.0, container.Opacity);
        Assert.Null(container.PendingFlipDelta);
    }
}
