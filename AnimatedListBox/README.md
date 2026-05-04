# Animated ListBox — v3

Paginated, animated ListBox pair for Avalonia 11.2.5 with a robust Option D animation pipeline.

## File layout

```
AnimatedListBox/
├── Controls/
│   ├── AnimatedListBox.cs              ← Base control
│   ├── OrderedAnimatedListBox.cs       ← Adds FLIP reorder + page-edge animations
│   └── AnimatedItemContainer.cs        ← Custom ListBoxItem with owned TranslateTransform
├── Coordination/
│   ├── ITransferCoordinator.cs         ← ITransferCoordinator + IPeekableCoordinator
│   ├── TransferCoordinator.cs          ← Default implementation (derive your parent VM from this)
│   ├── EntryReason.cs
│   ├── ExitReason.cs
│   └── AnimationDirection.cs
├── ViewModels/
│   └── IAnimatedItem.cs                ← IAnimatedItem + IOrderedAnimatedItem
└── Examples/
    ├── TwoListParentViewModel.cs        ← Example wiring with 150ms throttle
    └── TwoListView.axaml                ← Example XAML
```

## Animation pipeline (Option D)

All source changes flow through a single-entry pipeline:

```
Source change
    ↓
ClassifyChange()        Transfer | OrderChange | AddRemove
    ↓
Enqueue with rules:
  Transfer    → jump queue, snap current animation, process immediately
  OrderChange → collapse with previous queued OrderChange (keep latest only)
  AddRemove   → always enqueue, never collapse
    ↓
TryProcessNext()        gated by _pipelineBusy
    ↓
ProcessSnapshot()       OnBeforeAnimatedDiff() → DiffVisible() → gate timer
    ↓
OnBatchComplete()       releases gate, calls TryProcessNext()
```

**What this guarantees:**
- Every transfer animates, even rapid successive ones. They queue and play in order.
- Rapid order changes collapse — only the final state animates. No stacking, no lag.
- Page changes always snap — no animation, no queue. Instant.
- Nothing ever jumps to an unintended position. Every visible change is either animated or a no-op.
- Re-entry race is guarded — a stale exit completion can't remove a re-added item.
- FLIP captures are guarded by generation counter — stale captures are discarded.

## Throttling

The example parent VM uses `Throttle(150ms)` on the DynamicData source subscriptions.
This collapses rapid service-driven updates (order changes, bulk edits) before they even
reach the control. Transfers bypass the throttle entirely — they come from explicit user
commands, not the subscription.

150ms was chosen because:
- It's shorter than the 200ms animation duration, so the queue drains naturally.
- It's long enough to collapse typical burst updates from backend services.
- It's short enough to feel live for genuine order changes the user cares about.

Adjust up (250ms) if your backend fires very rapid bursts. Adjust down (100ms) if order
changes need to feel more responsive.

## Integration checklist

1. **Replace `YourApp` namespace** everywhere with your actual namespace.

2. **Make your wrapper VMs implement `IAnimatedItem`** (or `IOrderedAnimatedItem` for the
   ordered variant). The `Id` must come from the underlying model, not the wrapper, so it
   stays stable across DynamicData wrapper recreations.

3. **Derive your parent VM from `TransferCoordinator`** (or implement both
   `ITransferCoordinator` and `IPeekableCoordinator` directly). The `IPeekableCoordinator`
   interface is required for transfer detection during change classification.

4. **Call `StampTransfer` before the cache mutation**, not after. DynamicData propagates
   synchronously on `_cache.Edit`, so the stamp must be in place before `Edit` is called.

5. **Add `Throttle(150ms).ObserveOn(...)` to your DynamicData subscriptions** as shown in
   the example VM. Use `AvaloniaScheduler.Instance` or `RxApp.MainThreadScheduler` to
   ensure the `Bind` drives the `ObservableCollection` on the UI thread.

6. **Set `ClipToBounds="True"`** on each `OrderedAnimatedListBox` in XAML so items sliding
   in/out are clipped to the list bounds rather than overflowing into adjacent UI.

7. **Transfer button binding** in item templates:
   ```xml
   Command="{Binding $parent[controls:OrderedAnimatedListBox].TransferCommand}"
   CommandParameter="{Binding}"
   ```

## Duration properties

`AnimationDuration` is the global fallback. Override per phase:

```xml
<controls:OrderedAnimatedListBox
    AnimationDuration="0:0:0.2"
    EntryDuration="0:0:0.15"
    ExitDuration="0:0:0.25"
    ReorderDuration="0:0:0.3"
    ... />
```

Leave any of the three as unset to use `AnimationDuration` for that phase.

## NeedsContainerOverride

Make sure this returns `true`:

```csharp
protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
{
    recycleKey = nameof(AnimatedItemContainer);
    return true;  // ← must be true, or Avalonia tries to cast your VM to Control
}
```
