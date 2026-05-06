using ReactiveUI;

namespace YourApp.Services;

/// <summary>
/// Shared selection service. A single instance is shared across all view models
/// that display items from the same source cache. When any list selects an item,
/// it updates SelectedModel here. All subscribed view models react and update their
/// own SelectedItem wrapper (or clear it if the item isn't in their list).
///
/// Inject this as a singleton via your DI container.
/// </summary>
public class SelectionService : ReactiveObject
{
    private ItemModel? _selectedModel;

    public ItemModel? SelectedModel
    {
        get => _selectedModel;
        set => this.RaiseAndSetIfChanged(ref _selectedModel, value);
    }
}
