namespace CodeAlta.Agent.OpenAI.Codex;

internal sealed class CodexTurnState
{
    private readonly object _gate = new();
    private string? _capturedState;

    public bool TryGetCapturedState(out string state)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_capturedState))
            {
                state = string.Empty;
                return false;
            }

            state = _capturedState;
            return true;
        }
    }

    public void Capture(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        lock (_gate)
        {
            _capturedState ??= state;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _capturedState = null;
        }
    }
}
