namespace YourApp.Controls;

public enum EntryReason
{
    /// <summary>Default — item appeared with no registered intent. Fade in.</summary>
    Default,

    /// <summary>Item was sent here from a sibling list. Slide in from the configured direction.</summary>
    FromSibling,

    /// <summary>Item moved into the current page from another page due to an order change. Slide from page-edge.</summary>
    FromOtherPage
}
