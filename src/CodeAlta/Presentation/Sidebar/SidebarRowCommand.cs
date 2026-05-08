namespace CodeAlta.Presentation.Sidebar;

internal abstract record SidebarRowCommand
{
    private SidebarRowCommand()
    {
    }

    public sealed record DeleteThread(string ThreadId) : SidebarRowCommand;

    public sealed record DeleteProject(string ProjectId) : SidebarRowCommand;

    public sealed record OpenProjectThreads(string ProjectId) : SidebarRowCommand;

    public sealed record OpenProjectDetails(string ProjectId) : SidebarRowCommand;

    public sealed record OpenFolder : SidebarRowCommand;
}

internal interface ISidebarRowCommandDispatcher
{
    void Dispatch(SidebarRowCommand command);
}
