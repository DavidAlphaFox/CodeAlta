using CodeAlta.CodexSdk.Generator;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodexSdkGeneratorTests
{
    [TestMethod]
    public async Task OutputDirectoryCleaner_CanDeleteReadOnlyFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CodeAlta.Tests.{Guid.NewGuid():N}");
        var output = Path.Combine(root, "generated");
        Directory.CreateDirectory(output);

        var filePath = Path.Combine(output, "read-only.gen.cs");
        await File.WriteAllTextAsync(filePath, "// test").ConfigureAwait(false);
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        try
        {
            await OutputDirectoryCleaner.CleanAsync(output).ConfigureAwait(false);
            Assert.IsFalse(Directory.Exists(output));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [TestMethod]
    public async Task SchemaWalker_AddsAliasWhenRootRefTargetsOnlyExistInV2()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CodeAlta.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var schemaPath = Path.Combine(root, "schema.json");

        await File.WriteAllTextAsync(
                schemaPath,
                """
                {
                  "$schema": "http://json-schema.org/draft-07/schema#",
                  "title": "Test",
                  "type": "object",
                  "definitions": {
                    "v2": {
                      "ThreadId": { "type": "string" }
                    },
                    "Foo": {
                      "type": "object",
                      "properties": {
                        "thread_id": { "$ref": "#/definitions/ThreadId" }
                      }
                    }
                  }
                }
                """)
            .ConfigureAwait(false);

        try
        {
            var defs = await SchemaWalker.LoadDefinitionsAsync(
                    schemaPath,
                    "CodeAlta.CodexSdk")
                .ConfigureAwait(false);

            Assert.IsTrue(defs.Any(x => x.CsNamespace == "CodeAlta.CodexSdk.V2" && x.Name == "ThreadId"));
            Assert.IsTrue(defs.Any(x => x.CsNamespace == "CodeAlta.CodexSdk" && x.Name == "ThreadId"));
            Assert.IsTrue(defs.Any(x => x.Name == "Foo"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
