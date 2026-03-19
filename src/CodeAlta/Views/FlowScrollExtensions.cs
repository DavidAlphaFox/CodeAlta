using XenoAtom.Terminal.UI.Controls;

internal static class FlowScrollExtensions
{
    internal static void ScrollToTailIfFollowing(this DocumentFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        if (flow.FollowTail)
        {
            flow.ScrollToTail();
        }
    }
}
