# Animated ListBox — File Layout

```
AnimatedListBox/
├── Controls/
│   ├── AnimatedListBox.cs              ← Base control
│   ├── OrderedAnimatedListBox.cs       ← Adds FLIP reorder + page-edge animations
│   └── AnimatedItemContainer.cs        ← Custom ListBoxItem with TranslateTransform
├── Coordination/
│   ├── ITransferCoordinator.cs         ← Interface implemented by the parent VM
│   ├── TransferCoordinator.cs          ← Default base implementation
│   ├── EntryReason.cs                  ← Enum
│   ├── ExitReason.cs                   ← Enum
│   └── AnimationDirection.cs           ← Enum
├── ViewModels/
│   └── IAnimatedItem.cs                ← IAnimatedItem + IOrderedAnimatedItem
└── Examples/
    ├── TwoListParentViewModel.cs       ← Example wiring (adapt to your real VMs)
    └── TwoListView.axaml               ← Example XAML
```

## Integration checklist

1. **Replace the namespace** `YourApp` everywhere with your actual project namespace. The files use:
   - `YourApp.Controls` for the controls + coordination types
   - `YourApp.ViewModels` for `IAnimatedItem` / `IOrderedAnimatedItem`
   - `YourApp.Examples` for the example VM/view

2. **Make your wrapper VMs implement `IAnimatedItem`** (or `IOrderedAnimatedItem` if you want the order-aware list). The `Id` should come from the underlying *model*, not the wrapper, so it stays stable when DynamicData re-creates the wrapper after a filter change.

3. **Hook the parent VM into the cache mutation flow**. Before flipping the toggle that moves an item from one filtered list to the other, call `StampTransfer(id, ExitReason.ToSibling, EntryReason.FromSibling)` on the coordinator. The example `TwoListParentViewModel` shows this pattern.

4. **For order-driven cross-page transitions** (an item's Order changes such that it leaves the visible page or arrives onto the visible page), stamp `ExitReason.ToOtherPage` / `EntryReason.FromOtherPage` from whichever service mutates the order, just before the mutation propagates. Same-page reorders need no stamping — the FLIP logic handles them automatically.

5. **`INotifyPropertyChanged`** isn't strictly required on the wrapper VMs at the property level, but the *collection* you pass to `FullItems` must implement `INotifyCollectionChanged` so the control sees changes. The DynamicData `Bind(out var bindable)` extension produces a `ReadOnlyObservableCollection<T>` which already implements this.

6. **DynamicData `.Sort()`** raises Move events when items reorder, so the control's `OnFullItemsCollectionChanged` will pick them up. This is why `OrderedAnimatedListBox.MaterializeFullItems()` re-sorts defensively — it doesn't rely on the source already being sorted, but if you sort it, behaviour stays correct.

7. **One known thing to verify in your build**: the `Animation.RunAsync(target, token)` overload should exist in Avalonia 11.2.5. If it doesn't compile against your build, the workaround is to use `Transitions` on the container and just mutate `Translate.X` / `Translate.Y` directly. Tell me and I'll rework it.

## How the recycling guarantee works

1. `PrepareContainerForItemOverride` runs on the UI thread when a container is bound to a new item. It cancels any in-flight animation on that container and synchronously calls `ResetVisuals()` (which sets translate to (0,0) and opacity to 1) before the new entry animation starts.

2. `ClearContainerForItemOverride` does the same on the way out — cancels in-flight animations and resets visuals before the container is recycled.

3. Every animation completion callback explicitly assigns the final value to `Translate.X/Y` or `Opacity`, defending against any sub-pixel drift from interpolation.

4. All transform mutations use `RenderTransform` (a single `TranslateTransform`), never layout-affecting properties. So the layout system and the animation system don't interfere with each other.

5. Exit animations keep the item in `VisibleItems` for their duration. Once complete, the item is removed and Avalonia's container pool reclaims the container — at which point clear/prepare guarantees clean state for the next use.
