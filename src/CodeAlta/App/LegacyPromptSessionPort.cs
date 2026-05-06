using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal sealed class LegacyPromptSessionPort : IPromptSessionPort
{
    private readonly IFrontendUiScheduler _uiScheduler;
    private readonly Func<bool> _isPromptEmpty;
    private readonly Action _clearPrompt;
    private readonly Action<string> _restorePromptText;
    private readonly Func<IReadOnlyList<PromptImageAttachment>> _snapshotPromptImages;
    private readonly Action<IReadOnlyList<PromptImageAttachment>> _restorePromptImages;
    private readonly Action _updatePromptAvailability;
    private readonly Action _updatePromptAttachments;
    private readonly Dictionary<PromptSessionId, PromptSessionBinding> _bindings = new();

    public LegacyPromptSessionPort(
        IFrontendUiScheduler uiScheduler,
        Func<bool> isPromptEmpty,
        Action clearPrompt,
        Action<string> restorePromptText,
        Func<IReadOnlyList<PromptImageAttachment>> snapshotPromptImages,
        Action<IReadOnlyList<PromptImageAttachment>> restorePromptImages,
        Action? updatePromptAvailability = null,
        Action? updatePromptAttachments = null)
    {
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentNullException.ThrowIfNull(isPromptEmpty);
        ArgumentNullException.ThrowIfNull(clearPrompt);
        ArgumentNullException.ThrowIfNull(restorePromptText);
        ArgumentNullException.ThrowIfNull(snapshotPromptImages);
        ArgumentNullException.ThrowIfNull(restorePromptImages);

        _uiScheduler = uiScheduler;
        _isPromptEmpty = isPromptEmpty;
        _clearPrompt = clearPrompt;
        _restorePromptText = restorePromptText;
        _snapshotPromptImages = snapshotPromptImages;
        _restorePromptImages = restorePromptImages;
        _updatePromptAvailability = updatePromptAvailability ?? (() => { });
        _updatePromptAttachments = updatePromptAttachments ?? (() => { });
    }

    public PromptSessionSnapshot GetPromptSession(PromptSessionId promptSessionId)
    {
        ValidatePromptSessionId(promptSessionId);
        if (!_bindings.TryGetValue(promptSessionId, out var binding))
        {
            throw new KeyNotFoundException($"Prompt session '{promptSessionId.Value}' is not bound.");
        }

        return new PromptSessionSnapshot(binding, IsPromptEmpty(promptSessionId));
    }

    public PromptSubmission CapturePrompt(PromptSessionId promptSessionId, string? submittedText)
    {
        ValidatePromptSessionId(promptSessionId);
        return _uiScheduler.Invoke(() => PromptSubmission.Create(submittedText, _snapshotPromptImages()));
    }

    public bool IsPromptEmpty(PromptSessionId promptSessionId)
    {
        ValidatePromptSessionId(promptSessionId);
        return _uiScheduler.Invoke(_isPromptEmpty);
    }

    public void BindPromptSession(PromptSessionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _bindings[binding.PromptSessionId] = binding;
    }

    public void ClearPrompt(PromptSessionId promptSessionId)
    {
        ValidatePromptSessionId(promptSessionId);
        _uiScheduler.Invoke(_clearPrompt);
    }

    public void RestorePrompt(PromptSessionId promptSessionId, PromptSubmission prompt)
    {
        ValidatePromptSessionId(promptSessionId);
        ArgumentNullException.ThrowIfNull(prompt);
        _uiScheduler.Invoke(() =>
        {
            _restorePromptText(prompt.Text);
            _restorePromptImages(prompt.Images);
        });
    }

    public void UpdatePromptAvailability(PromptSessionId promptSessionId)
    {
        ValidatePromptSessionId(promptSessionId);
        _uiScheduler.Invoke(_updatePromptAvailability);
    }

    public void UpdatePromptAttachments(PromptSessionId promptSessionId)
    {
        ValidatePromptSessionId(promptSessionId);
        _uiScheduler.Invoke(_updatePromptAttachments);
    }

    private static void ValidatePromptSessionId(PromptSessionId promptSessionId)
    {
        if (promptSessionId.IsEmpty)
        {
            throw new ArgumentException("Prompt session id cannot be empty.", nameof(promptSessionId));
        }
    }
}
