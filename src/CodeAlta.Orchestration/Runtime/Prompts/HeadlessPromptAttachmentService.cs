using System.Text;
using CodeAlta.Agent;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Prompts;

/// <summary>
/// Materializes headless work-thread prompt text and attachments into backend and plugin input shapes.
/// </summary>
public sealed class HeadlessPromptAttachmentService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
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
    };

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
        IReadOnlyList<WorkThreadPromptAttachment> attachments)
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

    private static AgentInputItem? CreateInputItem(WorkThreadPromptAttachment attachment, WorkThreadPromptAttachmentKind kind)
        => kind switch
        {
            WorkThreadPromptAttachmentKind.Text => new AgentInputItem.Text(RequireTextContent(attachment)),
            WorkThreadPromptAttachmentKind.File => new AgentInputItem.File(RequirePath(attachment), attachment.DisplayName, attachment.LineRange),
            WorkThreadPromptAttachmentKind.Directory => new AgentInputItem.Directory(RequirePath(attachment), attachment.DisplayName, attachment.LineRange),
            WorkThreadPromptAttachmentKind.Image => CreateImageInputItem(attachment),
            WorkThreadPromptAttachmentKind.Selection => new AgentInputItem.Selection(
                RequirePath(attachment),
                attachment.DisplayName ?? Path.GetFileName(RequirePath(attachment)),
                RequireTextContent(attachment),
                attachment.SelectionRange ?? throw new ArgumentException("Selection attachments require a selection range.", nameof(attachment))),
            WorkThreadPromptAttachmentKind.Metadata => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported prompt attachment kind."),
        };

    private static AgentInputItem CreateImageInputItem(WorkThreadPromptAttachment attachment)
    {
        var path = RequirePath(attachment);
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new AgentInputItem.ImageUrl(path)
            : new AgentInputItem.LocalImage(path, attachment.DisplayName, attachment.ContentType);
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(WorkThreadPromptAttachment attachment)
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

    private static WorkThreadPromptAttachmentKind ResolveKind(WorkThreadPromptAttachment attachment)
    {
        if (attachment.Kind != WorkThreadPromptAttachmentKind.Auto)
        {
            return attachment.Kind;
        }

        if (attachment.SelectionRange is not null)
        {
            return WorkThreadPromptAttachmentKind.Selection;
        }

        if (attachment.Text is not null || (attachment.Content.Length > 0 && string.IsNullOrWhiteSpace(attachment.Path)))
        {
            return WorkThreadPromptAttachmentKind.Text;
        }

        if (!string.IsNullOrWhiteSpace(attachment.Path))
        {
            if (Directory.Exists(attachment.Path))
            {
                return WorkThreadPromptAttachmentKind.Directory;
            }

            return IsImage(attachment.Path, attachment.ContentType)
                ? WorkThreadPromptAttachmentKind.Image
                : WorkThreadPromptAttachmentKind.File;
        }

        return WorkThreadPromptAttachmentKind.Metadata;
    }

    private static PluginPromptAttachmentKind ToPluginKind(WorkThreadPromptAttachmentKind kind)
        => kind switch
        {
            WorkThreadPromptAttachmentKind.Text => PluginPromptAttachmentKind.Text,
            WorkThreadPromptAttachmentKind.File => PluginPromptAttachmentKind.File,
            WorkThreadPromptAttachmentKind.Directory => PluginPromptAttachmentKind.Directory,
            WorkThreadPromptAttachmentKind.Image => PluginPromptAttachmentKind.Image,
            WorkThreadPromptAttachmentKind.Selection => PluginPromptAttachmentKind.Selection,
            WorkThreadPromptAttachmentKind.Metadata => PluginPromptAttachmentKind.Metadata,
            _ => PluginPromptAttachmentKind.Metadata,
        };

    private static string? GetTextContentOrNull(WorkThreadPromptAttachment attachment)
    {
        if (attachment.Text is not null)
        {
            return attachment.Text;
        }

        return attachment.Content.Length == 0 ? null : Encoding.UTF8.GetString(attachment.Content.Span);
    }

    private static string RequireTextContent(WorkThreadPromptAttachment attachment)
        => GetTextContentOrNull(attachment) ?? throw new ArgumentException("Text or selection attachments require text content.", nameof(attachment));

    private static string RequirePath(WorkThreadPromptAttachment attachment)
        => string.IsNullOrWhiteSpace(attachment.Path)
            ? throw new ArgumentException("File, directory, image, and selection attachments require a path.", nameof(attachment))
            : attachment.Path;

    private static void ValidateAttachmentId(WorkThreadPromptAttachment attachment)
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
