using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using YourApp.ViewModels;

namespace YourApp.Controls;

/// <summary>
/// AnimatedListBox variant that orders items by IOrderedAnimatedItem.Order and animates
/// reorder operations using FLIP (First, Last, Invert, Play).
///
/// Reorder cases:
///   1. Old and new positions both on current page  → FLIP between slots (ResolvedReorderDuration)
///   2. Item leaves current page due to order change → slide out bottom  (ResolvedExitDuration)
///   3. Item arrives on current page from elsewhere  → slide in from bottom (ResolvedEntryDuration)
///
/// FLIP correctness for recycled containers:
///   DiffVisible may recycle a container before ApplyFlip fires (Render priority).
///   In that case, PendingFlipDelta is stored on the container and picked up by
///   PrepareContainerForItemOverride → OnPendingFlipDelta, ensuring both items animate.
/// </summary>
public class OrderedAnimatedListBox : AnimatedListBox
{
    private readonly Dictionary<Guid, Rect> _preLayoutBounds = new();

    /// <summary>
    /// Incremented on every OnBeforeAnimatedDiff and every snap.
    /// ApplyFlip checks this before running to discard stale captures.
    /// </summary>
    private int _flipGeneration;

    // -------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------

    protected override List<object> MaterializeFullItems()
    {
        var items = base.MaterializeFullItems();
        return items
            .OfType<IOrderedAnimatedItem>()
            .OrderBy(x => x.Order)
            .Cast<object>()
            .ToList();
    }

    // -------------------------------------------------------------------------
    // FLIP — capture before layout, apply after
    // -------------------------------------------------------------------------

    protected override void OnBeforeAnimatedDiff()
    {
        CapturePreLayoutBounds();
        var capturedGeneration = ++_flipGeneration;

        // Post at Render priority so layout has settled before we read new positions.
        Dispatcher.UIThread.Post(() =>
        {
            if (capturedGeneration == _flipGeneration)
                ApplyFlip();
        }, DispatcherPriority.Render);
    }

    private void CapturePreLayoutBounds()
    {
        _preLayoutBounds.Clear();

        foreach (var item in VisibleItems)
        {
            if (item is not IAnimatedItem ai) continue;
            if (ContainerFromItem(item) is not AnimatedItemContainer container) continue;

            var panel = container.Parent as Visual ?? this;
            var topLeft = container.TranslatePoint(new Point(0, 0), panel) ?? new Point(0, 0);
            _preLayoutBounds[ai.Id] = new Rect(topLeft, container.Bounds.Size);
        }
    }

    private void ApplyFlip()
    {
        if (_preLayoutBounds.Count == 0) return;

        foreach (var item in VisibleItems)
        {
            if (item is not IAnimatedItem ai) continue;
            if (!_preLayoutBounds.TryGetValue(ai.Id, out var oldRect)) continue;
            if (ContainerFromItem(item) is not AnimatedItemContainer container) continue;

            var panel = container.Parent as Visual ?? this;
            var newTopLeft = container.TranslatePoint(new Point(0, 0), panel) ?? new Point(0, 0);

            var dx = oldRect.X - newTopLeft.X;
            var dy = oldRect.Y - newTopLeft.Y;

            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;

            if (container.DataContext == item)
            {
                // Container still bound to this item — start FLIP directly.
                StartFlipAnimation(container, dx, dy);
            }
            else
            {
                // Container was recycled during DiffVisible. Stash delta so
                // PrepareContainerForItemOverride → OnPendingFlipDelta picks it up.
                container.PendingFlipDelta = (dx, dy);
            }
        }

        _preLayoutBounds.Clear();
    }

    private void StartFlipAnimation(AnimatedItemContainer container, double dx, double dy)
    {
        container.Translate.X = dx;
        container.Translate.Y = dy;

        var captured = container;
        RunAnimation(
            container,
            container.Translate,
            BuildFlipAnimation(dx, dy, ResolvedReorderDuration),
            onComplete:  () => { captured.Translate.X = 0; captured.Translate.Y = 0; },
            onCancelled: () => { captured.Translate.X = 0; captured.Translate.Y = 0; },
            cancellation: default);
    }

    // -------------------------------------------------------------------------
    // OnPendingFlipDelta — handles containers recycled before ApplyFlip fires
    // -------------------------------------------------------------------------

    protected override void OnPendingFlipDelta(AnimatedItemContainer container, double dx, double dy)
    {
        StartFlipAnimation(container, dx, dy);
    }

    // -------------------------------------------------------------------------
    // Snap override — invalidate pending FLIP state
    // -------------------------------------------------------------------------

    protected override void OnSnapAllAnimations()
    {
        _flipGeneration++;
        _preLayoutBounds.Clear();
    }

    // -------------------------------------------------------------------------
    // Page-edge entry/exit directions
    // -------------------------------------------------------------------------

    protected override AnimationDirection? ResolveEntryDirection(EntryReason reason)
    {
        if (reason == EntryReason.FromOtherPage)
            return AnimationDirection.Down;

        return base.ResolveEntryDirection(reason);
    }

    protected override AnimationDirection? ResolveExitDirection(ExitReason reason)
    {
        if (reason == ExitReason.ToOtherPage)
            return AnimationDirection.Down;

        return base.ResolveExitDirection(reason);
    }

    // -------------------------------------------------------------------------
    // FLIP animation builder
    // -------------------------------------------------------------------------

    private static Animation BuildFlipAnimation(double fromDx, double fromDy, TimeSpan duration)
        => new()
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, fromDx),
                        new Setter(TranslateTransform.YProperty, fromDy)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 0d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                }
            }
        };
}
