using XenoAtom.Terminal.UI.Controls;

internal static class FlowScrollExtensions
{
    internal static void ScrollToTailIfFollowing(this DocumentFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        //if (flow.FollowTail) // We have to disable this for now, as it can happen that adding dynamically elements to the last item in a document flow will not keep the follow tail, and so we are no longer scrolling to the end correctly. This will need to be fixed in the XenoAtom.Terminal.UI
        {
            flow.ScrollToTail();
        }
    }
}
