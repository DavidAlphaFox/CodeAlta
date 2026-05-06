using System.Text;
using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Prompts;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class HeadlessPromptAttachmentServiceTests
{
    [TestMethod]
    public void Materialize_CreatesAgentAndPluginAttachmentsForHeadlessFiles()
    {
        var service = new HeadlessPromptAttachmentService();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var filePath = Path.Combine(root, "notes.txt");
            var imagePath = Path.Combine(root, "diagram.png");
            File.WriteAllText(filePath, "notes");
            File.WriteAllBytes(imagePath, [1, 2, 3]);

            var result = service.Materialize(
                "prompt",
                [
                    new WorkThreadPromptAttachment
                    {
                        AttachmentId = "file",
                        Path = filePath,
                        DisplayName = "Notes",
                        LineRange = new AgentLineRange(2, 4),
                    },
                    new WorkThreadPromptAttachment
                    {
                        AttachmentId = "directory",
                        Path = root,
                        DisplayName = "Root",
                    },
                    new WorkThreadPromptAttachment
                    {
                        AttachmentId = "image",
                        Path = imagePath,
                        DisplayName = "Diagram",
                    },
                ]);

            Assert.AreEqual(4, result.Input.Items.Count);
            Assert.IsInstanceOfType<AgentInputItem.Text>(result.Input.Items[0]);
            var file = Assert.IsInstanceOfType<AgentInputItem.File>(result.Input.Items[1]);
            Assert.AreEqual(filePath, file.Path);
            Assert.AreEqual("Notes", file.DisplayName);
            Assert.AreEqual(2, file.LineRange?.StartLine);
            var directory = Assert.IsInstanceOfType<AgentInputItem.Directory>(result.Input.Items[2]);
            Assert.AreEqual(root, directory.Path);
            var image = Assert.IsInstanceOfType<AgentInputItem.LocalImage>(result.Input.Items[3]);
            Assert.AreEqual(imagePath, image.Path);

            Assert.AreEqual(3, result.PluginAttachments.Count);
            Assert.AreEqual(PluginPromptAttachmentKind.File, result.PluginAttachments[0].Kind);
            Assert.AreEqual("file", result.PluginAttachments[0].Metadata["attachmentId"]);
            Assert.AreEqual("2", result.PluginAttachments[0].Metadata["startLine"]);
            Assert.AreEqual(PluginPromptAttachmentKind.Directory, result.PluginAttachments[1].Kind);
            Assert.AreEqual(PluginPromptAttachmentKind.Image, result.PluginAttachments[2].Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Materialize_CreatesSelectionAndInMemoryTextAttachments()
    {
        var service = new HeadlessPromptAttachmentService();
        var range = new AgentSelectionRange(new AgentPosition(10, 3), new AgentPosition(12, 8));

        var result = service.Materialize(
            "prompt",
            [
                new WorkThreadPromptAttachment
                {
                    AttachmentId = "selection",
                    Kind = WorkThreadPromptAttachmentKind.Selection,
                    Path = "src/Test.cs",
                    DisplayName = "selection",
                    Text = "selected",
                    SelectionRange = range,
                },
                new WorkThreadPromptAttachment
                {
                    AttachmentId = "text",
                    Content = Encoding.UTF8.GetBytes("inline"),
                    ContentType = "text/plain",
                },
            ]);

        Assert.AreEqual(3, result.Input.Items.Count);
        var selection = Assert.IsInstanceOfType<AgentInputItem.Selection>(result.Input.Items[1]);
        Assert.AreEqual("src/Test.cs", selection.FilePath);
        Assert.AreEqual("selected", selection.SelectedText);
        Assert.AreSame(range, selection.Range);
        var text = Assert.IsInstanceOfType<AgentInputItem.Text>(result.Input.Items[2]);
        Assert.AreEqual("inline", text.Value);
        Assert.AreEqual(PluginPromptAttachmentKind.Selection, result.PluginAttachments[0].Kind);
        Assert.AreEqual("10", result.PluginAttachments[0].Metadata["selectionStartLine"]);
        Assert.AreEqual(PluginPromptAttachmentKind.Text, result.PluginAttachments[1].Kind);
        Assert.AreEqual("inline", result.PluginAttachments[1].Text);
    }

    [TestMethod]
    public void Materialize_RejectsInvalidExplicitSelection()
    {
        var service = new HeadlessPromptAttachmentService();

        Assert.ThrowsExactly<ArgumentException>(() => service.Materialize(
            "prompt",
            [
                new WorkThreadPromptAttachment
                {
                    AttachmentId = "bad",
                    Kind = WorkThreadPromptAttachmentKind.Selection,
                    Path = "file.cs",
                    Text = "selected",
                },
            ]));
    }
}
