using System.IO.Compression;
using CodeAlta.Acp.Generator;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpGeneratorTests
{
    [TestMethod]
    public async Task ResolveAsync_LocalRepo_UsesSchemaFolder()
    {
        using var temp = TestTempDirectory.Create();
        var schemaDir = Path.Combine(temp.Path, "schema");
        Directory.CreateDirectory(schemaDir);
        await File.WriteAllTextAsync(Path.Combine(schemaDir, "schema.json"), CreateMinimalSchemaJson()).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(schemaDir, "meta.json"), CreateMinimalMetaJson()).ConfigureAwait(false);

        var source = await AcpSchemaSourceResolver.ResolveAsync(
                new GeneratorCliOptions(
                    schemaFile: null,
                    acpRepoDir: temp.Path,
                    zipFile: null,
                    githubRef: null,
                    AcpSurface.Stable,
                    "CodeAlta.Acp",
                    "agentclientprotocol/agent-client-protocol",
                    outputDir: null,
                    cacheDir: null,
                    forceDownload: false),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.AreEqual("local-repo", source.SourceKind);
        Assert.AreEqual(Path.Combine(schemaDir, "schema.json"), source.SchemaPath);
        Assert.AreEqual(Path.Combine(schemaDir, "meta.json"), source.MetaPath);
    }

    [TestMethod]
    public async Task ResolveAsync_LocalZip_ExtractsUnstableSchema()
    {
        using var temp = TestTempDirectory.Create();
        var zipPath = Path.Combine(temp.Path, "acp.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteArchiveFile(archive, "agent-client-protocol-v-test/schema/schema.unstable.json", CreateMinimalSchemaJson());
            WriteArchiveFile(archive, "agent-client-protocol-v-test/schema/meta.unstable.json", CreateMinimalMetaJson());
        }

        var source = await AcpSchemaSourceResolver.ResolveAsync(
                new GeneratorCliOptions(
                    schemaFile: null,
                    acpRepoDir: null,
                    zipFile: zipPath,
                    githubRef: null,
                    AcpSurface.Unstable,
                    "CodeAlta.Acp",
                    "agentclientprotocol/agent-client-protocol",
                    outputDir: null,
                    cacheDir: null,
                    forceDownload: false),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.AreEqual("local-zip", source.SourceKind);
        Assert.AreEqual("schema.unstable.json", Path.GetFileName(source.SchemaPath));
        Assert.AreEqual("meta.unstable.json", Path.GetFileName(source.MetaPath));
        Assert.IsTrue(Directory.Exists(source.WorkingRoot));
    }

    [TestMethod]
    public async Task Emitter_ProducesExpectedMethodAndMetadataOutput()
    {
        using var temp = TestTempDirectory.Create();
        var schemaPath = Path.Combine(temp.Path, "schema.json");
        var metaPath = Path.Combine(temp.Path, "meta.json");
        await File.WriteAllTextAsync(schemaPath, CreateMinimalSchemaJson()).ConfigureAwait(false);
        await File.WriteAllTextAsync(metaPath, CreateMinimalMetaJson()).ConfigureAwait(false);

        var definitions = await SchemaWalker.LoadDefinitionsAsync(schemaPath, "CodeAlta.Acp").ConfigureAwait(false);
        var emitter = new CSharpEmitter(definitions, "CodeAlta.Acp");
        var filesByNamespace = emitter.EmitAll(definitions);
        var methodMap = AcpMethodMap.Load(metaPath);

        Assert.AreEqual(2, filesByNamespace["CodeAlta.Acp"].Count);
        var pingRequest = filesByNamespace["CodeAlta.Acp"].Single(static file => file.FileName == "PingRequest.gen.cs").Content;
        var methods = AcpMethodEmitter.EmitClientMethods(definitions, methodMap, "CodeAlta.Acp");
        var metadata = AcpMethodEmitter.EmitMetadataPartial(
            "CodeAlta.Acp",
            new AcpSchemaSourceInfo(
                "local-file",
                schemaPath,
                "https://github.com/agentclientprotocol/agent-client-protocol",
                "v-test",
                schemaPath,
                metaPath,
                temp.Path),
            AcpSurface.Stable,
            methodMap.Version);

        StringAssert.Contains(pingRequest, "public sealed partial record PingRequest");
        StringAssert.Contains(pingRequest, "[JsonPropertyName(\"message\")]");
        StringAssert.Contains(methods, "public Task<PingResponse> PingAsync(");
        StringAssert.Contains(methods, "return _transport.SendRequestAsync<PingRequest, PingResponse>(\"session/ping\"");
        StringAssert.Contains(metadata, "CompiledAgainstSchemaSourceKind = \"local-file\"");
        StringAssert.Contains(metadata, "CompiledAgainstGitRef = \"v-test\"");
        StringAssert.Contains(metadata, "CompiledAgainstMethodMapVersion = 7");
    }

    private static void WriteArchiveFile(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string CreateMinimalSchemaJson()
    {
        return """
               {
                 "$defs": {
                   "PingRequest": {
                     "type": "object",
                     "x-method": "session/ping",
                     "x-side": "agent",
                     "properties": {
                       "message": {
                         "type": "string"
                       }
                     },
                     "required": ["message"]
                   },
                   "PingResponse": {
                     "type": "object",
                     "x-method": "session/ping",
                     "x-side": "agent",
                     "properties": {
                       "ok": {
                         "type": "boolean"
                       }
                     },
                     "required": ["ok"]
                   }
                 }
               }
               """;
    }

    private static string CreateMinimalMetaJson()
    {
        return """
               {
                 "version": 7,
                 "agentMethods": {
                   "ping": "session/ping"
                 },
                 "clientMethods": {}
               }
               """;
    }
}
