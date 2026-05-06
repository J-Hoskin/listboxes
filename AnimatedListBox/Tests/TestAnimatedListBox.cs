using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using YourApp.Controls;
using YourApp.ViewModels;

namespace YourApp.Tests;

/// <summary>
/// Test-friendly subclass of AnimatedListBox.
/// Sets all animation durations to zero so tests don't need to await real timers.
/// Exposes VisibleItems and SnapAllAnimations through the public API surface.
/// </summary>
public class TestAnimatedListBox : AnimatedListBox
{
    public TestAnimatedListBox()
    {
        AnimationDuration = TimeSpan.Zero;
    }

    /// <summary>Exposes the internal VisibleItems for test assertions.</summary>
    public IReadOnlyList<object> VisiblePage => VisibleItems;

    /// <summary>Exposes SnapAllAnimations for tests that need to force completion.</summary>
    public new void SnapAllAnimations() => base.SnapAllAnimations();
}

/// <summary>
/// Test-friendly subclass of OrderedAnimatedListBox.
/// </summary>
public class TestOrderedAnimatedListBox : OrderedAnimatedListBox
{
    public TestOrderedAnimatedListBox()
    {
        AnimationDuration = TimeSpan.Zero;
    }

    public IReadOnlyList<object> VisiblePage => VisibleItems;

    public new void SnapAllAnimations() => base.SnapAllAnimations();
}

/// <summary>
/// Minimal IAnimatedItem implementation for tests.
/// </summary>
public class TestItem : IOrderedAnimatedItem
{
    public Guid Id { get; }
    public int Order { get; set; }
    public string Name { get; }

    public TestItem(string name, int order = 0)
    {
        Id = Guid.NewGuid();
        Name = name;
        Order = order;
    }
}

/// <summary>
/// Minimal ITransferCoordinator for tests.
/// </summary>
public class TestCoordinator : TransferCoordinator { }
