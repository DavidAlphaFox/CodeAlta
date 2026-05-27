using System.Collections.Frozen;
using System.Text;
using CodeAlta.Agent;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Prompts;

/// <summary>
/// Materializes headless session-view prompt text and attachments into backend and plugin input shapes.
/// </summary>
public sealed class HeadlessPromptAttachmentService
{
    private static readonly FrozenSet<string> ImageExtensions = new[]
    {
        ".apng",
        ".avif",
        ".bmp",
        ".gif",
        ".jpeg",
        ".jpg",
        ".png",
        ".svg",
        ".webp",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Materializes prompt text and attachments into agent input and plugin prompt attachments.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="attachments">The headless prompt attachments.</param>
    /// <returns>The materialized prompt input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prompt"/> or <paramref name="attachments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when an attachment cannot be materialized from its supplied metadata.</exception>
    public HeadlessPromptMaterializationResult Materialize(
        string prompt,
        IReadOnlyList<SessionPromptAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(attachments);

        var inputItems = new List<AgentInputItem>(attachments.Count + 1)
        {
            new AgentInputItem.Text(prompt),
        };
        var pluginAttachments = new List<PluginPromptAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            ValidateAttachmentId(attachment);
            var kind = ResolveKind(attachment);
            var pluginKind = ToPluginKind(kind);
            pluginAttachments.Add(new PluginPromptAttachment
            {
                Kind = pluginKind,
                Path = attachment.Path,
                DisplayName = attachment.DisplayName,
                Text = GetTextContentOrNull(attachment),
                MediaType = attachment.ContentType,
                Metadata = CreateMetadata(attachment),
            });

            var inputItem = CreateInputItem(attachment, kind);
            if (inputItem is not null)
            {
                inputItems.Add(inputItem);
            }
        }

        return new HeadlessPromptMaterializationResult(
            new AgentInput(inputItems),
            pluginAttachments);
    }

    private static AgentInputItem? CreateInputItem(SessionPromptAttachment attachment, SessionPromptAttachmentKind kind)
        => kind switch
        {
            SessionPromptAttachmentKind.Text => new AgentInputItem.Text(RequireTextContent(attachment)),
            SessionPromptAttachmentKind.File => new AgentInputItem.File(RequirePath(attachment), attachment.DisplayName, attachment.LineRange),
            SessionPromptAttachmentKind.Directory => new AgentInputItem.Directory(RequirePath(attachment), attachment.DisplayName, attachment.LineRange),
            SessionPromptAttachmentKind.Image => CreateImageInputItem(attachment),
            SessionPromptAttachmentKind.Selection => new AgentInputItem.Selection(
                RequirePath(attachment),
                attachment.DisplayName ?? Path.GetFileName(RequirePath(attachment)),
                RequireTextContent(attachment),
                attachment.SelectionRange ?? throw new ArgumentException("Selection attachments require a selection range.", nameof(attachment))),
            SessionPromptAttachmentKind.Metadata => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported prompt attachment kind."),
        };

    private static AgentInputItem CreateImageInputItem(SessionPromptAttachment attachment)
    {
        var path = RequirePath(attachment);
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new AgentInputItem.ImageUrl(path)
            : new AgentInputItem.LocalImage(path, attachment.DisplayName, attachment.ContentType);
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(SessionPromptAttachment attachment)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentId"] = attachment.AttachmentId,
        };

        if (attachment.LineRange is not null)
        {
            metadata["startLine"] = attachment.LineRange.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["endLine"] = attachment.LineRange.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (attachment.SelectionRange is not null)
        {
            metadata["selectionStartLine"] = attachment.SelectionRange.Start.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["selectionStartCharacter"] = attachment.SelectionRange.Start.Character.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["selectionEndLine"] = attachment.SelectionRange.End.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["selectionEndCharacter"] = attachment.SelectionRange.End.Character.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static SessionPromptAttachmentKind ResolveKind(SessionPromptAttachment attachment)
    {
        if (attachment.Kind != SessionPromptAttachmentKind.Auto)
        {
            return attachment.Kind;
        }

        if (attachment.SelectionRange is not null)
        {
            return SessionPromptAttachmentKind.Selection;
        }

        if (attachment.Text is not null || (attachment.Content.Length > 0 && string.IsNullOrWhiteSpace(attachment.Path)))
        {
            return SessionPromptAttachmentKind.Text;
        }

        if (!string.IsNullOrWhiteSpace(attachment.Path))
        {
            if (Directory.Exists(attachment.Path))
            {
                return SessionPromptAttachmentKind.Directory;
            }

            return IsImage(attachment.Path, attachment.ContentType)
                ? SessionPromptAttachmentKind.Image
                : SessionPromptAttachmentKind.File;
        }

        return SessionPromptAttachmentKind.Metadata;
    }

    private static PluginPromptAttachmentKind ToPluginKind(SessionPromptAttachmentKind kind)
        => kind switch
        {
            SessionPromptAttachmentKind.Text => PluginPromptAttachmentKind.Text,
            SessionPromptAttachmentKind.File => PluginPromptAttachmentKind.File,
            SessionPromptAttachmentKind.Directory => PluginPromptAttachmentKind.Directory,
            SessionPromptAttachmentKind.Image => PluginPromptAttachmentKind.Image,
            SessionPromptAttachmentKind.Selection => PluginPromptAttachmentKind.Selection,
            SessionPromptAttachmentKind.Metadata => PluginPromptAttachmentKind.Metadata,
            _ => PluginPromptAttachmentKind.Metadata,
        };

    private static string? GetTextContentOrNull(SessionPromptAttachment attachment)
    {
        if (attachment.Text is not null)
        {
            return attachment.Text;
        }

        return attachment.Content.Length == 0 ? null : Encoding.UTF8.GetString(attachment.Content.Span);
    }

    private static string RequireTextContent(SessionPromptAttachment attachment)
        => GetTextContentOrNull(attachment) ?? throw new ArgumentException("Text or selection attachments require text content.", nameof(attachment));

    private static string RequirePath(SessionPromptAttachment attachment)
        => string.IsNullOrWhiteSpace(attachment.Path)
            ? throw new ArgumentException("File, directory, image, and selection attachments require a path.", nameof(attachment))
            : attachment.Path;

    private static void ValidateAttachmentId(SessionPromptAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.AttachmentId))
        {
            throw new ArgumentException("Prompt attachments require a non-empty attachment id.", nameof(attachment));
        }
    }

    private static bool IsImage(string path, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return IsImage(uri.AbsolutePath, contentType);
        }

        return ImageExtensions.Contains(Path.GetExtension(path));
    }
}

/// <summary>
/// Describes materialized headless prompt input for backend and plugin pipelines.
/// </summary>
/// <param name="Input">The backend agent input.</param>
/// <param name="PluginAttachments">Plugin prompt attachments derived from the same headless attachments.</param>
public sealed record HeadlessPromptMaterializationResult(
    AgentInput Input,
    IReadOnlyList<PluginPromptAttachment> PluginAttachments);
