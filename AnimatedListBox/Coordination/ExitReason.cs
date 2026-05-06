namespace YourApp.Controls;

public enum ExitReason
{
    /// <summary>Item disappeared with no registered intent. Fade out.</summary>
    Default,

    /// <summary>Item is being sent to a sibling list. Slide out toward sibling.</summary>
    ToSibling,

    /// <summary>Item is leaving toward the next page (higher page index). Slide out bottom edge.</summary>
    ToNextPage,

    /// <summary>Item is leaving toward the previous page (lower page index). Slide out top edge.</summary>
    ToPreviousPage,
}
