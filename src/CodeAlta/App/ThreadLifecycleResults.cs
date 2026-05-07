namespace CodeAlta.App;

internal enum OpenThreadResult
{
    Opened,
    NotFound,
    AlreadyOpen,
}

internal enum SelectionChangeResult
{
    Changed,
    Unchanged,
}

internal enum TabCloseResult
{
    Closed,
    NotOpen,
}
