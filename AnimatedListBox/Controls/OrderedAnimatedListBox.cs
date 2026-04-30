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
/// AnimatedListBox variant that orders its items by IOrderedAnimatedItem.Order and animates
/// reorder operations using FLIP (First, Last, Invert, Play).
///
/// Reorder cases handled:
///   1. Old + new positions both on the current page  → FLIP between slots (ResolvedReorderDuration)
///   2. Item leaves current page due to order change   → slide out bottom  (ResolvedExitDuration)
///   3. Item arrives on current page from elsewhere    → slide in from bottom (ResolvedEntryDuration)
/// </summary>
public class OrderedAnimatedListBox : AnimatedListBox
{
    /// <summary>
    /// Snapshot of each visible container's bounds, captured before layout runs.
    /// Used to compute the FLIP delta once layout has settled into the new order.
    /// </summary>
    private readonly Dictionary<Guid, Rect> _preLayoutBounds = new();

    private bool _flipPending;

    // --------------------------------------------------------------------
    // Ordering
    // --------------------------------------------------------------------

    protected override List<object> MaterializeFullItems()
    {
        var items = base.MaterializeFullItems();
        return items
            .OfType<IOrderedAnimatedItem>()
            .OrderBy(x => x.Order)
            .Cast<object>()
            .ToList();
    }

    // --------------------------------------------------------------------
    // FLIP hook — capture bounds before pagination refreshes layout, apply after
    // --------------------------------------------------------------------

    protected override void RefreshPagination(bool animate)
    {
        if (animate)
        {
            CapturePreLayoutBounds();
            _flipPending = true;
        }

        base.RefreshPagination(animate);

        if (_flipPending)
        {
            // Post at Render priority so layout has settled before we read new positions.
            Dispatcher.UIThread.Post(ApplyFlip, DispatcherPriority.Render);
        }
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
        _flipPending = false;
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
                FillMode = FillMode.Forward,
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

            var capturedContainer = container;
            _ = animation.RunAsync(container.Translate).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    capturedContainer.Translate.X = 0;
                    capturedContainer.Translate.Y = 0;
                });
            });
        }

        _preLayoutBounds.Clear();
    }

    // --------------------------------------------------------------------
    // Direction overrides for order-driven page transitions
    //
    // Items arriving from / leaving to another page slide in/out from the bottom edge.
    // This conveys the idea that the item moved past the visible page boundary.
    // Same-page reorders are handled by FLIP above and don't go through these methods.
    // --------------------------------------------------------------------

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
