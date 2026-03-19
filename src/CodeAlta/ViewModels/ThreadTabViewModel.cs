internal sealed class ThreadTabViewModel
{
    public string ThreadId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? StatusMessage { get; set; }

    public CodeAltaApp.StatusTone StatusTone { get; set; } = CodeAltaApp.StatusTone.Ready;

    public bool StatusBusy { get; set; }

    public bool HasCustomStatus { get; set; }
}
