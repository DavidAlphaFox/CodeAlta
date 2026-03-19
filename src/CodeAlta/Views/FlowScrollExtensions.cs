using XenoAtom.Terminal.UI.Controls;

internal static class FlowScrollExtensions
{
    internal static void ScrollToTailIfFollowing(this DocumentFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        // We cannot rely on FollowTail for now. DocumentFlow does not always update it
        // correctly when the tail item is mutated dynamically, so we keep the existing
        // unconditional scroll workaround until XenoAtom.Terminal.UI is fixed upstream.
        {
            flow.ScrollToTail();
        }
    }
}
