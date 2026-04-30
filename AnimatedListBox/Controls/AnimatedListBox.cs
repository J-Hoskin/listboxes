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
/// Bind <see cref="FullItems"/> (the unpaginated source). The control internally slices it
/// according to <see cref="PageSize"/> and <see cref="CurrentPage"/>.
///
/// All items must implement <see cref="IAnimatedItem"/> for stable id resolution.
///
/// Activation (click-to-transfer) is handled by buttons in the item template. Bind each
/// button's Command to TransferCommand via $parent[AnimatedListBox].TransferCommand,
/// with CommandParameter="{Binding}" to pass the item VM.
/// </summary>
public class AnimatedListBox : ListBox
{
    // --------------------------------------------------------------------
    // Styled / Direct properties
    // --------------------------------------------------------------------

    public static readonly StyledProperty<IEnumerable?> FullItemsProperty =
        AvaloniaProperty.Register<AnimatedListBox, IEnumerable?>(nameof(FullItems));

    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<AnimatedListBox, int>(nameof(PageSize), defaultValue: 4);

    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<AnimatedListBox, int>(nameof(CurrentPage), defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<AnimatedListBox, int> PageCountProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, int>(nameof(PageCount),
            o => o.PageCount);

    public static readonly StyledProperty<ITransferCoordinator?> CoordinatorProperty =
        AvaloniaProperty.Register<AnimatedListBox, ITransferCoordinator?>(nameof(Coordinator));

    public static readonly StyledProperty<AnimationDirection> EntryDirectionProperty =
        AvaloniaProperty.Register<AnimatedListBox, AnimationDirection>(
            nameof(EntryDirection), defaultValue: AnimationDirection.Left);

    public static readonly StyledProperty<AnimationDirection> ExitDirectionProperty =
        AvaloniaProperty.Register<AnimatedListBox, AnimationDirection>(
            nameof(ExitDirection), defaultValue: AnimationDirection.Right);

    // --------------------------------------------------------------------
    // Duration properties
    //
    // AnimationDuration is the global fallback.
    // EntryDuration / ExitDuration / ReorderDuration override it per-phase when non-null.
    // Leave any of the three as null to use AnimationDuration for that phase.
    //
    // Example — slower reorder, faster entry:
    //   AnimationDuration="0:0:0.2"
    //   EntryDuration="0:0:0.15"
    //   ReorderDuration="0:0:0.35"
    // --------------------------------------------------------------------

    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan>(
            nameof(AnimationDuration), defaultValue: TimeSpan.FromMilliseconds(200));

    public static readonly StyledProperty<TimeSpan?> EntryDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(EntryDuration));

    public static readonly StyledProperty<TimeSpan?> ExitDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(ExitDuration));

    public static readonly StyledProperty<TimeSpan?> ReorderDurationProperty =
        AvaloniaProperty.Register<AnimatedListBox, TimeSpan?>(nameof(ReorderDuration));

    // --------------------------------------------------------------------
    // Transfer + pagination commands
    // --------------------------------------------------------------------

    public static readonly StyledProperty<ICommand?> TransferCommandProperty =
        AvaloniaProperty.Register<AnimatedListBox, ICommand?>(nameof(TransferCommand));

    public static readonly DirectProperty<AnimatedListBox, ICommand> NextPageCommandProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, ICommand>(nameof(NextPageCommand),
            o => o.NextPageCommand);

    public static readonly DirectProperty<AnimatedListBox, ICommand> PreviousPageCommandProperty =
        AvaloniaProperty.RegisterDirect<AnimatedListBox, ICommand>(nameof(PreviousPageCommand),
            o => o.PreviousPageCommand);

    // --------------------------------------------------------------------
    // CLR wrappers
    // --------------------------------------------------------------------

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

    // --------------------------------------------------------------------
    // Resolved durations — always use these in animation code.
    // Falls back to AnimationDuration when the specific override is null.
    // --------------------------------------------------------------------

    protected TimeSpan ResolvedEntryDuration   => EntryDuration   ?? AnimationDuration;
    protected TimeSpan ResolvedExitDuration    => ExitDuration    ?? AnimationDuration;
    protected TimeSpan ResolvedReorderDuration => ReorderDuration ?? AnimationDuration;

    // --------------------------------------------------------------------
    // Internal state
    // --------------------------------------------------------------------

    /// <summary>
    /// Items currently bound to ListBox.ItemsSource — i.e. visible on the current page.
    /// We mutate this list to drive animations; ListBox observes via INotifyCollectionChanged.
    /// </summary>
    protected readonly System.Collections.ObjectModel.ObservableCollection<object> VisibleItems = new();

    /// <summary>
    /// Tracks in-flight exit animations keyed by item id. The item is kept in VisibleItems
    /// during its exit animation, then removed in the completion callback.
    /// </summary>
    private readonly Dictionary<Guid, ExitAnimationHandle> _activeExits = new();

    /// <summary>
    /// Tracks in-flight entry animations keyed by container, so we can cancel cleanly
    /// if a container is recycled mid-animation.
    /// </summary>
    private readonly Dictionary<AnimatedItemContainer, CancellationTokenSource> _activeEntries = new();

    private INotifyCollectionChanged? _observedSource;
    private bool _suppressPaginationRefresh;

    // --------------------------------------------------------------------
    // Construction
    // --------------------------------------------------------------------

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
            (o, _) => o.RefreshPagination(animate: false));
        CurrentPageProperty.Changed.AddClassHandler<AnimatedListBox>(
            (o, _) => o.OnCurrentPageChanged());
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    // --------------------------------------------------------------------
    // Container creation / recycling
    // --------------------------------------------------------------------

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new AnimatedItemContainer();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = nameof(AnimatedItemContainer);
        return false;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not AnimatedItemContainer animated || item is not IAnimatedItem ai)
            return;

        // Cancel any animation running on this container from its previous lifetime.
        if (_activeEntries.TryGetValue(animated, out var oldCts))
        {
            oldCts.Cancel();
            _activeEntries.Remove(animated);
        }

        // Snap to neutral before the new entry animation begins.
        animated.ResetVisuals();

        // Defensively abort any tracked exit for this item — shouldn't happen, but guards
        // against being asked to prepare a container for an item that's still mid-exit.
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
                _activeEntries.Remove(animated);
            }
            animated.ResetVisuals();
        }
    }

    // --------------------------------------------------------------------
    // Source change handling
    // --------------------------------------------------------------------

    private void OnFullItemsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_observedSource != null)
            _observedSource.CollectionChanged -= OnFullItemsCollectionChanged;

        _observedSource = e.NewValue as INotifyCollectionChanged;
        if (_observedSource != null)
            _observedSource.CollectionChanged += OnFullItemsCollectionChanged;

        RefreshPagination(animate: false);
    }

    private void OnFullItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshPagination(animate: true);

    private void OnCurrentPageChanged()
    {
        RefreshPagination(animate: false);
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    // --------------------------------------------------------------------
    // Pagination + visible-set diff
    // --------------------------------------------------------------------

    protected virtual void RefreshPagination(bool animate)
    {
        if (_suppressPaginationRefresh) return;

        var full = MaterializeFullItems();
        var pageSize = Math.Max(1, PageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(full.Count / (double)pageSize));

        var page = Math.Clamp(CurrentPage, 0, pageCount - 1);
        if (page != CurrentPage)
        {
            _suppressPaginationRefresh = true;
            try { CurrentPage = page; } finally { _suppressPaginationRefresh = false; }
        }

        PageCount = pageCount;

        var newVisible = full.Skip(page * pageSize).Take(pageSize).ToList();

        // Page changes are instant (no animation). Source mutations are diffed with animation.
        if (animate)
            DiffVisible(newVisible);
        else
            ReplaceVisible(newVisible);

        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Hook for subclasses. OrderedAnimatedListBox overrides to sort by Order.
    /// </summary>
    protected virtual List<object> MaterializeFullItems()
    {
        if (FullItems == null) return new();
        return FullItems.Cast<object>().ToList();
    }

    /// <summary>
    /// Instant replace — used for page changes where no animation is required.
    /// </summary>
    private void ReplaceVisible(List<object> desired)
    {
        VisibleItems.Clear();
        foreach (var item in desired)
            VisibleItems.Add(item);
    }

    /// <summary>
    /// Animated diff — used when the source collection mutates.
    /// Items removed from desired get exit animations. Items added to desired get entry
    /// animations (triggered by PrepareContainerForItemOverride). Positions are moved to match.
    /// </summary>
    private void DiffVisible(List<object> desired)
    {
        var desiredIds = desired.OfType<IAnimatedItem>().Select(x => x.Id).ToHashSet();

        // Remove items not in the desired set (skip those already mid-exit).
        for (int i = VisibleItems.Count - 1; i >= 0; i--)
        {
            if (VisibleItems[i] is not IAnimatedItem ai) continue;
            if (desiredIds.Contains(ai.Id)) continue;
            if (_activeExits.ContainsKey(ai.Id)) continue;

            BeginExit(ai, VisibleItems[i]);
        }

        // Insert / move items into their correct positions.
        for (int targetIndex = 0; targetIndex < desired.Count; targetIndex++)
        {
            var item = desired[targetIndex];
            if (item is not IAnimatedItem ai) continue;

            int currentIndex = IndexOfById(ai.Id);

            if (currentIndex < 0)
            {
                VisibleItems.Insert(ClampInsertIndex(targetIndex), item);
            }
            else if (currentIndex != targetIndex)
            {
                int moveTo = ClampInsertIndex(targetIndex);
                if (currentIndex != moveTo)
                    VisibleItems.Move(currentIndex, moveTo);
            }
        }
    }

    private int IndexOfById(Guid id)
    {
        for (int i = 0; i < VisibleItems.Count; i++)
            if (VisibleItems[i] is IAnimatedItem ai && ai.Id == id)
                return i;
        return -1;
    }

    private int ClampInsertIndex(int target) => Math.Min(target, VisibleItems.Count);

    // --------------------------------------------------------------------
    // Entry animation
    // --------------------------------------------------------------------

    private void StartEntryAnimation(AnimatedItemContainer container, EntryReason reason)
    {
        var direction = ResolveEntryDirection(reason);

        if (direction == null)
            RunFade(container, fromOpacity: 0, toOpacity: 1, ResolvedEntryDuration);
        else
            RunSlide(container, direction.Value, slidingIn: true, ResolvedEntryDuration);
    }

    /// <summary>
    /// Returns null → fade, or a direction → slide.
    /// Subclasses override to add FromOtherPage handling.
    /// </summary>
    protected virtual AnimationDirection? ResolveEntryDirection(EntryReason reason)
        => reason switch
        {
            EntryReason.FromSibling => EntryDirection,
            _ => null
        };

    // --------------------------------------------------------------------
    // Exit animation
    // --------------------------------------------------------------------

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

        Action onComplete = () =>
        {
            if (cts.IsCancellationRequested) return;
            _activeExits.Remove(ai.Id);
            VisibleItems.Remove(item);
        };

        Action onCancelled = () =>
        {
            _activeExits.Remove(ai.Id);
            container.ResetVisuals();
        };

        if (direction == null)
            RunFade(container, fromOpacity: 1, toOpacity: 0, ResolvedExitDuration, onComplete, onCancelled, cts.Token);
        else
            RunSlide(container, direction.Value, slidingIn: false, ResolvedExitDuration, onComplete, onCancelled, cts.Token);
    }

    protected virtual AnimationDirection? ResolveExitDirection(ExitReason reason)
        => reason switch
        {
            ExitReason.ToSibling => ExitDirection,
            _ => null
        };

    // --------------------------------------------------------------------
    // Animation primitives
    // --------------------------------------------------------------------

    private static (double X, double Y) OffscreenOffset(AnimatedItemContainer c, AnimationDirection d)
    {
        var w = c.Bounds.Width > 0 ? c.Bounds.Width : 200;
        var h = c.Bounds.Height > 0 ? c.Bounds.Height : 40;
        return d switch
        {
            AnimationDirection.Left  => (-w, 0),
            AnimationDirection.Right => ( w, 0),
            AnimationDirection.Up    => (0, -h),
            AnimationDirection.Down  => (0,  h),
            _                        => (0,  0)
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

        // Snap to start position immediately — prevents a one-frame flash at the wrong position.
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
    /// Runs <paramref name="animation"/> on <paramref name="target"/>, tracked against
    /// <paramref name="container"/> so it can be cancelled if the container is recycled.
    ///
    /// The compare-before-remove guard in the completion callback ensures that if the container
    /// was recycled and a new animation started before this one's continuation runs, we don't
    /// accidentally remove the new animation's registration.
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

        _ = animation.RunAsync(target, localCts.Token).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeEntries.TryGetValue(container, out var registered) && registered == localCts)
                    _activeEntries.Remove(container);

                if (localCts.IsCancellationRequested)
                    onCancelled?.Invoke();
                else
                    onComplete();
            });
        }, TaskScheduler.Default);
    }

    // --------------------------------------------------------------------
    // Animation builders
    // --------------------------------------------------------------------

    private static Animation BuildTranslateAnimation(
        double fromX, double fromY, double toX, double toY, TimeSpan duration)
        => new()
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
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
            FillMode = FillMode.Forward,
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

    // --------------------------------------------------------------------
    // Pagination commands
    // --------------------------------------------------------------------

    private void GoToNextPage()
    {
        if (CurrentPage < PageCount - 1) CurrentPage++;
    }

    private void GoToPreviousPage()
    {
        if (CurrentPage > 0) CurrentPage--;
    }

    // --------------------------------------------------------------------
    // Private helpers
    // --------------------------------------------------------------------

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

        public void Cancel() => _cts.Cancel();
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
