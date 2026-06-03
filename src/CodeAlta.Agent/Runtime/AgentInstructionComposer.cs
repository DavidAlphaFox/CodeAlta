using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodeAlta.Agent.Runtime;

// 模块功能：将会话选项、项目说明文件、已加载技能等组合成 Agent 系统指令包（AgentInstructionBundle）
internal static class AgentInstructionComposer
{
    // 函数功能：根据会话创建选项和已加载技能列表，组装完整指令包并计算内容哈希；返回 AgentInstructionBundle
    public static AgentInstructionBundle Compose(
        AgentSessionCreateOptions options,
        IReadOnlyList<AgentLoadedSkillState>? loadedSkills = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var systemMessage = Normalize(options.SystemMessage);
        var developerInstructionsInput = Normalize(options.DeveloperInstructions);
        var runtimeContext = ContainsSection(developerInstructionsInput, "# Runtime Context")
            ? string.Empty
            : BuildRuntimeContextSection(options.WorkingDirectory, options.ProjectRoots);
        var developerSections = new List<string>();
        if (!string.IsNullOrWhiteSpace(developerInstructionsInput))
        {
            developerSections.Add(developerInstructionsInput);
        }

        if (!ContainsSection(developerInstructionsInput, "# Project Context"))
        {
            foreach (var path in EnumerateAgentInstructionFiles(options.WorkingDirectory, options.ProjectRoots))
            {
                var content = File.ReadAllText(path).Trim();
                if (content.Length == 0)
                {
                    continue;
                }

                developerSections.Add(
                    $"""
                    File: {path}
                    {content}
                    """);
            }
        }

        if (!ContainsSection(developerInstructionsInput, "# Active Skills") &&
            !ContainsSection(developerInstructionsInput, "<active_skills>"))
        {
            var activeSkillsSection = BuildActiveSkillsSection(loadedSkills);
            if (!string.IsNullOrWhiteSpace(activeSkillsSection))
            {
                developerSections.Add(activeSkillsSection);
            }
        }

        var developerInstructions = developerSections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, developerSections);
        var hash = ComputeHash(systemMessage, developerInstructions, runtimeContext);
        return new AgentInstructionBundle(systemMessage, developerInstructions, runtimeContext, hash);
    }

    // 函数功能：去除字符串首尾空白；若为空白字符串则返回 null
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // 函数功能：检查指令文本中是否已包含指定标记节（不区分大小写），用于避免重复注入
    private static bool ContainsSection(string? value, string marker)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(marker, StringComparison.OrdinalIgnoreCase);

    // 函数功能：构建运行时上下文信息段，包含当前日期、平台、默认 Shell、工作目录及项目根目录
    private static string BuildRuntimeContextSection(string? workingDirectory, IReadOnlyList<string> projectRoots)
    {
        var lines = new List<string>
        {
            $"Current date: {DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"Platform: {GetPlatformLabel()}",
            $"Default shell for `shell_command`: `{GetDefaultShellLabel()}`",
        };

        var normalizedWorkingDirectory = NormalizePath(workingDirectory);
        if (normalizedWorkingDirectory is not null)
        {
            lines.Add($"Current working directory: `{normalizedWorkingDirectory}`");
        }

        var normalizedProjectRoots = projectRoots
            .Select(NormalizePath)
            .Where(static path => path is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        if (normalizedProjectRoots.Length > 0)
        {
            lines.Add(
                normalizedProjectRoots.Length == 1
                    ? $"Project root: `{normalizedProjectRoots[0]}`"
                    : $"Project roots: {string.Join(", ", normalizedProjectRoots.Select(static root => $"`{root}`"))}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    // 函数功能：从工作目录及项目根目录的各级父目录中查找 AGENTS.md / CLAUDE.md 等指令文件，返回有序列表（外层优先）
    private static IReadOnlyList<string> EnumerateAgentInstructionFiles(string? workingDirectory, IReadOnlyList<string> projectRoots)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateRelativePaths = new[]
        {
            "AGENTS.md",
            "CLAUDE.md",
            Path.Combine(".github", "copilot-instructions.md"),
        };

        void AddWalk(string? root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            var current = Path.GetFullPath(root);
            var stack = new Stack<string>();
            while (!string.IsNullOrWhiteSpace(current))
            {
                stack.Push(current);
                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }

            while (stack.Count > 0)
            {
                var directory = stack.Pop();
                var selectedFile = candidateRelativePaths
                    .Select(relativePath => Path.Combine(directory, relativePath))
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(static file => file.Length)
                    .ThenBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (selectedFile is not null && seen.Add(selectedFile.FullName))
                {
                    files.Add(selectedFile.FullName);
                }
            }
        }

        AddWalk(workingDirectory);
        foreach (var projectRoot in projectRoots)
        {
            AddWalk(projectRoot);
        }

        return files;
    }

    // 函数功能：将路径标准化为绝对路径并去除末尾分隔符；空路径返回 null
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    // 函数功能：返回当前操作系统平台名称字符串（Windows/macOS/Linux 或 OS 描述）
    private static string GetPlatformLabel()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return RuntimeInformation.OSDescription.Trim();
    }

    // 函数功能：返回当前平台默认 Shell（Windows 为 pwsh，其他读取 SHELL 环境变量，缺省 /bin/sh）
    private static string GetDefaultShellLabel()
    {
        if (OperatingSystem.IsWindows())
        {
            return "pwsh";
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        return string.IsNullOrWhiteSpace(shell)
            ? "/bin/sh"
            : shell.Trim();
    }

    // 函数功能：对系统消息、开发者指令、运行时上下文拼接后计算 SHA-256 哈希，返回十六进制字符串
    private static string ComputeHash(string? systemMessage, string? developerInstructions, string? runtimeContext)
    {
        var payload = $"{systemMessage ?? string.Empty}\n---\n{developerInstructions ?? string.Empty}\n---\n{runtimeContext ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    // 函数功能：将已激活技能列表格式化为 <active_skills> XML 块；无技能时返回 null
    private static string? BuildActiveSkillsSection(IReadOnlyList<AgentLoadedSkillState>? loadedSkills)
    {
        if (loadedSkills is not { Count: > 0 })
        {
            return null;
        }

        var orderedSkills = loadedSkills
            .OrderBy(static skill => skill.ActivatedAt)
            .ThenBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("The following skills are already active in this session. Treat them as loaded host-managed context.");
        builder.AppendLine("Use the skill root when resolving relative paths mentioned by a loaded skill.");
        builder.AppendLine();
        builder.AppendLine("<active_skills>");

        foreach (var skill in orderedSkills)
        {
            if (!skill.IsAvailable)
            {
                builder.Append("  <skill_missing name=\"")
                    .Append(EscapeXml(skill.Name))
                    .Append("\" path=\"")
                    .Append(EscapeXml(skill.SkillFilePath))
                    .Append("\">")
                    .Append(EscapeXml(skill.MissingReason ?? "Skill content was restored from session history but the on-disk skill is no longer available."))
                    .AppendLine("</skill_missing>");
            }

            builder.AppendLine(skill.Payload.Trim());
        }

        builder.AppendLine("</active_skills>");
        return builder.ToString().Trim();
    }

    // 函数功能：对字符串进行 XML 特殊字符转义（&、<、>、"），用于安全嵌入 XML 属性或文本
    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}

// 类型：不可变的指令包，包含系统消息、开发者指令、运行时上下文及其内容哈希
internal sealed record AgentInstructionBundle(
    string? SystemMessage,
    string? DeveloperInstructions,
    string RuntimeContext,
    string InstructionHash);
