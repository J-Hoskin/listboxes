# AnimatedListBox Tests

## Setup required in your test project

### 1. Assembly attribute (once per test project)
```csharp
// TestSetup.cs or AssemblyInfo.cs
[assembly: AvaloniaTestApplication(typeof(TestApp))]
```

### 2. TestApp (you said you already have this)
```csharp
public class TestApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

### 3. ReactiveUI scheduler
Each test class sets this in a base or inline:
```csharp
RxApp.MainThreadScheduler = Scheduler.CurrentThread;
```
All test helpers in this suite do this inline via `MakeSetup()`.

### 4. NuGet packages needed in test project
- `Avalonia.Headless.XUnit`
- `xunit`
- `Moq` (available if needed — not used in current suite, pure API testing)
- `ReactiveUI` (already in main project, shared transitively)

## Test files

| File | What it covers |
|------|----------------|
| `TransferCoordinatorTests.cs` | Stamp, consume, peek, one-shot, multi-item |
| `AnimatedListBoxPaginationTests.cs` | Initial population, page nav, commands, CanExecute, source mutations, rebind |
| `AnimatedListBoxPipelineTests.cs` | Change classification, transfer queue-jump, AddRemove collapse, snap |
| `AnimatedListBoxSelectionTests.cs` | ProgrammaticSelectedItem page jump, null, clearing, page boundary |
| `OrderedAnimatedListBoxTests.cs` | Order-based display, order changes, page-edge items, rapid changes |
| `AnimatedItemContainerTests.cs` | ResetVisuals, constructor state, ZIndex, HitTest, PendingFlipDelta |
| `GhostItemTests.cs` | Ghost insert position, re-entry race, bulk remove/add, source swap with ghost |

## Notes

### Animation duration is zero
`TestAnimatedListBox` and `TestOrderedAnimatedListBox` set `AnimationDuration = TimeSpan.Zero`
so tests don't need real timers. Animations still go through the full pipeline and completion
callbacks fire — they just complete in the same dispatcher tick.

### SnapAllAnimations
Many tests call `lb.SnapAllAnimations()` after mutations. This forces any in-flight exit
animations (ghosts) to complete immediately. Without it, removed items linger in `VisiblePage`
for the duration of the exit animation (zero ms, but still async via `ContinueWith`).
The pattern is:
```csharp
source.Remove(item);
await Dispatcher.UIThread.InvokeAsync(() => { }); // let pipeline start
lb.SnapAllAnimations();                            // force exit complete
await Dispatcher.UIThread.InvokeAsync(() => { }); // let cleanup post fire
```

### What isn't tested
- Actual visual animation (translate values, opacity values mid-animation) — not meaningful headless
- FLIP pixel positions — layout doesn't fully run in headless without a window
- Z-index layering during swap — same reason
- Transfer animation overlap timing between two lists — would require two controls rendered together

These are better covered by manual visual testing or screenshot-comparison tests with a
full Avalonia window, which is outside the scope of this suite.
