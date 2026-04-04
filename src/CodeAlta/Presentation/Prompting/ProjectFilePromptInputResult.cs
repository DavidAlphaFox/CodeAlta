using CodeAlta.Agent;

namespace CodeAlta.Presentation.Prompting;

internal sealed record ProjectFilePromptInputResult(
    string NormalizedPromptText,
    AgentInput Input);
