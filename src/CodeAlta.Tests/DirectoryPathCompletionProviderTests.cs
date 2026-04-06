using System.Reflection;
using CodeAlta.Catalog;
using CodeAlta.Views;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;

namespace CodeAlta.Tests;

[TestClass]
public sealed class DirectoryPathCompletionProviderTests
{
    [TestMethod]
    public void GetSuggestions_ListsMatchingDirectoriesWithinParent()
    {
        var root = Path.Combine(Path.GetTempPath(), "codealta-open-folder-tests", Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "Repos");
        Directory.CreateDirectory(Path.Combine(parent, "CodeAlta"));
        Directory.CreateDirectory(Path.Combine(parent, "Codex"));
        Directory.CreateDirectory(Path.Combine(parent, "Other"));

        try
        {
            var provider = new DirectoryPathCompletionProvider(root);
            var input = Path.Combine(parent, "Cod");

            var result = provider.GetSuggestions(input);

            CollectionAssert.AreEqual(
                new[]
                {
                    new OpenProjectSuggestion(OpenProjectSuggestionKind.Directory, Path.Combine(parent, "CodeAlta") + Path.DirectorySeparatorChar, Path.Combine(parent, "CodeAlta") + Path.DirectorySeparatorChar),
                    new OpenProjectSuggestion(OpenProjectSuggestionKind.Directory, Path.Combine(parent, "Codex") + Path.DirectorySeparatorChar, Path.Combine(parent, "Codex") + Path.DirectorySeparatorChar),
                },
                result.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void GetSuggestions_BlankInput_IncludesProjectsBeforeRoots()
    {
        var provider = new DirectoryPathCompletionProvider(
            Environment.CurrentDirectory,
            projects: () => [CreateProject("codealta", "CodeAlta")]);

        var result = provider.GetSuggestions(string.Empty);

        Assert.IsTrue(result.Count > 0);
        Assert.AreEqual(OpenProjectSuggestionKind.Project, result[0].Kind);
        Assert.AreEqual("CodeAlta", result[0].PrimaryText);
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "codealta"), result[0].SecondaryText);
        Assert.IsTrue(result.Any(static candidate => candidate.Kind == OpenProjectSuggestionKind.Directory));
    }

    [TestMethod]
    public void GetSuggestions_ProjectReference_UsesDisplayNameOnly()
    {
        var projects = new[]
        {
            CreateProject("codealta", "CodeAlta"),
            CreateProject("codex-cli", "Workbench"),
            CreateProject("other", "Other"),
        };
        var provider = new DirectoryPathCompletionProvider(
            Environment.CurrentDirectory,
            projects: () => projects);

        var result = provider.GetSuggestions("Cod");

        CollectionAssert.AreEqual(
            new[]
            {
                new OpenProjectSuggestion(OpenProjectSuggestionKind.Project, "CodeAlta", "CodeAlta", Path.Combine(Path.GetTempPath(), "codealta")),
            },
            result.ToArray());
    }

    [TestMethod]
    public void GetSuggestions_HiddenProjects_AreExcludedUntilIncludeHiddenIsEnabled()
    {
        var includeHidden = false;
        var hiddenProject = CreateProject("hidden-project", "Hidden Project");
        hiddenProject.Archived = true;
        var provider = new DirectoryPathCompletionProvider(
            Environment.CurrentDirectory,
            includeHidden: () => includeHidden,
            projects: () => [CreateProject("codealta", "CodeAlta"), hiddenProject]);

        var hiddenResult = provider.GetSuggestions("Hid");

        Assert.AreEqual(0, hiddenResult.Count);

        includeHidden = true;
        hiddenResult = provider.GetSuggestions("Hid");

        CollectionAssert.AreEqual(
            new[]
            {
                new OpenProjectSuggestion(OpenProjectSuggestionKind.Project, "Hidden Project", "Hidden Project", Path.Combine(Path.GetTempPath(), "hidden-project")),
            },
            hiddenResult.ToArray());
    }

    [TestMethod]
    public void Dialog_RendersProjectDisplayNameAndFolderPath()
    {
        const string projectPath = @"C:\repo\CodeAlta";
        var dialog = new DirectoryPathDialog(
            "Open Project",
            "Type a project name from the sidebar or a rooted folder path.",
            "Open",
            (_, _) => Task.CompletedTask,
            () => new XenoAtom.Terminal.UI.Geometry.Rectangle(0, 0, 120, 40),
            () => null,
            getProjects: () => [CreateProject("codealta", "CodeAlta", projectPath)]);

        using var terminalSession = Terminal.Open(new InMemoryTerminalBackend(new TerminalSize(120, 40)), new TerminalOptions { ImplicitStartInput = true }, force: true);
        var app = new TerminalApp(
            new TextBlock("host"),
            terminalSession.Instance,
            new TerminalAppOptions
            {
                HostKind = TerminalHostKind.Fullscreen,
            });

        InvokeTerminalApp(app, "BeginRun");
        try
        {
            dialog.Show();
            TickTerminalApp(app);

            var backend = (InMemoryTerminalBackend)terminalSession.Instance.Backend;
            var output = backend.GetOutText();
            StringAssert.Contains(output, "CodeAlta");
            StringAssert.Contains(output, projectPath);
            StringAssert.Contains(output, NerdFont.MdFolderOutline.ToString());
        }
        finally
        {
            InvokeTerminalApp(app, "EndRun");
        }
    }

    private static void InvokeTerminalApp(TerminalApp app, string methodName)
    {
        var method = typeof(TerminalApp).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(app, null);
    }

    private static void TickTerminalApp(TerminalApp app)
    {
        var method = typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(app, [null]);
    }

    private static ProjectDescriptor CreateProject(string slug, string displayName, string? projectPath = null)
    {
        return new ProjectDescriptor
        {
            Id = $"project-{slug}",
            Slug = slug,
            Name = displayName,
            DisplayName = displayName,
            ProjectPath = projectPath ?? Path.Combine(Path.GetTempPath(), slug),
            DefaultBranch = "main",
        };
    }
}
