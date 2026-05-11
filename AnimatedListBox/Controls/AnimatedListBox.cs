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
///   The pipeline gate is driven by _inflightCount — releases only when every animation
///   in the current batch has completed or been cancelled. No timers.
///
///   Transfers    → jump the queue, snap in-flight animations, play immediately.
///   OrderChanges → collapse to latest state (only the final order animates).
///   AddRemoves   → collapse to latest state (bulk add/remove animates as one batch).
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

    /// <summary>
    /// Bind this OneWay from your VM to programmatically select an item.
    /// The control jumps to the correct page before setting SelectedItem so the item
    /// is guaranteed to be in VisibleItems when ListBox resolves the selection.
    /// For user-click selection flowing back to the VM, bind SelectedItem OneWayToSource.
    /// </summary>
    public static readonly StyledProperty<object?> ProgrammaticSelectedItemProperty =
        AvaloniaProperty.Register<AnimatedListBox, object?>(nameof(ProgrammaticSelectedItem));

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

    public object? ProgrammaticSelectedItem
    {
        get => GetValue(ProgrammaticSelectedItemProperty);
        set => SetValue(ProgrammaticSelectedItemProperty, value);
    }

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
    /// Counts animations currently in flight for the active batch. The pipeline gate releases
    /// when this reaches zero. Incremented before RunAsync, decremented in ContinueWith on
    /// both complete and cancelled paths.
    /// </summary>
    private int _inflightCount;

    /// <summary>
    /// True while a batch is being processed. No new snapshot is dequeued until false.
    /// </summary>
    private bool _pipelineBusy;

    /// <summary>
    /// Incremented on page navigation and source rebind. Stale OnAllAnimationsComplete
    /// callbacks check this and skip releasing the gate if the generation has changed.
    /// </summary>
    private int _batchGeneration;

    /// <summary>
    /// The animation pipeline queue. Transfers jump it and clear it. OrderChanges and
    /// AddRemoves collapse — only the latest snapshot of each type is kept.
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
        ProgrammaticSelectedItemProperty.Changed.AddClassHandler<AnimatedListBox>(
            (o, e) => o.OnProgrammaticSelectedItemChanged(e));
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

        if (_activeEntries.TryGetValue(animated, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
            _activeEntries.Remove(animated);
        }

        // Read pending FLIP delta BEFORE ResetVisuals clears it.
        var flipDelta = animated.PendingFlipDelta;
        animated.ResetVisuals();

        // Defensively clear any exit for this item. The pipeline gate normally prevents
        // re-entry before exit completes, but this guards against edge cases.
        if (_activeExits.TryGetValue(ai.Id, out var exit))
        {
            exit.Cancel();
            _activeExits.Remove(ai.Id);
        }

        if (flipDelta.HasValue)
        {
            // Container was repositioned during DiffVisible — play FLIP instead of entry.
            OnPendingFlipDelta(animated, flipDelta.Value.X, flipDelta.Value.Y);
        }
        else
        {
            var reason = Coordinator?.ConsumeEntryReason(ai.Id) ?? EntryReason.Default;
            StartEntryAnimation(animated, reason);
        }
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

        var full = MaterializeFullItems();

        // Consume and discard any entry reasons stamped for items in the new source.
        // Stamps belong to the old source context — a transfer that was in-flight when
        // FullItems was rebound should not cause slide animations on initial population.
        // ConsumeEntryReason is a no-op for ids with no pending reason so this is safe
        // to call unconditionally for every item in the new source.
        if (Coordinator != null)
            foreach (var item in full.OfType<IAnimatedItem>())
                Coordinator.ConsumeEntryReason(item.Id);

        ReplaceVisible(BuildPageSlice(full));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var type = ClassifyChange(e);
        var full = MaterializeFullItems();

        UpdatePageCount(full);

        var pageSlice = BuildPageSlice(full);

        // Consume and discard entry reasons for items NOT on the current page.
        // A transfer stamp is only meaningful if the item is landing on the visible page
        // right now. If it lands on a different page, the stamp would otherwise sit in
        // the coordinator dictionary and incorrectly classify a future page navigation
        // as a transfer, causing a slide animation instead of a fade.
        if (Coordinator != null)
        {
            var pageIds = pageSlice.OfType<IAnimatedItem>().Select(x => x.Id).ToHashSet();
            foreach (var item in full.OfType<IAnimatedItem>())
                if (!pageIds.Contains(item.Id))
                    Coordinator.ConsumeEntryReason(item.Id);
        }

        var snapshot = new SourceSnapshot(pageSlice, type);

        if (type == ChangeType.Transfer)
        {
            // Transfers jump the queue. Snap whatever is running, clear the entire queue
            // (current state is the source of truth), and process immediately.
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
        else
        {
            // OrderChange and AddRemove both collapse — replace the last queued snapshot
            // of the same type with the newer one. Only the latest desired state matters.
            var items = _pipeline.ToList();
            var lastIdx = items.FindLastIndex(s => s.Type == type);

            if (lastIdx >= 0)
                items[lastIdx] = snapshot;
            else
                items.Add(snapshot);

            _pipeline.Clear();
            foreach (var s in items) _pipeline.Enqueue(s);
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

    /// <summary>
    /// Handles programmatic selection from the VM. Jumps to the correct page first so
    /// the item is in VisibleItems before ListBox tries to resolve the selection.
    /// Bind ProgrammaticSelectedItem OneWay from your VM property.
    /// Bind SelectedItem OneWayToSource to capture user-click selection back to the VM.
    /// </summary>
    private void OnProgrammaticSelectedItemChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not IAnimatedItem ai)
        {
            SelectedItem = null;
            return;
        }

        var full = MaterializeFullItems();
        var index = full.OfType<IAnimatedItem>().ToList().FindIndex(x => x.Id == ai.Id);

        if (index < 0)
        {
            // Item does not exist in this list — clear selection silently.
            SelectedItem = null;
            return;
        }

        var pageSize = Math.Max(1, PageSize);
        var targetPage = index / pageSize;

        if (targetPage != CurrentPage)
        {
            // Jump to the page containing the item. OnCurrentPageChanged fires
            // synchronously and calls ReplaceVisible, putting the item in VisibleItems
            // before we set SelectedItem below.
            CurrentPage = targetPage;
        }

        // Item is now in VisibleItems — ListBox can find and select it.
        SelectedItem = e.NewValue;
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
        OnBeforeAnimatedDiff();
        DiffVisible(snapshot.PageItems);

        // If DiffVisible started no animations (no-op diff), release gate immediately.
        if (_inflightCount == 0)
        {
            _pipelineBusy = false;
            TryProcessNext();
        }
        // Otherwise gate releases in OnAllAnimationsComplete when _inflightCount reaches 0.
    }

    private void OnAllAnimationsComplete(int capturedGeneration)
    {
        if (capturedGeneration != _batchGeneration) return;

        _pipelineBusy = false;
        TryProcessNext();
    }

    // -------------------------------------------------------------------------
    // Snap
    // -------------------------------------------------------------------------

    protected void SnapAllAnimations()
    {
        foreach (var (container, cts) in _activeEntries)
        {
            cts.Cancel();
            cts.Dispose();
            container.ResetVisuals();
        }
        _activeEntries.Clear();

        foreach (var (id, handle) in _activeExits)
        {
            handle.Cancel();
            var idx = IndexOfById(id);
            if (idx >= 0) VisibleItems.RemoveAt(idx);
        }
        _activeExits.Clear();

        OnSnapAllAnimations();
    }

    // -------------------------------------------------------------------------
    // Virtual hooks for subclasses
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by ProcessSnapshot immediately before DiffVisible.
    /// OrderedAnimatedListBox uses this to capture pre-layout bounds for FLIP.
    /// </summary>
    protected virtual void OnBeforeAnimatedDiff() { }

    /// <summary>
    /// Called at the end of SnapAllAnimations.
    /// OrderedAnimatedListBox uses this to invalidate pending FLIP state.
    /// </summary>
    protected virtual void OnSnapAllAnimations() { }

    /// <summary>
    /// Called by PrepareContainerForItemOverride when a container has a pending FLIP delta.
    /// Happens when a container is repositioned by DiffVisible before ApplyFlip fires.
    /// Base implementation snaps to final position. OrderedAnimatedListBox animates from it.
    /// </summary>
    protected virtual void OnPendingFlipDelta(AnimatedItemContainer container, double dx, double dy)
    {
        container.Translate.X = 0;
        container.Translate.Y = 0;
    }

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

        // Remove items no longer desired, skipping those already mid-exit.
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
    /// Finds the correct insert position for targetIndex, treating ghost items (mid-exit)
    /// as non-existent so they don't displace incoming items.
    /// </summary>
    private int ResolveInsertIndex(int targetIndex)
    {
        int desiredPos = 0;
        for (int i = 0; i < VisibleItems.Count; i++)
        {
            if (VisibleItems[i] is IAnimatedItem ai && _activeExits.ContainsKey(ai.Id))
                continue;

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
    /// Transfer: detected via HasAnyPendingReason(). Stamps exist only for the duration
    /// of a single synchronous cache.Edit call, so there is no false-positive window.
    ///
    /// OrderChange: DynamicData's .Sort() raises Move for sort-order changes.
    /// AddRemove: everything else (Add, Remove, Replace, Reset).
    /// </summary>
    private ChangeType ClassifyChange(NotifyCollectionChangedEventArgs e)
    {
        if (Coordinator?.HasAnyPendingReason() == true)
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

            // Guard: only remove if this exit is still the registered one.
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

        container.Translate.X = fromX;
        container.Translate.Y = fromY;

        // Exit animations hold their final offscreen position via FillMode.Forward to
        // prevent a one-frame snap back before the container is removed from VisibleItems.
        // Entry animations use FillMode.None — the explicit snap in onComplete is sufficient.
        var fillMode = slidingIn ? FillMode.None : FillMode.Forward;

        RunAnimation(
            container,
            container.Translate,
            BuildTranslateAnimation(fromX, fromY, toX, toY, duration, fillMode),
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

        // Same FillMode logic as RunSlide — exits hold, entries don't need to.
        var fillMode = toOpacity == 0 ? FillMode.Forward : FillMode.None;

        RunAnimation(
            container,
            container,
            BuildOpacityAnimation(fromOpacity, toOpacity, duration, fillMode),
            onComplete: () =>
            {
                container.Opacity = toOpacity;
                onComplete?.Invoke();
            },
            onCancelled,
            cancellation);
    }

    /// <summary>
    /// Core animation runner. Protected so subclasses (OrderedAnimatedListBox) can route
    /// FLIP animations through it and participate correctly in the _inflightCount gate.
    ///
    /// GATE: increments _inflightCount before RunAsync, decrements in ContinueWith on both
    /// complete and cancelled paths. OnAllAnimationsComplete fires when count hits zero.
    ///
    /// COMPARE-BEFORE-REMOVE: only removes the _activeEntries registration if it's still
    /// the one this animation owns, preventing stale continuations from clobbering newer
    /// animations on recycled containers.
    /// </summary>
    protected void RunAnimation(
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
                if (_activeEntries.TryGetValue(container, out var registered) && registered == localCts)
                {
                    _activeEntries.Remove(container);
                    localCts.Dispose();
                }

                if (localCts.IsCancellationRequested)
                    onCancelled?.Invoke();
                else
                    onComplete();

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
        double fromX, double fromY, double toX, double toY,
        TimeSpan duration, FillMode fillMode = FillMode.None)
        => new()
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            FillMode = fillMode,
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

    private static Animation BuildOpacityAnimation(
        double from, double to, TimeSpan duration, FillMode fillMode = FillMode.None)
        => new()
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            FillMode = fillMode,
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

        public void RaiseCanExecuteChanged()
        {
            if (Dispatcher.UIThread.CheckAccess())
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
