using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
/// Paginated ListBox with directional entry/exit animations and cross-list transfer coordination.
///
/// ANIMATION PIPELINE (Option D):
///   All source changes are classified, queued, and processed one batch at a time.
///   The pipeline gate is driven by a completion counter (_inflightCount), not a timer —
///   the gate releases only when every animation started by a batch has actually finished
///   or been cancelled. This is correct regardless of load, frame drops, or cancellations.
///
///   Consecutive order-change updates collapse to the latest state (no stacking).
///   Transfers jump the queue, snap any in-flight animation, and play immediately.
///
/// TRANSFER ACTIVATION:
///   Bind a button in your ItemTemplate:
///     Command="{Binding $parent[controls:AnimatedListBox].TransferCommand}"
///     CommandParameter="{Binding}"
/// </summary>
public class AnimatedListBox : ListBox
{
    // -------------------------------------------------------------------------
    // Change classification
    // -------------------------------------------------------------------------

    protected enum ChangeType { Transfer, OrderChange, AddRemove }

    protected record SourceSnapshot(List<object> PageItems, ChangeType Type);

    // -------------------------------------------------------------------------
    // Styled / Direct properties
    // -------------------------------------------------------------------------

    public static readonly StyledProperty<IEnumerable?> FullItemsProperty =
        AvaloniaProperty.Register<AnimatedListBox, IEnumerable?>(nameof(FullItems));

    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<AnimatedListBox, int>(nameof(PageSize), defaultValue: 4);

    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<AnimatedListBox, int>(nameof(CurrentPage), defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<AnimatedListBox, int> PageCountProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, int>(nameof(PageCount), o => o.PageCount);

    public static readonly StyledProperty<ITransferCoordinator?> CoordinatorProperty =
        AvaloniaProperty.Register<AnimatedListBox, ITransferCoordinator?>(nameof(Coordinator));

    public static readonly StyledProperty<AnimationDirection> EntryDirectionProperty =
        AvaloniaProperty.Register<AnimatedListBox, AnimationDirection>(
            nameof(EntryDirection), defaultValue: AnimationDirection.Left);

    public static readonly StyledProperty<AnimationDirection> ExitDirectionProperty =
        AvaloniaProperty.Register<AnimatedListBox, AnimationDirection>(
            nameof(ExitDirection), defaultValue: AnimationDirection.Right);

    // Global fallback duration. Override per-phase with EntryDuration / ExitDuration / ReorderDuration.
    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan>(
            nameof(AnimationDuration), defaultValue: TimeSpan.FromMilliseconds(200));

    public static readonly StyledProperty<TimeSpan?> EntryDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(EntryDuration));

    public static readonly StyledProperty<TimeSpan?> ExitDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(ExitDuration));

    public static readonly StyledProperty<TimeSpan?> ReorderDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(ReorderDuration));

    public static readonly StyledProperty<ICommand?> TransferCommandProperty =
        AvaloniaProperty.Register<AnimatedListBox, ICommand?>(nameof(TransferCommand));

    public static readonly DirectProperty<AnimatedListBox, ICommand> NextPageCommandProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, ICommand>(nameof(NextPageCommand),
            o => o.NextPageCommand);

    public static readonly DirectProperty<AnimatedListBox, ICommand> PreviousPageCommandProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, ICommand>(nameof(PreviousPageCommand),
            o => o.PreviousPageCommand);

    // -------------------------------------------------------------------------
    // CLR wrappers
    // -------------------------------------------------------------------------

    public IEnumerable? FullItems
    {
        get => GetValue(FullItemsProperty);
        set => SetValue(FullItemsProperty, value);
    }

    public int PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    private int _pageCount;
    public int PageCount
    {
        get => _pageCount;
        private set => SetAndRaise(PageCountProperty, ref _pageCount, value);
    }

    public ITransferCoordinator? Coordinator
    {
        get => GetValue(CoordinatorProperty);
        set => SetValue(CoordinatorProperty, value);
    }

    public AnimationDirection EntryDirection
    {
        get => GetValue(EntryDirectionProperty);
        set => SetValue(EntryDirectionProperty, value);
    }

    public AnimationDirection ExitDirection
    {
        get => GetValue(ExitDirectionProperty);
        set => SetValue(ExitDirectionProperty, value);
    }

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public TimeSpan? EntryDuration
    {
        get => GetValue(EntryDurationProperty);
        set => SetValue(EntryDurationProperty, value);
    }

    public TimeSpan? ExitDuration
    {
        get => GetValue(ExitDurationProperty);
        set => SetValue(ExitDurationProperty, value);
    }

    public TimeSpan? ReorderDuration
    {
        get => GetValue(ReorderDurationProperty);
        set => SetValue(ReorderDurationProperty, value);
    }

    public ICommand? TransferCommand
    {
        get => GetValue(TransferCommandProperty);
        set => SetValue(TransferCommandProperty, value);
    }

    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    // Resolved durations — always use these in animation code, never the raw properties.
    protected TimeSpan ResolvedEntryDuration   => EntryDuration   ?? AnimationDuration;
    protected TimeSpan ResolvedExitDuration    => ExitDuration    ?? AnimationDuration;
    protected TimeSpan ResolvedReorderDuration => ReorderDuration ?? AnimationDuration;

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    /// <summary>
    /// The collection bound to ListBox.ItemsSource. Contains visible items plus any items
    /// currently mid-exit animation (ghosts). We diff against this to drive animations.
    /// </summary>
    protected readonly System.Collections.ObjectModel.ObservableCollection<object> VisibleItems = new();

    /// <summary>
    /// In-flight exit animations keyed by item id. Ghosts stay in VisibleItems until their
    /// exit completes or is snapped.
    /// </summary>
    private readonly Dictionary<Guid, ExitAnimationHandle> _activeExits = new();

    /// <summary>
    /// In-flight entry animations keyed by container. Tracked so they can be cancelled and
    /// the container reset if recycled mid-animation.
    /// </summary>
    private readonly Dictionary<AnimatedItemContainer, CancellationTokenSource> _activeEntries = new();

    /// <summary>
    /// Counts animations currently in flight for the active batch.
    /// The pipeline gate releases when this reaches zero — not on a timer.
    /// Incremented before RunAsync, decremented in ContinueWith (both complete and cancelled paths).
    /// </summary>
    private int _inflightCount;

    /// <summary>
    /// True while a batch is being processed. No new snapshot is dequeued until false.
    /// Set to true at ProcessSnapshot, set to false in OnAllAnimationsComplete.
    /// </summary>
    private bool _pipelineBusy;

    /// <summary>
    /// Incremented on every page navigation and source rebind. Lets stale OnAllAnimationsComplete
    /// callbacks (from cancelled animations that resolve after a page change) detect that they
    /// are no longer relevant and skip releasing the gate.
    /// </summary>
    private int _batchGeneration;

    /// <summary>
    /// The animation pipeline queue. Processed one snapshot at a time.
    /// Consecutive OrderChange snapshots collapse — only the latest is kept.
    /// Transfer snapshots jump the queue and clear it.
    /// </summary>
    private readonly Queue<SourceSnapshot> _pipeline = new();

    private INotifyCollectionChanged? _observedSource;
    private bool _suppressPageClamp;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public AnimatedListBox()
    {
        ItemsSource = VisibleItems;
        NextPageCommand = new RelayCommand(GoToNextPage, () => CurrentPage < PageCount - 1);
        PreviousPageCommand = new RelayCommand(GoToPreviousPage, () => CurrentPage > 0);
    }

    static AnimatedListBox()
    {
        FullItemsProperty.Changed.AddClassHandler<AnimatedListBox>(
            (o, e) => o.OnFullItemsChanged(e));
        PageSizeProperty.Changed.AddClassHandler<AnimatedListBox>(
            (o, _) => o.OnPageParametersChanged());
        CurrentPageProperty.Changed.AddClassHandler<AnimatedListBox>(
            (o, _) => o.OnCurrentPageChanged());
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    // -------------------------------------------------------------------------
    // Container creation / recycling
    // -------------------------------------------------------------------------

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new AnimatedItemContainer();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = nameof(AnimatedItemContainer);
        return true;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not AnimatedItemContainer animated || item is not IAnimatedItem ai)
            return;

        // Cancel any animation running on this container from a previous item.
        if (_activeEntries.TryGetValue(animated, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
            _activeEntries.Remove(animated);
        }

        // Snap to neutral — ensures no stale transform/opacity from prior use.
        animated.ResetVisuals();

        // Defensively clear any exit tracked for this item. Shouldn't normally happen —
        // the pipeline gate ensures exits complete before re-entries process — but if it
        // does, cancelling here prevents the stale completion from removing the re-added item.
        if (_activeExits.TryGetValue(ai.Id, out var exit))
        {
            exit.Cancel();
            _activeExits.Remove(ai.Id);
        }

        var reason = Coordinator?.ConsumeEntryReason(ai.Id) ?? EntryReason.Default;
        StartEntryAnimation(animated, reason);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is AnimatedItemContainer animated)
        {
            if (_activeEntries.TryGetValue(animated, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _activeEntries.Remove(animated);
            }
            animated.ResetVisuals();
        }
    }

    // -------------------------------------------------------------------------
    // Source change handling
    // -------------------------------------------------------------------------

    private void OnFullItemsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_observedSource != null)
            _observedSource.CollectionChanged -= OnSourceCollectionChanged;

        _observedSource = e.NewValue as INotifyCollectionChanged;
        if (_observedSource != null)
            _observedSource.CollectionChanged += OnSourceCollectionChanged;

        // New source — snap everything, clear stale pipeline, populate instantly.
        _batchGeneration++;
        _pipeline.Clear();
        _pipelineBusy = false;
        _inflightCount = 0;
        SnapAllAnimations();
        ReplaceVisible(BuildPageSlice(MaterializeFullItems()));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var type = ClassifyChange(e);
        var full = MaterializeFullItems();

        UpdatePageCount(full);

        var snapshot = new SourceSnapshot(BuildPageSlice(full), type);

        if (type == ChangeType.Transfer)
        {
            // Transfers jump the queue. Snap whatever is running, clear the entire queue
            // (all queued snapshots are superseded by the current state), and process now.
            if (_pipelineBusy)
            {
                _batchGeneration++;
                SnapAllAnimations();
                _pipelineBusy = false;
                _inflightCount = 0;
            }

            _pipeline.Clear();
            _pipeline.Enqueue(snapshot);
        }
        else if (type == ChangeType.OrderChange)
        {
            // Collapse: replace the last queued OrderChange with this one, or append.
            var items = _pipeline.ToList();
            var lastOrderIdx = items.FindLastIndex(s => s.Type == ChangeType.OrderChange);

            if (lastOrderIdx >= 0)
                items[lastOrderIdx] = snapshot;
            else
                items.Add(snapshot);

            _pipeline.Clear();
            foreach (var s in items) _pipeline.Enqueue(s);
        }
        else
        {
            // AddRemove — always enqueue, never collapse.
            _pipeline.Enqueue(snapshot);
        }

        TryProcessNext();
    }

    private void OnPageParametersChanged()
    {
        _batchGeneration++;
        _pipeline.Clear();
        _pipelineBusy = false;
        _inflightCount = 0;
        SnapAllAnimations();
        var full = MaterializeFullItems();
        UpdatePageCount(full);
        ReplaceVisible(BuildPageSlice(full));
    }

    private void OnCurrentPageChanged()
    {
        if (_suppressPageClamp) return;

        // Page navigation is always instant. Snap everything, clear the pipeline,
        // invalidate any in-flight batch via generation bump, and replace instantly.
        _batchGeneration++;
        _pipeline.Clear();
        _pipelineBusy = false;
        _inflightCount = 0;
        SnapAllAnimations();

        var full = MaterializeFullItems();
        UpdatePageCount(full);
        ReplaceVisible(BuildPageSlice(full));

        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    // -------------------------------------------------------------------------
    // Pipeline
    // -------------------------------------------------------------------------

    private void TryProcessNext()
    {
        if (_pipelineBusy) return;
        if (_pipeline.Count == 0) return;

        _pipelineBusy = true;
        var next = _pipeline.Dequeue();
        ProcessSnapshot(next);
    }

    private void ProcessSnapshot(SourceSnapshot snapshot)
    {
        // Capture the generation at the start of this batch. The completion callback
        // checks this to discard results from batches superseded by page changes or snaps.
        var capturedGeneration = _batchGeneration;

        OnBeforeAnimatedDiff();
        DiffVisible(snapshot.PageItems);

        // If DiffVisible started no animations (e.g. no-op diff), release immediately.
        if (_inflightCount == 0)
        {
            _pipelineBusy = false;
            TryProcessNext();
        }
        // Otherwise the gate releases in OnAllAnimationsComplete when _inflightCount → 0.
    }

    /// <summary>
    /// Called from RunAnimation's ContinueWith (both complete and cancelled paths) when
    /// _inflightCount reaches zero. Releases the pipeline gate and processes the next batch.
    /// Stale callbacks from superseded batches are discarded via the generation check.
    /// </summary>
    private void OnAllAnimationsComplete(int capturedGeneration)
    {
        if (capturedGeneration != _batchGeneration) return;

        _pipelineBusy = false;
        TryProcessNext();
    }

    // -------------------------------------------------------------------------
    // Snap — cancel all in-flight animations and clear ghost items
    // -------------------------------------------------------------------------

    protected void SnapAllAnimations()
    {
        // Cancel and reset all entry animations, disposing CTSes.
        foreach (var (container, cts) in _activeEntries)
        {
            cts.Cancel();
            cts.Dispose();
            container.ResetVisuals();
        }
        _activeEntries.Clear();

        // Cancel all exit animations and remove ghosts by index (not reference)
        // to guard against DynamicData wrapper recreation changing references.
        foreach (var (id, handle) in _activeExits)
        {
            handle.Cancel();
            var idx = IndexOfById(id);
            if (idx >= 0) VisibleItems.RemoveAt(idx);
        }
        _activeExits.Clear();

        // Notify subclasses (OrderedAnimatedListBox invalidates pending FLIP state).
        OnSnapAllAnimations();
    }

    // -------------------------------------------------------------------------
    // Virtual hooks for subclasses
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by ProcessSnapshot immediately before DiffVisible runs.
    /// OrderedAnimatedListBox overrides this to capture pre-layout bounds for FLIP.
    /// </summary>
    protected virtual void OnBeforeAnimatedDiff() { }

    /// <summary>
    /// Called at the end of SnapAllAnimations.
    /// OrderedAnimatedListBox overrides this to invalidate any pending FLIP capture.
    /// </summary>
    protected virtual void OnSnapAllAnimations() { }

    // -------------------------------------------------------------------------
    // Pagination helpers
    // -------------------------------------------------------------------------

    private void UpdatePageCount(List<object> full)
    {
        var pageSize = Math.Max(1, PageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(full.Count / (double)pageSize));

        var page = Math.Clamp(CurrentPage, 0, pageCount - 1);
        if (page != CurrentPage)
        {
            _suppressPageClamp = true;
            try { CurrentPage = page; } finally { _suppressPageClamp = false; }
        }

        PageCount = pageCount;
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    protected List<object> BuildPageSlice(List<object> full)
    {
        var pageSize = Math.Max(1, PageSize);
        var page = Math.Clamp(CurrentPage, 0,
            Math.Max(0, (int)Math.Ceiling(full.Count / (double)pageSize) - 1));
        return full.Skip(page * pageSize).Take(pageSize).ToList();
    }

    /// <summary>
    /// Produces the full ordered item list. Override in subclasses to apply sorting.
    /// </summary>
    protected virtual List<object> MaterializeFullItems()
    {
        if (FullItems == null) return new();
        return FullItems.Cast<object>().ToList();
    }

    // -------------------------------------------------------------------------
    // Visible set management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instant replace — no animation. Used for page changes and initial population.
    /// Snaps first so no ghost items or in-flight animations leak into the new state.
    /// </summary>
    private void ReplaceVisible(List<object> desired)
    {
        SnapAllAnimations();
        VisibleItems.Clear();
        foreach (var item in desired)
            VisibleItems.Add(item);
    }

    /// <summary>
    /// Animated diff. Removes items with exit animations, inserts/moves to match desired order.
    /// Ghost items (mid-exit) are skipped by ResolveInsertIndex so new items land correctly.
    /// </summary>
    private void DiffVisible(List<object> desired)
    {
        var desiredIds = desired.OfType<IAnimatedItem>().Select(x => x.Id).ToHashSet();

        // Remove items no longer desired (skip those already mid-exit).
        for (int i = VisibleItems.Count - 1; i >= 0; i--)
        {
            if (VisibleItems[i] is not IAnimatedItem ai) continue;
            if (desiredIds.Contains(ai.Id)) continue;
            if (_activeExits.ContainsKey(ai.Id)) continue;

            BeginExit(ai, VisibleItems[i]);
        }

        // Insert / move items to match desired order.
        for (int targetIndex = 0; targetIndex < desired.Count; targetIndex++)
        {
            var item = desired[targetIndex];
            if (item is not IAnimatedItem ai) continue;

            int currentIndex = IndexOfById(ai.Id);

            if (currentIndex < 0)
            {
                VisibleItems.Insert(ResolveInsertIndex(targetIndex), item);
            }
            else if (currentIndex != targetIndex)
            {
                int moveTo = ResolveInsertIndex(targetIndex);
                if (currentIndex != moveTo)
                    VisibleItems.Move(currentIndex, moveTo);
            }
        }
    }

    /// <summary>
    /// Finds the correct insert position for targetIndex in the desired list, treating
    /// ghost items (mid-exit) as non-existent so they don't displace new items.
    /// </summary>
    private int ResolveInsertIndex(int targetIndex)
    {
        int desiredPos = 0;
        for (int i = 0; i < VisibleItems.Count; i++)
        {
            if (VisibleItems[i] is IAnimatedItem ai && _activeExits.ContainsKey(ai.Id))
                continue; // ghost — skip

            if (desiredPos == targetIndex)
                return i;

            desiredPos++;
        }
        return VisibleItems.Count;
    }

    private int IndexOfById(Guid id)
    {
        for (int i = 0; i < VisibleItems.Count; i++)
            if (VisibleItems[i] is IAnimatedItem ai && ai.Id == id)
                return i;
        return -1;
    }

    // -------------------------------------------------------------------------
    // Change classification
    // -------------------------------------------------------------------------

    /// <summary>
    /// Classifies a collection change for pipeline routing.
    ///
    /// Transfer detection: uses HasAnyPendingReason() rather than inspecting e.NewItems/OldItems.
    /// DynamicData does not reliably populate those fields, and transfers are structurally
    /// identical to Add/Remove. The pending reason in the coordinator is the only reliable signal.
    /// The stamp exists for the duration of a single synchronous cache.Edit call — there is no
    /// race window where an unrelated change could be misclassified as a transfer.
    ///
    /// OrderChange: DynamicData's .Sort() raises Move for sort-order changes.
    /// Everything else is AddRemove.
    /// </summary>
    private ChangeType ClassifyChange(NotifyCollectionChangedEventArgs e)
    {
        if (Coordinator is IPeekableCoordinator p && p.HasAnyPendingReason())
            return ChangeType.Transfer;

        return e.Action == NotifyCollectionChangedAction.Move
            ? ChangeType.OrderChange
            : ChangeType.AddRemove;
    }

    // -------------------------------------------------------------------------
    // Entry animation
    // -------------------------------------------------------------------------

    private void StartEntryAnimation(AnimatedItemContainer container, EntryReason reason)
    {
        var direction = ResolveEntryDirection(reason);

        if (direction == null)
            RunFade(container, fromOpacity: 0, toOpacity: 1, ResolvedEntryDuration);
        else
            RunSlide(container, direction.Value, slidingIn: true, ResolvedEntryDuration);
    }

    protected virtual AnimationDirection? ResolveEntryDirection(EntryReason reason)
        => reason switch
        {
            EntryReason.FromSibling => EntryDirection,
            _ => null
        };

    // -------------------------------------------------------------------------
    // Exit animation
    // -------------------------------------------------------------------------

    private void BeginExit(IAnimatedItem ai, object item)
    {
        var reason = Coordinator?.ConsumeExitReason(ai.Id) ?? ExitReason.Default;
        var direction = ResolveExitDirection(reason);
        var container = ContainerFromItem(item) as AnimatedItemContainer;

        if (container == null)
        {
            VisibleItems.Remove(item);
            return;
        }

        var cts = new CancellationTokenSource();
        _activeExits[ai.Id] = new ExitAnimationHandle(item, container, cts);

        var exitId = ai.Id;
        var exitCts = cts;

        Action onComplete = () =>
        {
            if (exitCts.IsCancellationRequested) return;

            // Only remove if this exit is still the registered one. PrepareContainer clears
            // the registration if the item was re-added before this callback fired.
            if (_activeExits.ContainsKey(exitId))
            {
                _activeExits.Remove(exitId);
                var idx = IndexOfById(exitId);
                if (idx >= 0) VisibleItems.RemoveAt(idx);
            }
        };

        Action onCancelled = () =>
        {
            _activeExits.Remove(exitId);
            container.ResetVisuals();
        };

        if (direction == null)
            RunFade(container, 1, 0, ResolvedExitDuration, onComplete, onCancelled, cts.Token);
        else
            RunSlide(container, direction.Value, false, ResolvedExitDuration, onComplete, onCancelled, cts.Token);
    }

    protected virtual AnimationDirection? ResolveExitDirection(ExitReason reason)
        => reason switch
        {
            ExitReason.ToSibling => ExitDirection,
            _ => null
        };

    // -------------------------------------------------------------------------
    // Animation primitives
    // -------------------------------------------------------------------------

    private (double X, double Y) OffscreenOffset(AnimatedItemContainer c, AnimationDirection d)
    {
        var w = c.Bounds.Width > 0 ? c.Bounds.Width : Bounds.Width > 0 ? Bounds.Width : 200;
        var h = c.Bounds.Height > 0 ? c.Bounds.Height : Bounds.Height > 0 ? Bounds.Height : 60;
        return d switch
        {
            AnimationDirection.Left  => (-w,  0),
            AnimationDirection.Right => ( w,  0),
            AnimationDirection.Up    => ( 0, -h),
            AnimationDirection.Down  => ( 0,  h),
            _                        => ( 0,  0)
        };
    }

    private void RunSlide(
        AnimatedItemContainer container,
        AnimationDirection direction,
        bool slidingIn,
        TimeSpan duration,
        Action? onComplete = null,
        Action? onCancelled = null,
        CancellationToken cancellation = default)
    {
        var (offX, offY) = OffscreenOffset(container, direction);
        var (fromX, fromY, toX, toY) = slidingIn
            ? (offX, offY, 0d, 0d)
            : (0d, 0d, offX, offY);

        // Snap to start immediately — prevents a one-frame flash at the wrong position.
        container.Translate.X = fromX;
        container.Translate.Y = fromY;

        RunAnimation(
            container,
            container.Translate,
            BuildTranslateAnimation(fromX, fromY, toX, toY, duration),
            onComplete: () =>
            {
                container.Translate.X = toX;
                container.Translate.Y = toY;
                onComplete?.Invoke();
            },
            onCancelled,
            cancellation);
    }

    private void RunFade(
        AnimatedItemContainer container,
        double fromOpacity,
        double toOpacity,
        TimeSpan duration,
        Action? onComplete = null,
        Action? onCancelled = null,
        CancellationToken cancellation = default)
    {
        container.Opacity = fromOpacity;

        RunAnimation(
            container,
            container,
            BuildOpacityAnimation(fromOpacity, toOpacity, duration),
            onComplete: () =>
            {
                container.Opacity = toOpacity;
                onComplete?.Invoke();
            },
            onCancelled,
            cancellation);
    }

    /// <summary>
    /// Runs animation on target, tracked against container for cancellation.
    ///
    /// GATE MECHANISM: increments _inflightCount before starting, decrements in ContinueWith
    /// on both the complete and cancelled paths. When the count reaches zero,
    /// OnAllAnimationsComplete releases the pipeline gate. This means the gate releases
    /// exactly when the last animation of the current batch finishes — no timer, no guessing.
    ///
    /// COMPARE-BEFORE-REMOVE: only removes the _activeEntries entry if it's still the one
    /// this animation registered. Prevents a stale ContinueWith from a recycled container's
    /// prior animation clobbering a newer animation's registration.
    /// </summary>
    private void RunAnimation(
        AnimatedItemContainer container,
        Animatable target,
        Animation animation,
        Action onComplete,
        Action? onCancelled,
        CancellationToken cancellation)
    {
        var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _activeEntries[container] = localCts;

        var capturedGeneration = _batchGeneration;
        _inflightCount++;

        _ = animation.RunAsync(target, localCts.Token).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Compare-before-remove: only clean up if this is still the active animation.
                if (_activeEntries.TryGetValue(container, out var registered) && registered == localCts)
                {
                    _activeEntries.Remove(container);
                    localCts.Dispose();
                }

                if (localCts.IsCancellationRequested)
                    onCancelled?.Invoke();
                else
                    onComplete();

                // Decrement counter and release gate if this was the last animation.
                _inflightCount--;
                if (_inflightCount == 0)
                    OnAllAnimationsComplete(capturedGeneration);
            });
        }, TaskScheduler.Default);
    }

    // -------------------------------------------------------------------------
    // Animation builders
    // -------------------------------------------------------------------------

    private static Animation BuildTranslateAnimation(
        double fromX, double fromY, double toX, double toY, TimeSpan duration)
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
                        new Setter(TranslateTransform.XProperty, fromX),
                        new Setter(TranslateTransform.YProperty, fromY)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, toX),
                        new Setter(TranslateTransform.YProperty, toY)
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

    // -------------------------------------------------------------------------
    // Protected animation accessor for subclasses
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs an animation on a container's TranslateTransform, tracked by the inflight counter.
    /// Intended for FLIP animations in OrderedAnimatedListBox so they participate in the
    /// pipeline gate and don't release it prematurely.
    /// </summary>
    protected void RunFlipAnimation(
        AnimatedItemContainer container,
        Animation animation,
        Action onComplete,
        Action onCancelled)
    {
        RunAnimation(container, container.Translate, animation, onComplete, onCancelled, default);
    }

    // -------------------------------------------------------------------------
    // Pagination commands
    // -------------------------------------------------------------------------

    private void GoToNextPage()
    {
        if (CurrentPage < PageCount - 1) CurrentPage++;
    }

    private void GoToPreviousPage()
    {
        if (CurrentPage > 0) CurrentPage--;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

    private sealed class ExitAnimationHandle
    {
        private readonly CancellationTokenSource _cts;
        public object Item { get; }
        public AnimatedItemContainer Container { get; }

        public ExitAnimationHandle(object item, AnimatedItemContainer container, CancellationTokenSource cts)
        {
            Item = item;
            Container = container;
            _cts = cts;
        }

        public void Cancel()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => _canExecute();
        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() =>
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
