using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XenoAtom.Glob;
using XenoAtom.Glob.Git;
using XenoAtom.Glob.IO;

namespace CodeAlta.Agent.Runtime.Tools;

// 模块功能：内置工具工厂，为本地原始 API 会话创建并注册 read_file/list_dir/grep/webget/shell_command 等所有内置工具
/// <summary>
/// Creates the default non-provider built-in tools used by local raw-API sessions.
/// </summary>
public static class AgentBuiltInToolFactory
{
    private static readonly FileTreeWalker FileTreeWalker = new();
    private const string SupportedWebContentTypesDescription = "text/*, application/json, application/xml, and application/xhtml+xml";
    private const string WriteFileToolDescription =
        "Write an entire text file in one deterministic operation. Creates parent directories as needed and replaces any existing file.";
    private const string ReplaceInFileToolDescription =
        "Replace exact text in a file. Deterministic only: no regex, no fuzzy matching. When replace_all is false, the tool errors unless exactly one match exists and leaves the file unchanged. Use replace_all=true to replace every match.";
    private const string DeleteFileOrDirToolDescription =
        "Delete a file or a directory. Directory deletes are recursive.";
    private const string RenameFileOrDirToolDescription =
        "Rename or move a file or directory. Will not overwrite an existing destination.";
    private const string ApplyPatchToolDescription =
        "Use the `apply_patch` tool to edit files.";

    private static readonly JsonElement WriteFileSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File path. Relative paths resolve from the session working directory; absolute paths are accepted." },
            "content": { "type": "string", "description": "Exact file contents to write." }
          },
          "required": ["path", "content"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement ReplaceInFileSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File path. Relative paths resolve from the session working directory; absolute paths are accepted." },
            "old_string": { "type": "string", "description": "Exact text to replace. Newlines are matched exactly, with deterministic normalization to the file's newline style when needed." },
            "new_string": { "type": "string", "description": "Replacement text." },
            "replace_all": { "type": "boolean", "description": "Replace every exact match. When false, the tool errors unless exactly one match exists and leaves the file unchanged." }
          },
          "required": ["path", "old_string", "new_string"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement DeleteFileOrDirSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File or directory path. Relative paths resolve from the session working directory; absolute paths are accepted." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement RenameFileOrDirSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "old_path": { "type": "string", "description": "Existing file or directory path. Relative paths resolve from the session working directory; absolute paths are accepted." },
            "new_path": { "type": "string", "description": "Destination path. Relative paths resolve from the session working directory; absolute paths are accepted. Will not overwrite an existing destination." }
          },
          "required": ["old_path", "new_path"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement ApplyPatchSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "input": {
              "type": "string",
              "description": "Patch text in the Codex/OpenAI apply_patch format. Start with '*** Begin Patch' and end with '*** End Patch'. Use '*** Add File:', '*** Delete File:', or '*** Update File:'. Paths resolve from the session working directory unless absolute, and may use '..'. An update may include '*** Move to:'. Hunks begin with '@@' or '@@ anchor text'; inside hunks use space for context, '-' for deletions, and '+' for additions."
            }
          },
          "required": ["input"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement RequestUserInputSchema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "prompts": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string" },
                  "question": { "type": "string" },
                  "header": { "type": "string" },
                  "allowFreeform": { "type": "boolean" },
                  "isSecret": { "type": "boolean" },
                  "options": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "label": { "type": "string" },
                        "description": { "type": "string" }
                      },
                      "required": ["label"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["id", "question"],
                "additionalProperties": false
              }
            }
          },
          "required": ["prompts"],
          "additionalProperties": false
        }
        """);

    // 函数功能：构建并返回所有默认内置工具列表（read_file/list_dir/grep/webget/shell_command/write_file/replace_in_file/delete_file_or_dir/rename_file_or_dir/apply_patch），根据 options 决定是否启用 apply_patch
    /// <summary>
    /// Creates the default built-in tools.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>The built-in tools.</returns>
    public static IReadOnlyList<AgentToolDefinition> CreateDefaultTools(AgentBuiltInToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var httpClient = options.HttpClient ?? new HttpClient();
        AgentToolDefinition[] tools =
        [
            new AgentToolDefinition(
                new AgentToolSpec(
                    "read_file",
                    $"Read a local text file by line number. Offsets are 1-based; use a negative offset to count from the end (-1 is the last line). Offsets past EOF return an empty text result, and oversized negative offsets clamp to line 1. Omitting limit returns up to {options.DefaultReadFileLineLimit} lines by default.",
                    CreateReadFileSchema(options)),
                (invocation, cancellationToken) => ReadFileAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "list_dir",
                    "List the direct children of a directory as [dir] and [file] entries.",
                    CreateListDirSchema()),
                (invocation, cancellationToken) => ListDirectoryAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "grep",
                    $"Search one or more files or directories for line-based matches using a .NET regular expression. Recurses into directories, defaults to case-insensitive matching, skips likely-binary files, accepts one or more optional globs, and returns '(no matches)' when nothing matches.",
                    CreateGrepSchema(options)),
                (invocation, cancellationToken) => GrepAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "webget",
                    $"Fetch text-like web content from a known absolute URL. On success, returns a text result containing the fetched body. By default, HTML responses are simplified to plain text; set rawHtml=true to return raw HTML markup. Set includeHttpStatus=true to prefix the HTTP status line and response content type before the body. JSON and XML are returned as text bodies. Applies content-type checks and a {options.MaxWebGetBytes.ToString(CultureInfo.InvariantCulture)}-byte limit; default timeout is {FormatSeconds(options.WebGetTimeout)} seconds.",
                    CreateWebGetSchema(options)),
                (invocation, cancellationToken) => WebGetAsync(options, httpClient, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "shell_command",
                    "Execute a local shell command or short shell script using the platform shell, subject to host approval. Some hosts may auto-approve. Returns exit_code, working_directory, stdout, and stderr as emitted by the shell and child processes; child commands may still include ANSI/control sequences. On Windows, pwsh runs with -NoProfile and preserves the final external command exit code when that command fails.",
                    CreateShellCommandSchema()),
                (invocation, cancellationToken) => ShellCommandAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "write_file",
                    WriteFileToolDescription,
                    WriteFileSchema),
                (invocation, cancellationToken) => WriteFileAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "replace_in_file",
                    ReplaceInFileToolDescription,
                    ReplaceInFileSchema),
                (invocation, cancellationToken) => ReplaceInFileAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "delete_file_or_dir",
                    DeleteFileOrDirToolDescription,
                    DeleteFileOrDirSchema),
                (invocation, cancellationToken) => DeleteFileOrDirAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "rename_file_or_dir",
                    RenameFileOrDirToolDescription,
                    RenameFileOrDirSchema),
                (invocation, cancellationToken) => RenameFileOrDirAsync(options, invocation, cancellationToken)),
            new AgentToolDefinition(
                new AgentToolSpec(
                    "apply_patch",
                    ApplyPatchToolDescription,
                    ApplyPatchSchema),
                (invocation, cancellationToken) => ApplyPatchAsync(options, invocation, cancellationToken)),
            // Intentionally not registered yet: the local raw-API host does not currently expose
            // the structured UI feedback loop needed to pause for request_user_input safely.
            // Keep the implementation around so the tool can be enabled once the host supports it.
        ];

        return tools
            .Where(tool => ShouldIncludeBuiltInTool(options, tool.Spec.Name))
            .ToArray();
    }

    // 函数功能：实现 read_file 工具，按行号范围读取本地文本文件；参数 offset 支持正向/负向偏移，limit 限制最大行数，返回带行号前缀的文本内容
    private static Task<AgentToolResult> ReadFileAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var path = GetRequiredString(invocation.Arguments, "path");
        var resolvedPath = ResolvePath(options.WorkingDirectory, path);
        if (!File.Exists(resolvedPath))
        {
            return Task.FromResult(Failure($"File '{resolvedPath}' was not found."));
        }

        if (AgentFileTypeDetector.IsProbablyBinaryFile(resolvedPath))
        {
            return Task.FromResult(Failure($"'{resolvedPath}' appears to be a binary file. read_file only supports text files."));
        }

        var offset = GetOptionalInt(invocation.Arguments, "offset") ?? 1;
        if (offset == 0)
        {
            return Task.FromResult(Failure("The 'offset' value must be >= 1, or negative to count from the end. 0 is not allowed."));
        }

        var requestedLimit = GetOptionalInt(invocation.Arguments, "limit") ?? options.DefaultReadFileLineLimit;
        var limit = Math.Clamp(requestedLimit, 1, options.MaxReadFileLineLimit);
        var startLine = offset >= 1 ? offset : GetStartLineFromEnd(resolvedPath, offset, cancellationToken);

        var lines = new List<string>(limit);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(resolvedPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (lineNumber < startLine)
            {
                continue;
            }

            lines.Add($"{lineNumber,5}: {line}");
            if (lines.Count >= limit)
            {
                break;
            }
        }

        return Task.FromResult(new AgentToolResult(
            true,
            [new AgentToolResultItem.Text(string.Join(Environment.NewLine, lines))]));
    }

    // 函数功能：将负向行偏移（从末尾倒数）转换为正向起始行号；先统计文件总行数，再计算实际起始行，最小值钳至 1
    private static int GetStartLineFromEnd(string path, int offset, CancellationToken cancellationToken)
    {
        var totalLines = 0;
        foreach (var _ in File.ReadLines(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;
        }

        var startLine = totalLines + offset + 1;
        return Math.Max(1, startLine);
    }

    // 函数功能：实现 list_dir 工具，列出指定目录的直接子项（文件标注 [file]、目录标注 [dir]），按名称不区分大小写排序后返回
    private static Task<AgentToolResult> ListDirectoryAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var path = GetOptionalString(invocation.Arguments, "path");
        var resolvedPath = ResolvePath(options.WorkingDirectory, path);
        if (File.Exists(resolvedPath))
        {
            return Task.FromResult(Failure($"Path '{resolvedPath}' is not a directory."));
        }

        if (!Directory.Exists(resolvedPath))
        {
            return Task.FromResult(Failure($"Directory '{resolvedPath}' was not found."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = Directory.EnumerateFileSystemEntries(resolvedPath)
            .Select(static entry =>
            {
                var name = Path.GetFileName(entry);
                return Directory.Exists(entry) ? $"[dir]  {name}" : $"[file] {name}";
            })
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new AgentToolResult(
            true,
            [new AgentToolResultItem.Text(entries.Length == 0 ? "(empty directory)" : string.Join(Environment.NewLine, entries))]));
    }

    // 函数功能：实现 grep 工具，在指定文件或目录中按 .NET 正则逐行搜索；支持 glob 过滤、大小写敏感开关、最大匹配数限制，跳过二进制文件和图片，无匹配时返回 "(no matches)"
    private static Task<AgentToolResult> GrepAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var pattern = GetRequiredString(invocation.Arguments, "pattern");
        if (!TryResolveGrepTargetPaths(options.WorkingDirectory, invocation.Arguments, out var targetPaths, out var targetPathError))
        {
            return Task.FromResult(Failure(targetPathError));
        }

        foreach (var targetPath in targetPaths)
        {
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                return Task.FromResult(Failure($"Path '{targetPath}' was not found."));
            }
        }

        if (!TryGetGrepGlobPatterns(invocation.Arguments, out var globPatterns, out var globError))
        {
            return Task.FromResult(Failure(globError));
        }

        var caseSensitive = GetOptionalBool(invocation.Arguments, "caseSensitive") ?? false;
        var maxMatches = Math.Clamp(GetOptionalInt(invocation.Arguments, "maxMatches") ?? options.MaxGrepMatches, 1, options.MaxGrepMatches);
        Regex regex;
        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                matchTimeout: TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(Failure($"Invalid regular expression '{pattern}': {ex.Message}"));
        }

        var matches = new List<string>(maxMatches);

        var visitedFiles = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var targetPath in targetPaths)
        {
            IEnumerable<FileTreeEntry>? entries = null;
            if (Directory.Exists(targetPath))
            {
                var walkOptions = new FileTreeWalkOptions
                {
                    CancellationToken = cancellationToken,
                    RepositoryContext = RepositoryDiscovery.TryDiscover(targetPath, out var repositoryContext) ? repositoryContext : null,
                };
                entries = FileTreeWalker.Enumerate(targetPath, walkOptions);
            }

            foreach (var file in EnumerateSearchFiles(targetPath, entries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visitedFiles.Add(file.FullPath))
                {
                    continue;
                }

                if (globPatterns.Length > 0 && !GlobMatches(globPatterns, file))
                {
                    continue;
                }

                if (IsImagePath(file.FullPath))
                {
                    continue;
                }

                try
                {
                    if (AgentFileTypeDetector.IsProbablyBinaryFile(file.FullPath))
                    {
                        continue;
                    }

                    SearchFileLines(file.FullPath, file.DisplayPath, regex, maxMatches, matches, cancellationToken);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (DecoderFallbackException)
                {
                    continue;
                }

                if (matches.Count >= maxMatches)
                {
                    break;
                }
            }

            if (matches.Count >= maxMatches)
            {
                break;
            }
        }

        return Task.FromResult(new AgentToolResult(
            true,
            [new AgentToolResultItem.Text(matches.Count == 0 ? "(no matches)" : string.Join(Environment.NewLine, matches))]));
    }

    // 函数功能：实现 webget 工具，向指定 HTTP/HTTPS URL 发起 GET 请求；校验 URL 合法性、Content-Type 支持性及响应体大小限制，HTML/XHTML 默认简化为纯文本，支持 rawHtml/includeHttpStatus/timeoutSeconds 参数
    private static async Task<AgentToolResult> WebGetAsync(
        AgentBuiltInToolOptions options,
        HttpClient httpClient,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var url = GetRequiredString(invocation.Arguments, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Failure($"'{url}' is not a valid absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Failure($"The '{uri.Scheme}' scheme is not supported by webget. Use http or https.");
        }

        var timeoutOverride = GetOptionalInt(invocation.Arguments, "timeoutSeconds");
        var rawHtml = GetOptionalBool(invocation.Arguments, "rawHtml") ?? false;
        var includeHttpStatus = GetOptionalBool(invocation.Arguments, "includeHttpStatus") ?? false;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var effectiveTimeout = timeoutOverride is > 0 ? TimeSpan.FromSeconds(timeoutOverride.Value) : options.WebGetTimeout;
        linkedCts.CancelAfter(effectiveTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var reasonPhrase = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                    ? "Unknown Status"
                    : response.ReasonPhrase;
                return Failure(
                    $"webget request to '{uri}' failed with HTTP {(int)response.StatusCode} ({reasonPhrase}).");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !IsSupportedWebContentType(mediaType))
            {
                return Failure($"Content type '{mediaType}' is not supported by webget. Supported types are {SupportedWebContentTypesDescription}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(capacity: options.MaxWebGetBytes);
            var rented = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(rented, linkedCts.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > options.MaxWebGetBytes)
                {
                    return Failure($"Response exceeded the {options.MaxWebGetBytes.ToString(CultureInfo.InvariantCulture)} byte limit enforced by webget.");
                }

                buffer.Write(rented, 0, read);
            }

            var text = Encoding.UTF8.GetString(buffer.ToArray());
            if (!rawHtml &&
                (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            {
                text = SimplifyHtml(text);
            }

            if (includeHttpStatus)
            {
                text = FormatWebGetSuccessResponse(response, text);
            }

            var trimmedText = text.Trim();
            return new AgentToolResult(
                true,
                [new AgentToolResultItem.Text(trimmedText.Length == 0 ? "(empty response body)" : trimmedText)]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure($"webget timed out after {effectiveTimeout.TotalSeconds:0.###} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return Failure($"webget failed to fetch '{uri}': {ex.Message}");
        }
    }

    // 函数功能：实现 shell_command 工具，向宿主请求权限后在本地 shell（Unix 为 $SHELL，Windows 为 pwsh）执行命令，实时泵送 stdout/stderr，支持超时；返回 exit_code、working_directory、stdout、stderr
    private static async Task<AgentToolResult> ShellCommandAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var command = GetRequiredString(invocation.Arguments, "command");
        var workdir = ResolvePath(options.WorkingDirectory, GetOptionalString(invocation.Arguments, "workdir"));
        if (!Directory.Exists(workdir))
        {
            return Failure($"Directory '{workdir}' was not found.");
        }

        var timeout = GetOptionalInt(invocation.Arguments, "timeoutMs");
        var login = GetOptionalBool(invocation.Arguments, "login") ?? false;

        var permissionRequest = new AgentCommandPermissionRequest(
            options.ProviderId,
            options.SessionId,
            DateTimeOffset.UtcNow,
            RunId: null,
            InteractionId: invocation.ToolCallId,
            ApprovalId: null,
            Command: command,
            WorkingDirectory: workdir,
            Actions: null,
            Reason: "The agent requested local shell execution.",
            Network: null,
            ProposedExecPolicyAmendment: null,
            ProposedNetworkPolicyAmendments: null);

        var decision = await options.OnPermissionRequest(permissionRequest, cancellationToken).ConfigureAwait(false);
        switch (decision.Kind)
        {
            case AgentPermissionDecisionKind.AllowOnce:
            case AgentPermissionDecisionKind.AllowForSession:
                break;
            case AgentPermissionDecisionKind.Deny:
                return Failure("shell_command was denied by the host.");
            case AgentPermissionDecisionKind.Cancel:
                return Failure("shell_command was canceled by the host.");
            default:
                return Failure($"Unsupported permission decision '{decision.Kind}'.");
        }

        var processSpec = CreateShellProcessSpec(command, workdir, login);
        using var process = new Process
        {
            StartInfo = processSpec.StartInfo,
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                return Failure("shell_command did not start a process.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Failure($"shell_command failed to start: {ex.Message}");
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutTask = PumpProcessStreamAsync(
            process.StandardOutput,
            "stdout",
            stdoutBuilder,
            invocation.Progress,
            cancellationToken);
        var stderrTask = PumpProcessStreamAsync(
            process.StandardError,
            "stderr",
            stderrBuilder,
            invocation.Progress,
            cancellationToken);

        try
        {
            if (timeout is > 0)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout.Value);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Failure(timeout is > 0
                ? $"shell_command timed out after {timeout.Value} ms."
                : "shell_command was canceled.");
        }

        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
        var output = FormatShellCommandOutput(process.ExitCode, stdout, stderr, workdir);
        if (process.ExitCode != 0)
        {
            return new AgentToolResult(
                false,
                [new AgentToolResultItem.Text(output)],
                $"shell_command exited with code {process.ExitCode}.");
        }

        return new AgentToolResult(true, [new AgentToolResultItem.Text(output)]);
    }

    // 函数功能：实现 write_file 工具，将指定内容完整写入文件（若不存在则创建，已存在则覆盖）；写入前自动创建父目录并向宿主请求文件修改权限
    private static async Task<AgentToolResult> WriteFileAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = GetWorkingDirectoryRoot(options);
        WorkspacePathResolution resolution;
        try
        {
            resolution = ResolveWorkspacePath(rootPath, GetRequiredString(invocation.Arguments, "path"));
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        var existed = File.Exists(resolution.FullPath);
        if (Directory.Exists(resolution.FullPath))
        {
            return Failure($"'{resolution.DisplayPath}' is an existing directory.");
        }

        var content = GetRequiredStringValue(invocation.Arguments, "content");
        var permission = await RequestFileChangePermissionAsync(
                options,
                invocation,
                "write_file",
                [resolution.FullPath],
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is not null)
        {
            return permission;
        }

        var parentDirectory = Path.GetDirectoryName(resolution.FullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await File.WriteAllTextAsync(resolution.FullPath, content, cancellationToken).ConfigureAwait(false);
        var verb = existed ? "Wrote" : "Created";
        return SuccessResult($"{verb} {resolution.DisplayPath}");
    }

    // 函数功能：实现 replace_in_file 工具，在文本文件中精确替换字符串；replace_all=false 时要求恰好一处匹配，自动适配文件换行风格，替换前向宿主请求权限
    private static async Task<AgentToolResult> ReplaceInFileAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = GetWorkingDirectoryRoot(options);
        WorkspacePathResolution resolution;
        try
        {
            resolution = ResolveWorkspacePath(rootPath, GetRequiredString(invocation.Arguments, "path"));
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        if (!File.Exists(resolution.FullPath))
        {
            return Failure($"File '{resolution.DisplayPath}' was not found.");
        }

        if (AgentFileTypeDetector.IsProbablyBinaryFile(resolution.FullPath))
        {
            return Failure($"replace_in_file does not support binary files: '{resolution.DisplayPath}'.");
        }

        var oldString = GetRequiredStringValue(invocation.Arguments, "old_string");
        if (oldString.Length == 0)
        {
            return Failure("The 'old_string' value must not be empty.");
        }

        var newString = GetRequiredStringValue(invocation.Arguments, "new_string");
        var replaceAll = GetOptionalBool(invocation.Arguments, "replace_all") ?? false;

        var original = await File.ReadAllTextAsync(resolution.FullPath, cancellationToken).ConfigureAwait(false);
        var newline = DetectExistingNewline(original);
        var replacementTarget = oldString;
        var replacementValue = newString;

        if (!original.Contains(replacementTarget, StringComparison.Ordinal))
        {
            var normalizedOldString = NormalizeNewlines(oldString, newline);
            if (!string.Equals(normalizedOldString, oldString, StringComparison.Ordinal) &&
                original.Contains(normalizedOldString, StringComparison.Ordinal))
            {
                replacementTarget = normalizedOldString;
                replacementValue = NormalizeNewlines(newString, newline);
            }
        }

        var matchCount = CountOccurrences(original, replacementTarget);
        if (matchCount == 0)
        {
            return Failure($"replace_in_file could not find the requested text in '{resolution.DisplayPath}'.");
        }

        if (!replaceAll && matchCount != 1)
        {
            return Failure(
                $"replace_in_file found {matchCount} matches in '{resolution.DisplayPath}'. Set replace_all=true to replace every match.");
        }

        var permission = await RequestFileChangePermissionAsync(
                options,
                invocation,
                "replace_in_file",
                [resolution.FullPath],
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is not null)
        {
            return permission;
        }

        var updated = replaceAll
            ? original.Replace(replacementTarget, replacementValue, StringComparison.Ordinal)
            : ReplaceFirstOccurrence(original, replacementTarget, replacementValue);
        await File.WriteAllTextAsync(resolution.FullPath, updated, cancellationToken).ConfigureAwait(false);
        var replacedCount = replaceAll ? matchCount : 1;
        return SuccessResult($"Replaced {replacedCount} occurrence(s) in {resolution.DisplayPath}");
    }

    // 函数功能：实现 delete_file_or_dir 工具，删除指定文件或递归删除目录；禁止删除会话工作目录本身，删除前向宿主请求权限
    private static async Task<AgentToolResult> DeleteFileOrDirAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = GetWorkingDirectoryRoot(options);
        WorkspacePathResolution resolution;
        try
        {
            resolution = ResolveWorkspacePath(rootPath, GetRequiredString(invocation.Arguments, "path"));
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        if (string.Equals(resolution.FullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("delete_file_or_dir cannot delete the session working directory.");
        }

        var existsAsFile = File.Exists(resolution.FullPath);
        var existsAsDirectory = Directory.Exists(resolution.FullPath);
        if (!existsAsFile && !existsAsDirectory)
        {
            return Failure($"Path '{resolution.DisplayPath}' was not found.");
        }

        var permission = await RequestFileChangePermissionAsync(
                options,
                invocation,
                "delete_file_or_dir",
                [resolution.FullPath],
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is not null)
        {
            return permission;
        }

        try
        {
            if (existsAsDirectory)
            {
                Directory.Delete(resolution.FullPath, recursive: true);
                return SuccessResult($"Deleted directory {resolution.DisplayPath}");
            }

            File.Delete(resolution.FullPath);
            return SuccessResult($"Deleted file {resolution.DisplayPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure($"delete_file_or_dir failed for '{resolution.DisplayPath}': {ex.Message}");
        }
    }

    // 函数功能：实现 rename_file_or_dir 工具，重命名或移动文件/目录；不允许覆盖已存在的目标路径，自动创建目标父目录，移动前向宿主请求权限
    private static async Task<AgentToolResult> RenameFileOrDirAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = GetWorkingDirectoryRoot(options);
        WorkspacePathResolution sourceResolution;
        WorkspacePathResolution destinationResolution;
        try
        {
            sourceResolution = ResolveWorkspacePath(rootPath, GetRequiredString(invocation.Arguments, "old_path"));
            destinationResolution = ResolveWorkspacePath(rootPath, GetRequiredString(invocation.Arguments, "new_path"));
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        if (string.Equals(sourceResolution.FullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("rename_file_or_dir cannot move the session working directory.");
        }

        if (string.Equals(sourceResolution.FullPath, destinationResolution.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("rename_file_or_dir requires different source and destination paths.");
        }

        var sourceIsFile = File.Exists(sourceResolution.FullPath);
        var sourceIsDirectory = Directory.Exists(sourceResolution.FullPath);
        if (!sourceIsFile && !sourceIsDirectory)
        {
            return Failure($"Source path '{sourceResolution.DisplayPath}' was not found.");
        }

        if (File.Exists(destinationResolution.FullPath) || Directory.Exists(destinationResolution.FullPath))
        {
            return Failure($"Destination '{destinationResolution.DisplayPath}' already exists.");
        }

        var permission = await RequestFileChangePermissionAsync(
                options,
                invocation,
                "rename_file_or_dir",
                [sourceResolution.FullPath, destinationResolution.FullPath],
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is not null)
        {
            return permission;
        }

        try
        {
            var destinationParent = Path.GetDirectoryName(destinationResolution.FullPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            if (sourceIsDirectory)
            {
                Directory.Move(sourceResolution.FullPath, destinationResolution.FullPath);
                return SuccessResult($"Renamed directory {sourceResolution.DisplayPath} -> {destinationResolution.DisplayPath}");
            }

            File.Move(sourceResolution.FullPath, destinationResolution.FullPath);
            return SuccessResult($"Renamed file {sourceResolution.DisplayPath} -> {destinationResolution.DisplayPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(
                $"rename_file_or_dir failed for '{sourceResolution.DisplayPath}' -> '{destinationResolution.DisplayPath}': {ex.Message}");
        }
    }

    // 函数功能：实现 apply_patch 工具，解析 Codex/OpenAI apply_patch 格式的补丁文本，获取受影响路径并向宿主请求权限，通过 AgentApplyPatch.Apply 执行实际文件变更
    private static async Task<AgentToolResult> ApplyPatchAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var workingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory;
        var patchInput = GetRequiredString(invocation.Arguments, "input");
        IReadOnlyList<string> touchedPaths;
        try
        {
            touchedPaths = AgentApplyPatch.GetTouchedPaths(patchInput, workingDirectory);
        }
        catch (InvalidOperationException)
        {
            touchedPaths = [];
        }

        var permission = await RequestFileChangePermissionAsync(
                options,
                invocation,
                "apply_patch",
                touchedPaths,
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is not null)
        {
            return permission;
        }

        try
        {
            return AgentApplyPatch.Apply(patchInput, workingDirectory);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }
    }

    // 函数功能：实现 request_user_input 工具（暂未注册），将结构化提示列表转交宿主的用户输入处理器，等待用户响应后将答案序列化为 JSON 返回
    private static async Task<AgentToolResult> RequestUserInputAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (options.OnUserInputRequest is null)
        {
            return Failure("No user-input handler is configured for this session.");
        }

        var promptsElement = invocation.Arguments.TryGetProperty("prompts", out var prompts)
            ? prompts
            : throw new ArgumentException("The 'prompts' field is required.", nameof(invocation));
        if (promptsElement.ValueKind != JsonValueKind.Array)
        {
            return Failure("The 'prompts' field must be an array.");
        }

        var mappedPrompts = new List<AgentUserInputPrompt>();
        foreach (var prompt in promptsElement.EnumerateArray())
        {
            var id = GetRequiredString(prompt, "id");
            var question = GetRequiredString(prompt, "question");
            var header = GetOptionalString(prompt, "header");
            var allowFreeform = GetOptionalBool(prompt, "allowFreeform") ?? true;
            var isSecret = GetOptionalBool(prompt, "isSecret") ?? false;
            var optionsList = prompt.TryGetProperty("options", out var promptOptions) && promptOptions.ValueKind == JsonValueKind.Array
                ? promptOptions.EnumerateArray()
                    .Select(static option => new AgentUserInputOption(
                        GetRequiredString(option, "label"),
                        GetOptionalString(option, "description")))
                    .ToArray()
                : null;
            mappedPrompts.Add(new AgentUserInputPrompt(id, question, header, optionsList, allowFreeform, isSecret));
        }

        var request = new AgentUserInputRequest(
            options.ProviderId,
            options.SessionId,
            DateTimeOffset.UtcNow,
            null,
            $"tool-input:{Guid.NewGuid():N}",
            new AgentUserInputForm(mappedPrompts));
        var response = await options.OnUserInputRequest(request, cancellationToken).ConfigureAwait(false);

        var json = SerializeAnswers(response.Answers);
        return new AgentToolResult(true, [new AgentToolResultItem.Text(json)]);
    }

    // 函数功能：将 JSON 字符串解析为 JsonElement，用于构建工具 Schema 定义
    private static JsonElement ParseSchema([StringSyntax("json")] string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    // 函数功能：构造表示失败的工具结果，包含错误消息文本
    private static AgentToolResult Failure(string message)
        => new(false, [new AgentToolResultItem.Text(message)], message);

    // 函数功能：构造表示成功的工具结果，包含操作摘要文本
    private static AgentToolResult SuccessResult(string message)
        => new(true, [new AgentToolResultItem.Text(message)]);

    // 函数功能：根据 Provider Profile 的覆盖配置判断指定工具是否应被包含；apply_patch 需额外检查 Provider 支持
    private static bool ShouldIncludeBuiltInTool(AgentBuiltInToolOptions options, string toolName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (options.Provider?.Profile?.BuiltInToolOverrides is { Count: > 0 } overrides &&
            overrides.TryGetValue(toolName, out var enabled))
        {
            return enabled;
        }

        return !string.Equals(toolName, "apply_patch", StringComparison.Ordinal) ||
               IsApplyPatchSupported(options.Provider, options.ProviderId);
    }

    // 函数功能：判断当前 Provider 是否支持 apply_patch 工具；仅 OpenAI Responses/Chat 传输层且为 codex 协议族或指向 api.openai.com 时返回 true
    private static bool IsApplyPatchSupported(ModelProviderRuntimeDescriptor? provider, ModelProviderId ProviderId)
    {
        if (provider is null)
        {
            return ProviderId == ModelProviderIds.OpenAIResponses || ProviderId == ModelProviderIds.OpenAIChat;
        }

        if (provider.TransportKind is not (AgentTransportKind.OpenAIResponses or AgentTransportKind.OpenAIChatCompletions))
        {
            return false;
        }

        if (string.Equals(provider.ProtocolFamily, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (provider.BaseUri is null)
        {
            return true;
        }

        return string.Equals(provider.BaseUri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    // 函数功能：获取会话工作目录的规范化绝对路径，未设置时回退到进程当前目录
    private static string GetWorkingDirectoryRoot(AgentBuiltInToolOptions options)
        => Path.GetFullPath(options.WorkingDirectory ?? Environment.CurrentDirectory);

    // 函数功能：将工具参数中的路径解析为工作区绝对路径，同时生成相对于根目录的展示路径（用正斜杠）；路径为空时抛出异常
    private static WorkspacePathResolution ResolveWorkspacePath(string rootPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A non-empty path is required.");
        }

        var fullPath = ResolvePath(rootPath, path);
        var displayPath = Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return new WorkspacePathResolution(fullPath, displayPath);
    }

    // 函数功能：向宿主请求文件变更权限；AllowOnce/AllowForSession 时返回 null 表示允许，Deny/Cancel 时返回对应失败结果
    private static async Task<AgentToolResult?> RequestFileChangePermissionAsync(
        AgentBuiltInToolOptions options,
        AgentToolInvocation invocation,
        string toolName,
        IReadOnlyList<string> touchedPaths,
        CancellationToken cancellationToken)
    {
        var workingDirectory = GetWorkingDirectoryRoot(options);
        var permissionRequest = new AgentFileChangePermissionRequest(
            options.ProviderId,
            options.SessionId,
            DateTimeOffset.UtcNow,
            RunId: null,
            InteractionId: invocation.ToolCallId,
            GrantRoot: workingDirectory,
            Reason: touchedPaths.Count == 0
                ? $"The agent requested filesystem edits via {toolName}."
                : $"The agent requested filesystem edits via {toolName} affecting {touchedPaths.Count} path(s).");
        var decision = await options.OnPermissionRequest(permissionRequest, cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            AgentPermissionDecisionKind.AllowOnce or AgentPermissionDecisionKind.AllowForSession => null,
            AgentPermissionDecisionKind.Deny => Failure($"{toolName} was denied by the host."),
            AgentPermissionDecisionKind.Cancel => Failure($"{toolName} was canceled by the host."),
            _ => Failure($"Unsupported permission decision '{decision.Kind}'."),
        };
    }

    // 函数功能：检测文本使用的换行符风格，含 \r\n 则返回 "\r\n"，否则返回 "\n"
    private static string DetectExistingNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    // 函数功能：将文本中的换行符统一规范化为指定风格（先统一转为 \n，再替换为目标换行符）
    private static string NormalizeNewlines(string text, string newline)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);

    // 函数功能：统计 value 在 text 中精确出现的次数（Ordinal 比较），value 为空时返回 0
    private static int CountOccurrences(string text, string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    // 函数功能：替换 text 中 oldValue 的第一处出现为 newValue；未找到时返回原字符串
    private static string ReplaceFirstOccurrence(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }

    // 函数功能：将相对或绝对路径解析为完整路径；相对路径以 workingDirectory 为基准，绝对路径直接规范化，两者均为空时抛出异常
    private static string ResolvePath(string? workingDirectory, string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ? workingDirectory : path;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("A path or working directory is required.");
        }

        var baseDirectory = workingDirectory ?? Environment.CurrentDirectory;
        // Path-like tool arguments should resolve relative inputs from the session working directory
        // while still accepting rooted inputs unchanged.
        return Path.GetFullPath(Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(baseDirectory, candidate));
    }

    // 函数功能：根据文件扩展名判断路径是否为图片文件（png/jpg/jpeg/gif/webp/bmp）
    private static bool IsImagePath(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";

    // 函数功能：将 HTML 简化为纯文本：去除 script/style 标签、剥离所有 HTML 标签、解码 HTML 实体、合并连续空白
    private static string SimplifyHtml(string html)
    {
        var withoutScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    // 函数功能：判断 FileTreeEntry 是否匹配单个 glob 模式；useRelativePath=true 时匹配相对路径，否则匹配文件名
    private static bool GlobMatches(GlobPattern globPattern, FileTreeEntry entry, bool useRelativePath)
        => useRelativePath
            ? globPattern.IsMatch(entry.RelativePath)
            : globPattern.IsMatch(entry.Name);

    // 函数功能：判断 SearchFileTarget 是否匹配任意一个 SearchGlobPattern（任一匹配即返回 true）
    private static bool GlobMatches(IReadOnlyList<SearchGlobPattern> globPatterns, SearchFileTarget entry)
    {
        foreach (var globPattern in globPatterns)
        {
            if (GlobMatches(globPattern.Pattern, entry, globPattern.UseRelativePath))
            {
                return true;
            }
        }

        return false;
    }

    // 函数功能：判断 SearchFileTarget 是否匹配单个 glob 模式；useRelativePath=true 时匹配相对路径，否则匹配文件名
    private static bool GlobMatches(GlobPattern globPattern, SearchFileTarget entry, bool useRelativePath)
        => useRelativePath
            ? globPattern.IsMatch(entry.RelativePath)
            : globPattern.IsMatch(entry.Name);

    // 函数功能：从 grep 工具参数中解析搜索目标路径列表；path 可以是字符串或字符串数组，省略时默认为工作目录；解析失败时通过 errorMessage 返回原因
    private static bool TryResolveGrepTargetPaths(
        string? workingDirectory,
        JsonElement arguments,
        out string[] targetPaths,
        [NotNullWhen(false)] out string? errorMessage)
    {
        targetPaths = [];
        errorMessage = null;

        if (!arguments.TryGetProperty("path", out var pathElement) || pathElement.ValueKind == JsonValueKind.Null)
        {
            targetPaths = [ResolvePath(workingDirectory, null)];
            return true;
        }

        if (pathElement.ValueKind == JsonValueKind.String)
        {
            targetPaths = [ResolvePath(workingDirectory, pathElement.GetString())];
            return true;
        }

        if (pathElement.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "The 'path' field must be a string or an array of strings.";
            return false;
        }

        var paths = new List<string>();
        foreach (var item in pathElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errorMessage = "The 'path' field must be a string or an array of strings.";
                return false;
            }

            var path = item.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "The 'path' array must not contain empty paths.";
                return false;
            }

            paths.Add(ResolvePath(workingDirectory, path));
        }

        if (paths.Count == 0)
        {
            errorMessage = "The 'path' array must include at least one path.";
            return false;
        }

        targetPaths = DeduplicatePaths(paths);
        return true;
    }

    // 函数功能：从 grep 工具参数中解析 glob 过滤模式列表；glob 可以是字符串或字符串数组，省略时返回空数组；解析失败时通过 errorMessage 返回原因
    private static bool TryGetGrepGlobPatterns(
        JsonElement arguments,
        out SearchGlobPattern[] globPatterns,
        [NotNullWhen(false)] out string? errorMessage)
    {
        globPatterns = [];
        errorMessage = null;

        if (!arguments.TryGetProperty("glob", out var globElement) || globElement.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        var patterns = new List<SearchGlobPattern>();
        if (globElement.ValueKind == JsonValueKind.String)
        {
            if (!TryAddGrepGlobPattern(globElement.GetString(), patterns, out errorMessage))
            {
                return false;
            }

            globPatterns = patterns.ToArray();
            return true;
        }

        if (globElement.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "The 'glob' field must be a string or an array of strings.";
            return false;
        }

        foreach (var item in globElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errorMessage = "The 'glob' field must be a string or an array of strings.";
                return false;
            }

            if (!TryAddGrepGlobPattern(item.GetString(), patterns, out errorMessage))
            {
                return false;
            }
        }

        globPatterns = patterns.ToArray();
        return true;
    }

    // 函数功能：解析单个 glob 字符串并追加到模式列表；含路径分隔符时按相对路径匹配，否则按文件名匹配；glob 为空时直接返回 true
    private static bool TryAddGrepGlobPattern(
        string? glob,
        List<SearchGlobPattern> patterns,
        [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(glob))
        {
            return true;
        }

        var parseResult = GlobPattern.TryParse(glob);
        if (!parseResult.Success)
        {
            errorMessage = $"Invalid glob pattern '{glob}'.";
            return false;
        }

        patterns.Add(new SearchGlobPattern(parseResult.Pattern!, glob.Contains('/') || glob.Contains('\\')));
        return true;
    }

    // 函数功能：对路径列表去重（Windows 不区分大小写，Unix 区分大小写），保持原有顺序，返回唯一路径数组
    private static string[] DeduplicatePaths(IReadOnlyList<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var uniquePaths = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (seen.Add(path))
            {
                uniquePaths.Add(path);
            }
        }

        return uniquePaths.ToArray();
    }

    // 函数功能：枚举 grep 搜索目标文件；若 targetPath 是文件则直接返回该文件，否则遍历 directoryEntries 中的非目录条目
    private static IEnumerable<SearchFileTarget> EnumerateSearchFiles(
        string targetPath,
        IEnumerable<FileTreeEntry>? directoryEntries)
    {
        if (File.Exists(targetPath))
        {
            var fileName = Path.GetFileName(targetPath);
            yield return new SearchFileTarget(targetPath, fileName, fileName, fileName);
            yield break;
        }

        foreach (var entry in directoryEntries ?? [])
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            yield return new SearchFileTarget(entry.FullPath, entry.RelativePath, entry.RelativePath, entry.Name);
        }
    }

    // 函数功能：逐行读取文件并用正则匹配，将命中行以 "路径:行号: 内容" 格式追加到 matches；达到 maxMatches 后停止
    private static void SearchFileLines(
        string fullPath,
        string displayPath,
        Regex regex,
        int maxMatches,
        List<string> matches,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(fullPath);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (!regex.IsMatch(line))
            {
                continue;
            }

            matches.Add($"{displayPath}:{lineNumber}: {line}");
            if (matches.Count >= maxMatches)
            {
                break;
            }
        }
    }

    // 函数功能：根据操作系统和 login 参数构建 shell 进程规格；Windows 使用 pwsh -NoProfile，Unix 使用 $SHELL（bash/zsh 支持 -l 登录 shell）
    private static ShellProcessSpec CreateShellProcessSpec(string command, string workdir, bool login)
    {
        if (OperatingSystem.IsWindows())
        {
            var fileName = "pwsh";
            // Always suppress the user profile on Windows so prompt theming and other profile-time output
            // cannot leak ANSI/control sequences into tool results. The login flag is Unix-oriented here.
            // Wrap the command so a failing final external command can propagate its native exit code
            // instead of PowerShell collapsing it to 1.
            string[] shellArguments = ["-NoProfile", "-Command", WrapWindowsShellCommand(command)];
            return new ShellProcessSpec(CreateProcessStartInfo(fileName, shellArguments, workdir));
        }

        var shellPath = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            shellPath = "/bin/sh";
        }

        var shellFileName = Path.GetFileName(shellPath);
        string[] arguments;
        if (login && shellFileName is "bash" or "zsh")
        {
            arguments = ["-lc", command];
        }
        else
        {
            arguments = ["-c", command];
        }

        return new ShellProcessSpec(CreateProcessStartInfo(shellPath, arguments, workdir));
    }

    // 函数功能：构建进程启动信息，重定向 stdout/stderr，关闭 Shell 执行，并设置 NO_COLOR/CLICOLOR 环境变量以禁止 ANSI 控制序列
    private static ProcessStartInfo CreateProcessStartInfo(string fileName, IReadOnlyList<string> arguments, string workdir)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["CLICOLOR"] = "0";
        startInfo.Environment["CLICOLOR_FORCE"] = "0";

        return startInfo;
    }

    // 函数功能：生成 read_file 工具的 JSON Schema，包含 path/offset/limit 参数定义，limit 默认值和上限来自 options
    private static JsonElement CreateReadFileSchema(AgentBuiltInToolOptions options)
        => ParseSchema(
            $$"""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Path to the file to read." },
                "offset": { "type": "integer", "description": "1-based line offset. Use a negative value to count from the end (-1 is the last line). 0 is invalid. Offsets past EOF return an empty text result, and oversized negative offsets clamp to line 1." },
                "limit": { "type": "integer", "description": "Maximum number of lines to return. Defaults to {{options.DefaultReadFileLineLimit}} and is capped at {{options.MaxReadFileLineLimit}}.", "minimum": 1 }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """);

    // 函数功能：生成 list_dir 工具的 JSON Schema，仅包含可选的 path 参数
    private static JsonElement CreateListDirSchema()
        => ParseSchema(
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Directory to list. Defaults to the session working directory." }
              },
              "additionalProperties": false
            }
            """);

    // 函数功能：生成 grep 工具的 JSON Schema，包含 pattern/path/glob/caseSensitive/maxMatches 参数，maxMatches 默认值来自 options
    private static JsonElement CreateGrepSchema(AgentBuiltInToolOptions options)
        => ParseSchema(
            $$"""
            {
              "type": "object",
              "properties": {
                "pattern": { "type": "string", "description": "Regular expression to search within each line (.NET regex syntax)." },
                "path": { "type": ["string", "array"], "items": { "type": "string" }, "description": "File or directory, or array of files/directories, to search. Defaults to the session working directory." },
                "glob": { "type": ["string", "array"], "items": { "type": "string" }, "description": "Optional file-name glob or globs like *.cs. Multiple globs match when any glob matches. If a glob includes path separators, it is matched against each file's relative path." },
                "caseSensitive": { "type": "boolean", "description": "Whether matching is case-sensitive. Defaults to false." },
                "maxMatches": { "type": "integer", "description": "Maximum matches to return. Defaults to {{options.MaxGrepMatches}}.", "minimum": 1 }
              },
              "required": ["pattern"],
              "additionalProperties": false
            }
            """);

    // 函数功能：生成 webget 工具的 JSON Schema，包含 url/timeoutSeconds/rawHtml/includeHttpStatus 参数，超时默认值来自 options
    private static JsonElement CreateWebGetSchema(AgentBuiltInToolOptions options)
        => ParseSchema(
            $$"""
            {
              "type": "object",
              "properties": {
                "url": { "type": "string", "description": "Absolute http or https URL to fetch. On success, webget returns a text result containing the fetched body. By default, HTML responses are simplified to plain text; JSON and XML are returned as text bodies after webget's content-type and size checks." },
                "timeoutSeconds": { "type": "integer", "description": "Optional timeout override in seconds. Defaults to {{FormatSeconds(options.WebGetTimeout)}} seconds.", "minimum": 1 },
                "rawHtml": { "type": "boolean", "description": "When true, return raw HTML markup instead of simplified plain text for HTML/XHTML responses. Defaults to false." },
                "includeHttpStatus": { "type": "boolean", "description": "When true, prefix the HTTP status line and response content type before the returned body text. Defaults to false." }
              },
              "required": ["url"],
              "additionalProperties": false
            }
            """);

    // 函数功能：生成 shell_command 工具的 JSON Schema，包含 command/workdir/timeoutMs/login 参数
    private static JsonElement CreateShellCommandSchema()
        => ParseSchema(
            """
            {
              "type": "object",
              "properties": {
                "command": { "type": "string", "description": "Shell command or short shell script to execute." },
                "workdir": { "type": "string", "description": "Optional working directory override. Defaults to the session working directory." },
                "timeoutMs": { "type": "integer", "description": "Optional timeout in milliseconds.", "minimum": 1 },
                "login": { "type": "boolean", "description": "Whether to use login-shell semantics on Unix shells that support it. Ignored on Windows, where pwsh always runs with -NoProfile for predictable output." }
              },
              "required": ["command"],
              "additionalProperties": false
            }
            """);

    // 函数功能：检查 Content-Type 是否为 webget 支持的类型（text/*、application/json、application/xml、application/xhtml+xml）
    private static bool IsSupportedWebContentType(string mediaType)
        => mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    // 函数功能：在响应体前拼接 HTTP 状态行和 content-type 头，用于 includeHttpStatus=true 时的输出格式化
    private static string FormatWebGetSuccessResponse(HttpResponseMessage response, string body)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP ").Append((int)response.StatusCode);
        if (!string.IsNullOrWhiteSpace(response.ReasonPhrase))
        {
            builder.Append(' ').Append(response.ReasonPhrase);
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            builder.AppendLine();
            builder.Append("content-type: ").Append(contentType);
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.Append(body);
        return builder.ToString();
    }

    // 函数功能：在 Windows pwsh 命令末尾追加退出码传播代码，使失败的外部命令能正确传播原生 exit code 而非被 PowerShell 折叠为 1
    private static string WrapWindowsShellCommand(string command)
    {
        const string postlude =
            """
            if (-not $?) {
                if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) {
                    exit $LASTEXITCODE
                }

                exit 1
            }
            """;

        return string.Concat(
            command,
            command.EndsWith('\n') ? string.Empty : Environment.NewLine,
            postlude);
    }

    // 函数功能：将 TimeSpan 格式化为最多三位小数的秒数字符串（不变区域），用于工具描述中的超时显示
    private static string FormatSeconds(TimeSpan value)
        => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    // 函数功能：将 shell 命令的 exitCode/workdir/stdout/stderr 格式化为结构化文本，空输出替换为 "(empty)"
    private static string FormatShellCommandOutput(int exitCode, string stdout, string stderr, string workdir)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"exit_code: {exitCode}");
        builder.AppendLine($"working_directory: {workdir}");
        builder.AppendLine("stdout:");
        builder.AppendLine(string.IsNullOrEmpty(stdout) ? "(empty)" : stdout);
        builder.AppendLine("stderr:");
        builder.Append(string.IsNullOrEmpty(stderr) ? "(empty)" : stderr);
        return builder.ToString().TrimEnd();
    }

    // 函数功能：异步逐行读取进程输出流（stdout 或 stderr），拼入 StringBuilder，并通过 progress 回调实时推送每一行给调用方
    private static async Task<string> PumpProcessStreamAsync(
        StreamReader reader,
        string streamName,
        StringBuilder builder,
        AgentToolProgressHandler? progress,
        CancellationToken cancellationToken)
    {
        string? line;
        var firstLine = true;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (!firstLine)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            firstLine = false;

            if (progress is not null)
            {
                await progress(
                        new AgentToolProgressUpdate(
                            line + Environment.NewLine,
                            CreateShellStreamDetails(streamName)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return builder.ToString();
    }

    // 函数功能：构建包含 stream 字段名称的 JSON 对象（JsonElement），用于 PumpProcessStreamAsync 的进度推送 details
    private static JsonElement CreateShellStreamDetails(string streamName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("stream", streamName);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    // 函数功能：尝试终止进程及其整个进程树，忽略进程已退出或无效操作的异常
    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    // 函数功能：从 JSON 参数中读取必填字符串属性，值为空或空白时抛出 ArgumentException
    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"The '{propertyName}' field is required.")
            : value;
    }

    // 函数功能：从 JSON 参数中读取必填字符串属性，允许空字符串但属性缺失时抛出 ArgumentException（与 GetRequiredString 的区别在于不校验空白）
    private static string GetRequiredStringValue(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        return value is null
            ? throw new ArgumentException($"The '{propertyName}' field is required.")
            : value;
    }

    // 函数功能：从 JSON 参数中读取可选字符串属性，属性不存在或非字符串类型时返回 null
    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    // 函数功能：从 JSON 参数中读取可选整数属性，属性不存在、非数字或无法转为 int32 时返回 null
    private static int? GetOptionalInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    // 函数功能：从 JSON 参数中读取可选布尔属性，属性不存在或非布尔类型时返回 null
    private static bool? GetOptionalBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    // 函数功能：将用户输入答案字典序列化为 JSON 字符串，用于 request_user_input 工具的返回值
    private static string SerializeAnswers(IReadOnlyDictionary<string, string> answers)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var answer in answers)
            {
                writer.WriteString(answer.Key, answer.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // 类型：封装 shell 进程的 ProcessStartInfo，用于统一传递进程启动参数
    private sealed record ShellProcessSpec(ProcessStartInfo StartInfo);

    // 类型：工作区路径解析结果，包含完整绝对路径和相对于工作区根目录的展示路径
    private readonly record struct WorkspacePathResolution(string FullPath, string DisplayPath);

    // 类型：grep 搜索用的 glob 模式，附带是否按相对路径匹配的标志
    private readonly record struct SearchGlobPattern(GlobPattern Pattern, bool UseRelativePath);

    // 类型：grep 搜索的文件目标，包含完整路径、展示路径、相对路径及文件名
    private readonly record struct SearchFileTarget(string FullPath, string DisplayPath, string RelativePath, string Name);
}
