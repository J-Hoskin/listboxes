using System;

namespace YourApp.Controls;

/// <summary>
/// Implemented by the shared parent view-model. AnimatedListBox queries this when
/// items appear/disappear to know what animation to play, and to detect transfers
/// during change classification.
/// </summary>
public interface ITransferCoordinator
{
    /// <summary>
    /// Resolves and consumes the entry reason for the given item id.
    /// One-shot: subsequent calls return Default.
    /// </summary>
    EntryReason ConsumeEntryReason(Guid itemId);

    /// <summary>
    /// Resolves and consumes the exit reason for the given item id.
    /// One-shot.
    /// </summary>
    ExitReason ConsumeExitReason(Guid itemId);

    /// <summary>
    /// Returns true if ANY pending entry or exit reason exists in the coordinator.
    /// Must NOT consume any reasons — only inspect.
    /// Used by ClassifyChange to identify transfer events before animations run.
    /// Safe because stamps exist only for the duration of a single synchronous
    /// cache.Edit call on the UI thread — no risk of false positives.
    /// </summary>
    bool HasAnyPendingReason();
}
