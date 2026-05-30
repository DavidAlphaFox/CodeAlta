using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime.SystemPrompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SystemPromptInfrastructureTests
{
    [TestMethod]
    public void UserPromptCatalog_ListsBuiltInGlobalProjectAndMarksOverrides()
    {
        using var temp = TempDirectory.Create();
        var appBase = Path.Combine(temp.Path, "app");
        var globalRoot = Path.Combine(temp.Path, "global");
        var projectRoot = Path.Combine(temp.Path, "project");
        WritePrompt(appBase, "default", "Default Built-in", "default", "Built-in body.");
        WritePrompt(globalRoot, "default", "Default Global", "default", "Global body.");
        WritePrompt(globalRoot, "global-extra", "Global Extra", "default", "Global extra body.");
        WritePrompt(projectRoot, "default", "Default Project", "project-system", "Project body.");

        var catalog = new UserPromptCatalog(new FileSystemPromptContentLocator(appBase));
        var allPrompts = catalog.ListPrompts(new UserPromptCatalogQuery
        {
            UserCodeAltaRoot = globalRoot,
            ProjectRoot = projectRoot,
            ProjectPromptResourcesTrusted = true,
        });

        CollectionAssert.AreEqual(
            new[]
            {
                UserPromptSourceKind.BuiltIn,
                UserPromptSourceKind.UserGlobal,
                UserPromptSourceKind.UserGlobal,
                UserPromptSourceKind.Project,
            },
            allPrompts.Select(static prompt => prompt.SourceKind).ToArray());
        Assert.IsTrue(allPrompts.Single(prompt => prompt.SourceKind == UserPromptSourceKind.BuiltIn).IsShadowed);
        Assert.IsTrue(allPrompts.Single(prompt => prompt.SourceKind == UserPromptSourceKind.UserGlobal && prompt.PromptName == "default").IsShadowed);

        var effectiveDefault = catalog.ResolvePrompt(
            new UserPromptCatalogQuery
            {
                UserCodeAltaRoot = globalRoot,
                ProjectRoot = projectRoot,
                ProjectPromptResourcesTrusted = true,
            },
            "default");

        Assert.IsNotNull(effectiveDefault);
        Assert.AreEqual(UserPromptSourceKind.Project, effectiveDefault.SourceKind);
        Assert.AreEqual("Default Project", effectiveDefault.DisplayName);
        Assert.AreEqual("project-system", effectiveDefault.SystemPromptName);
    }

    [TestMethod]
    public void SystemPromptBuilder_UsesSelectedUserPromptBodyAndSystemProperty()
    {
        using var temp = TempDirectory.Create();
        var appBase = Path.Combine(temp.Path, "app");
        var globalRoot = Path.Combine(temp.Path, "global");
        var projectRoot = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(projectRoot);
        WriteSystem(appBase, "default", "Built-in default system.");
        WritePrompt(appBase, "default", "Default", "default", "Built-in default prompt.");
        WriteSystem(projectRoot, "custom-system", "Project custom system.");
        WritePrompt(projectRoot, "review", "Review", "custom-system", "Review the current change set.");

        var builder = new SystemPromptBuilder(new FileSystemPromptContentLocator(appBase));
        var bundle = builder.Build(new SystemPromptBuildRequest
        {
            ProviderKey = "codex",
            ProviderType = "codex",
            ProtocolFamily = "codex",
            Session = new SessionViewDescriptor
            {
                SessionId = "session-1",
                ProviderId = "codex",
                ProviderKey = "codex",
                WorkingDirectory = projectRoot,
                Kind = SessionViewKind.ProjectSession,
            },
            Project = new ProjectDescriptor
            {
                Id = "project-1",
                Slug = "project-1",
                DisplayName = "Project 1",
                ProjectPath = projectRoot,
            },
            UserCodeAltaRoot = globalRoot,
            SelectedPromptName = "review",
            PartOptionsOverride = new PartialSystemPromptPartOptions(
                Skills: false,
                ProjectContext: false,
                RuntimeContext: false,
                ToolGuidance: false),
        });

        Assert.AreEqual("Project custom system.", bundle.SystemMessage);
        StringAssert.Contains(bundle.DeveloperInstructions!, "# User Prompt");
        StringAssert.Contains(bundle.DeveloperInstructions!, "Review the current change set.");
        Assert.AreEqual("custom-system", bundle.Manifest.Template.BaseName);
        Assert.AreEqual("review", bundle.Manifest.Template.InstructionName);
        Assert.IsTrue(bundle.Manifest.Parts.Any(static part => part.Key == "prompts/review" && part.Status == "selected"));
    }

    private static void WriteSystem(string root, string id, string body)
    {
        var directory = root.EndsWith("project", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, ".alta", "instructions", "system")
            : Path.Combine(root, "content", "instructions", "system");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".system-prompt.md"), $"""
            ---
            description: Test system prompt.
            ---
            {body}
            """);
    }

    private static void WritePrompt(string root, string id, string name, string system, string body)
    {
        var directory = root.EndsWith("app", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, "content", "instructions", "prompts")
            : root.EndsWith("project", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(root, ".alta", "instructions", "prompts")
                : Path.Combine(root, "instructions", "prompts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".prompt.md"), $"""
            ---
            name: {name}
            system: {system}
            description: Test user prompt.
            ---
            {body}
            """);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codealta-orchestration-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
