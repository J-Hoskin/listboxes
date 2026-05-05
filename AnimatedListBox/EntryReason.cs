namespace YourApp.Controls;

public enum EntryReason
{
    /// <summary>Item appeared with no registered intent. Fade in.</summary>
    Default,

    /// <summary>Item was sent here from a sibling list. Slide in from the configured direction.</summary>
    FromSibling,

    /// <summary>Item arrived from the next page (higher page index). Slide up from bottom edge.</summary>
    FromNextPage,

    /// <summary>Item arrived from the previous page (lower page index). Slide down from top edge.</summary>
    FromPreviousPage,
}
