using System;
using System.Collections.Concurrent;

namespace YourApp.Controls;

/// <summary>
/// Default coordinator. Your parent view-model can either derive from this or implement
/// ITransferCoordinator directly.
///
/// Usage: before mutating the cache to move an item between filtered lists, call
/// StampTransfer(itemId, ExitReason.ToSibling, EntryReason.FromSibling). The animations on
/// both controls will resolve their reasons when the change propagates.
/// </summary>
public class TransferCoordinator : ITransferCoordinator
{
    private readonly ConcurrentDictionary<Guid, EntryReason> _entries = new();
    private readonly ConcurrentDictionary<Guid, ExitReason> _exits = new();

    public void StampEntry(Guid itemId, EntryReason reason) => _entries[itemId] = reason;

    public void StampExit(Guid itemId, ExitReason reason) => _exits[itemId] = reason;

    public void StampTransfer(Guid itemId, ExitReason exit, EntryReason entry)
    {
        _exits[itemId] = exit;
        _entries[itemId] = entry;
    }

    public EntryReason ConsumeEntryReason(Guid itemId)
        => _entries.TryRemove(itemId, out var r) ? r : EntryReason.Default;

    public ExitReason ConsumeExitReason(Guid itemId)
        => _exits.TryRemove(itemId, out var r) ? r : ExitReason.Default;
}
