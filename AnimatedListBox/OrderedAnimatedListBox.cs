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
/// reorder operations with directional sliding and Z-index layering.
///
/// SWAP ANIMATION:
///   When items change order within the visible page, each item slides straight to its
///   new position. Items moving up the list render in front (higher Z); items moving
///   down render behind (lower Z). This creates the visual of items passing each other.
///
/// PAGE-EDGE TRANSITIONS:
///   Items arriving from the next page (higher index) slide up from the bottom edge.
///   Items arriving from the previous page (lower index) slide down from the top edge.
///   Items departing are mirrored. New items (not previously in the list) fade in.
///
/// RECYCLED CONTAINER HANDLING:
///   DiffVisible may recycle a container before the Render-priority ApplyFlip fires.
///   PendingFlipDelta on the container captures the delta so OnPendingFlipDelta can
///   start the correct animation when PrepareContainerForItemOverride runs.
/// </summary>
public class OrderedAnimatedListBox : AnimatedListBox
{
    private readonly Dictionary<Guid, Rect> _preLayoutBounds = new();

    /// <summary>
    /// Full ordered list snapshot captured just before DiffVisible runs.
    /// Used to determine whether a newly appearing item came from the next or previous
    /// page, so the correct entry direction can be assigned without coordinator stamping.
    /// </summary>
    private List<object> _preChangeFullList = new();

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
        // Snapshot the full pre-change list so ApplyFlip can compute page-relative
        // positions for items that weren't previously on the visible page.
        _preChangeFullList = MaterializeFullItems();

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
            if (ContainerFromItem(item) is not AnimatedItemContainer container) continue;

            if (!_preLayoutBounds.TryGetValue(ai.Id, out var oldRect))
            {
                // Item was NOT on the visible page before this diff — it's a new arrival.
                // Determine where it came from and set the appropriate entry reason.
                var entryReason = ResolvePageEntryReason(ai);
                SetEntryAnimation(container, entryReason);
                continue;
            }

            var panel = container.Parent as Visual ?? this;
            var newTopLeft = container.TranslatePoint(new Point(0, 0), panel) ?? new Point(0, 0);

            var dx = oldRect.X - newTopLeft.X;
            var dy = oldRect.Y - newTopLeft.Y;

            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;

            if (container.DataContext == item)
            {
                StartFlipAnimation(container, dx, dy);
            }
            else
            {
                // Container was recycled during DiffVisible — stash for PrepareContainer.
                container.PendingFlipDelta = (dx, dy);
            }
        }

        _preLayoutBounds.Clear();
        _preChangeFullList = new();
    }

    // -------------------------------------------------------------------------
    // Page-edge entry reason detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Determines the correct EntryReason for an item newly arriving on the visible page.
    /// Compares the item's position in the pre-change full list against the current page
    /// range to decide whether it came from the next page, previous page, or is brand new.
    /// No coordinator stamping required — the control has all the information it needs.
    /// </summary>
    private EntryReason ResolvePageEntryReason(IAnimatedItem ai)
    {
        var pageSize = Math.Max(1, PageSize);
        var pageStart = CurrentPage * pageSize;
        var pageEnd = pageStart + pageSize - 1;

        // Find where this item was in the full list before the change.
        var oldIndex = _preChangeFullList
            .OfType<IAnimatedItem>()
            .Select((x, i) => (x.Id == ai.Id, i))
            .Where(t => t.Item1)
            .Select(t => (int?)t.i)
            .FirstOrDefault();

        if (oldIndex == null)
            return EntryReason.Default;         // Truly new item — fade in.

        if (oldIndex > pageEnd)
            return EntryReason.FromNextPage;    // Was below the page — slide up from bottom.

        return EntryReason.FromPreviousPage;    // Was above the page — slide down from top.
    }

    // -------------------------------------------------------------------------
    // Exit reason for items leaving the visible page
    // -------------------------------------------------------------------------

    /// <summary>
    /// Determines the correct ExitReason for an item leaving the visible page due to
    /// an order change. Checks where the item ends up in the new full list relative to
    /// the current page range.
    /// </summary>
    private ExitReason ResolvePageExitReason(IAnimatedItem ai)
    {
        var full = MaterializeFullItems();
        var pageSize = Math.Max(1, PageSize);
        var pageStart = CurrentPage * pageSize;
        var pageEnd = pageStart + pageSize - 1;

        var newIndex = full
            .OfType<IAnimatedItem>()
            .Select((x, i) => (x.Id == ai.Id, i))
            .Where(t => t.Item1)
            .Select(t => (int?)t.i)
            .FirstOrDefault();

        if (newIndex == null)
            return ExitReason.Default;          // Item removed entirely — fade out.

        if (newIndex > pageEnd)
            return ExitReason.ToNextPage;       // Moving to a later page — exit bottom.

        return ExitReason.ToPreviousPage;       // Moving to an earlier page — exit top.
    }

    // -------------------------------------------------------------------------
    // Entry / exit direction resolution
    // -------------------------------------------------------------------------

    protected override AnimationDirection? ResolveEntryDirection(EntryReason reason)
        => reason switch
        {
            EntryReason.FromNextPage     => AnimationDirection.Up,   // arrives from below, slides up into view... 
            EntryReason.FromPreviousPage => AnimationDirection.Down, // arrives from above, slides down into view
            _                            => base.ResolveEntryDirection(reason)
        };

    protected override AnimationDirection? ResolveExitDirection(ExitReason reason)
        => reason switch
        {
            ExitReason.ToNextPage     => AnimationDirection.Down, // exits toward bottom
            ExitReason.ToPreviousPage => AnimationDirection.Up,   // exits toward top
            _                         => base.ResolveExitDirection(reason)
        };

    // -------------------------------------------------------------------------
    // FLIP animation — directional slide with Z-index layering
    // -------------------------------------------------------------------------

    private void StartFlipAnimation(AnimatedItemContainer container, double dx, double dy)
    {
        container.Translate.X = dx;
        container.Translate.Y = dy;

        // Items moving UP (dy > 0 means old position was lower on screen, new is higher)
        // render in front. Items moving DOWN render behind.
        Panel.SetZIndex(container, dy > 0 ? 1 : 0);

        var captured = container;
        RunAnimation(
            container,
            container.Translate,
            BuildFlipAnimation(dx, dy, ResolvedReorderDuration),
            onComplete: () =>
            {
                captured.Translate.X = 0;
                captured.Translate.Y = 0;
                Panel.SetZIndex(captured, 0);
            },
            onCancelled: () =>
            {
                captured.Translate.X = 0;
                captured.Translate.Y = 0;
                Panel.SetZIndex(captured, 0);
            },
            cancellation: default);
    }

    /// <summary>
    /// Triggers the correct entry animation for a newly-arrived item directly on its
    /// container. Called from ApplyFlip for items that weren't previously on the page.
    /// </summary>
    private void SetEntryAnimation(AnimatedItemContainer container, EntryReason reason)
    {
        var direction = ResolveEntryDirection(reason);

        if (direction == null)
        {
            // Fade in — run through base entry path which calls RunFade via RunAnimation.
            // We call this by invoking the same logic PrepareContainer would use.
            container.Opacity = 0;
            RunAnimation(
                container,
                container,
                BuildOpacityAnimation(0, 1, ResolvedEntryDuration),
                onComplete:  () => container.Opacity = 1,
                onCancelled: () => container.Opacity = 1,
                cancellation: default);
        }
        else
        {
            // Slide in from the page edge.
            var panel = container.Parent as Visual ?? this;
            var h = container.Bounds.Height > 0 ? container.Bounds.Height
                  : Bounds.Height > 0 ? Bounds.Height / Math.Max(1, PageSize) : 60;

            var fromY = direction == AnimationDirection.Up ? h : -h;

            container.Translate.X = 0;
            container.Translate.Y = fromY;

            RunAnimation(
                container,
                container.Translate,
                BuildFlipAnimation(0, fromY, ResolvedEntryDuration),
                onComplete:  () => { container.Translate.X = 0; container.Translate.Y = 0; },
                onCancelled: () => { container.Translate.X = 0; container.Translate.Y = 0; },
                cancellation: default);
        }
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
        _preChangeFullList = new();
    }

    // -------------------------------------------------------------------------
    // Animation builders
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

    private static Animation BuildOpacityAnimation(double from, double to, TimeSpan duration)
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
                    Setters = { new Setter(Visual.OpacityProperty, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, to) }
                }
            }
        };
}
