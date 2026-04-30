namespace YourApp.Controls;

public enum ExitReason
{
    /// <summary>Default — item disappeared with no registered intent. Fade out.</summary>
    Default,

    /// <summary>Item is being sent to a sibling list. Slide out toward sibling.</summary>
    ToSibling,

    /// <summary>Item is leaving the current page due to an order change. Slide toward page-edge.</summary>
    ToOtherPage
}
