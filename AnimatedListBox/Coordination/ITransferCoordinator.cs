using System;

namespace YourApp.Controls;

/// <summary>
/// Implemented by the shared parent view-model. AnimatedListBox queries this when
/// items appear/disappear to know what animation to play.
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
}
