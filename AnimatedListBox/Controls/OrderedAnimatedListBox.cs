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
/// FLIP animations run through the base class RunAnimation method so they are correctly
/// counted by _inflightCount and the pipeline gate releases at the right time.
/// </summary>
public class OrderedAnimatedListBox : AnimatedListBox
{
    private readonly Dictionary<Guid, Rect> _preLayoutBounds = new();

    /// <summary>
    /// Incremented on every OnBeforeAnimatedDiff and every SnapAllAnimations.
    /// The ApplyFlip closure captures the generation at capture time and skips execution
    /// if the generation has changed — discarding stale FLIP data from collapsed updates.
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
    // FLIP hook — capture bounds before layout changes, animate after
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

            // Skip items that haven't moved (sub-pixel threshold).
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;

            // Snap to old position, then animate to (0,0) — the natural layout position.
            container.Translate.X = dx;
            container.Translate.Y = dy;

            var animation = new Animation
            {
                Duration = ResolvedReorderDuration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, dx),
                            new Setter(TranslateTransform.YProperty, dy)
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

            // Route through base RunAnimation so _inflightCount is tracked correctly.
            // This ensures the pipeline gate doesn't release until FLIP animations finish.
            var capturedContainer = container;
            RunAnimationForFlip(
                container,
                animation,
                onComplete: () =>
                {
                    capturedContainer.Translate.X = 0;
                    capturedContainer.Translate.Y = 0;
                },
                onCancelled: () =>
                {
                    capturedContainer.Translate.X = 0;
                    capturedContainer.Translate.Y = 0;
                });
        }

        _preLayoutBounds.Clear();
    }

    // -------------------------------------------------------------------------
    // Snap override — invalidate pending FLIP data
    // -------------------------------------------------------------------------

    protected override void OnSnapAllAnimations()
    {
        // Bump generation so any pending ApplyFlip post is discarded.
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

}
