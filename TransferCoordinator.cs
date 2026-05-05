using System;
using System.Collections.Concurrent;

namespace YourApp.Controls;

/// <summary>
/// Default coordinator implementation. Derive your parent view-model from this class,
/// or implement ITransferCoordinator directly.
///
/// Usage: call StampTransfer(id, exit, entry) immediately before mutating the cache.
/// DynamicData propagates the change synchronously on the UI thread, so both controls
/// will see the stamped reasons when their CollectionChanged handlers fire.
/// </summary>
public class TransferCoordinator : ITransferCoordinator
{
    private readonly ConcurrentDictionary<Guid, EntryReason> _entries = new();
    private readonly ConcurrentDictionary<Guid, ExitReason> _exits = new();

    public void StampEntry(Guid itemId, EntryReason reason) => _entries[itemId] = reason;

    public void StampExit(Guid itemId, ExitReason reason) => _exits[itemId] = reason;

    /// <summary>
    /// Stamp both sides of a transfer in one call. Must be called before cache.Edit.
    /// </summary>
    public void StampTransfer(Guid itemId, ExitReason exit, EntryReason entry)
    {
        _exits[itemId] = exit;
        _entries[itemId] = entry;
    }

    public EntryReason ConsumeEntryReason(Guid itemId)
        => _entries.TryRemove(itemId, out var r) ? r : EntryReason.Default;

    public ExitReason ConsumeExitReason(Guid itemId)
        => _exits.TryRemove(itemId, out var r) ? r : ExitReason.Default;

    /// <summary>
    /// Returns true if any pending reason exists for any item.
    /// Does not consume. Used by AnimatedListBox.ClassifyChange.
    /// </summary>
    public bool HasAnyPendingReason() => !_entries.IsEmpty || !_exits.IsEmpty;
}
