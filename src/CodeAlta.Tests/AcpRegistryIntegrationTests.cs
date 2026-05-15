using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CodeAlta.Acp;
using CodeAlta.App;
using CodeAlta.Catalog;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpRegistryIntegrationTests
{
    [TestMethod]
    public async Task RegistryClient_LoadFromFileAsync_ParsesManifest()
    {
        using var temp = TempDirectory.Create();
        var registryPath = Path.Combine(temp.Path, "registry.json");
        await File.WriteAllTextAsync(
            registryPath,
            """
            {
              "version": "1.0.0",
              "agents": [
                {
                  "id": "sample-agent",
                  "name": "Sample Agent",
                  "version": "1.2.3",
                  "description": "Test agent",
                  "distribution": {
                    "npx": {
                      "package": "@sample/agent@1.2.3",
                      "args": ["--acp"]
                    }
                  }
                }
              ],
              "extensions": []
            }
            """);

        using var client = new AcpRegistryClient();
        var registry = await client.LoadFromFileAsync(registryPath);

        Assert.AreEqual("1.0.0", registry.Version);
        Assert.AreEqual(1, registry.Agents.Count);
        Assert.AreEqual("sample-agent", registry.Agents[0].Id);
        Assert.AreEqual("@sample/agent@1.2.3", registry.Agents[0].Distribution.Npx!.Package);
    }

    [TestMethod]
    public void AuthMethodConverter_DeserializesAuthMethodList()
    {
        var methods = JsonSerializer.Deserialize<List<AuthMethod>>(
            """
            [
              {
                "id": "agent-auth",
                "name": "Agent Auth"
              },
              {
                "type": "env_var",
                "id": "env-auth",
                "name": "Env Auth",
                "vars": [
                  {
                    "name": "OPENAI_API_KEY"
                  }
                ]
              }
            ]
            """,
            AcpClient.CreateJsonSerializerOptions());

        Assert.IsNotNull(methods);
        Assert.AreEqual(2, methods.Count);
        Assert.IsInstanceOfType<AuthMethodAgent>(methods[0]);
        Assert.IsInstanceOfType<AuthMethodEnvVar>(methods[1]);
    }

    [TestMethod]
    public void InstallResolver_Resolve_PrefersBinaryForCurrentTarget()
    {
        var manifest = CreateBinaryManifest();
        var resolver = new AcpInstallResolver();

        var plan = resolver.Resolve(manifest);

        Assert.AreEqual(AcpInstallKind.Binary, plan.Kind);
        Assert.AreEqual(AcpInstallResolver.GetCurrentTargetId(), plan.TargetId);
        Assert.AreEqual("./agent.exe", plan.RelativeCommandPath);
    }

    [TestMethod]
    public async Task Installer_InstallAsync_BinaryExtractsArchiveAndResolvesCommand()
    {
        using var temp = TempDirectory.Create();
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                CreateZipArchiveBytes(("dist/agent.exe", "echo ok")),
                new Uri("https://example.test/agent.zip")));
        using var installer = new AcpInstaller(httpClient);
        var manifest = CreateBinaryManifest();
        var plan = new AcpInstallPlan
        {
            Manifest = manifest,
            Kind = AcpInstallKind.Binary,
            Command = "./dist/agent.exe",
            RelativeCommandPath = "./dist/agent.exe",
            ArchiveUri = new Uri("https://example.test/agent.zip"),
            TargetId = AcpInstallResolver.GetCurrentTargetId(),
        };

        var resolved = await installer.InstallAsync(
            plan,
            Path.Combine(temp.Path, "downloads"),
            Path.Combine(temp.Path, "installs"));

        Assert.AreEqual(AcpInstallKind.Binary, resolved.Kind);
        Assert.IsTrue(File.Exists(resolved.Command), resolved.Command);
        Assert.IsTrue(Path.IsPathRooted(resolved.Command));
        Assert.IsTrue(Directory.Exists(resolved.InstallRoot!));
    }

    [TestMethod]
    public async Task AgentRegistryService_InstallAgentAsync_PersistsDefinition()
    {
        using var temp = TempDirectory.Create();
        var targetId = AcpInstallResolver.GetCurrentTargetId();
        var commandFileName = OperatingSystem.IsWindows() ? "agent.exe" : "agent";
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            $$"""
            {
              "version": "1.0.0",
              "agents": [
                {
                  "id": "registry-agent",
                  "name": "Registry Agent",
                  "version": "2.0.0",
                  "description": "Installed from test registry",
                  "distribution": {
                    "binary": {
                      "{{targetId}}": {
                        "archive": "https://example.test/registry-agent.zip",
                        "cmd": "./{{commandFileName}}",
                        "args": ["acp"]
                      }
                    }
                  }
                }
              ],
              "extensions": []
            }
            """,
            new Uri("https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json"),
            CreateZipArchiveBytes((commandFileName, "echo registry")),
            new Uri("https://example.test/registry-agent.zip")));

        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var store = new AcpInstalledBackendStore(catalogOptions);
        using var service = new AcpAgentRegistryService(
            catalogOptions,
            store,
            new AcpRegistryClient(httpClient),
            new AcpInstallResolver(),
            new AcpInstaller(httpClient));

        var definition = await service.InstallAgentAsync("registry-agent");
        var installed = store.Load();

        Assert.AreEqual("registry-agent", definition.AgentId);
        Assert.AreEqual("Registry Agent", definition.DisplayName);
        Assert.AreEqual(1, installed.Count);
        Assert.AreEqual(definition.AgentId, installed[0].AgentId);
        Assert.IsTrue(File.Exists(installed[0].Command));
    }

    [TestMethod]
    public void InstalledBackendStore_SaveAndLoad_RoundTripsDefinition()
    {
        using var temp = TempDirectory.Create();
        var store = new AcpInstalledBackendStore(new CatalogOptions { GlobalRoot = temp.Path });
        var definition = new AcpBackendDefinition
        {
            AgentId = "sample-agent",
            DisplayName = "Sample Agent",
            Command = @"C:\tools\sample-agent.exe",
            Arguments = ["acp"],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SAMPLE_MODE"] = "1"
            }
        };

        store.Save(definition);
        var loaded = store.Load();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("sample-agent", loaded[0].AgentId);
        Assert.AreEqual("Sample Agent", loaded[0].DisplayName);
        Assert.AreEqual(@"C:\tools\sample-agent.exe", loaded[0].Command);
        CollectionAssert.AreEqual(new[] { "acp" }, loaded[0].Arguments);
    }

    private static AcpRegistryAgentManifest CreateBinaryManifest()
    {
        return new AcpRegistryAgentManifest
        {
            Id = "binary-agent",
            Name = "Binary Agent",
            Version = "1.0.0",
            Description = "Binary test agent",
            Distribution = new AcpRegistryDistribution
            {
                Binary = new Dictionary<string, AcpRegistryBinaryPackage>(StringComparer.OrdinalIgnoreCase)
                {
                    [AcpInstallResolver.GetCurrentTargetId()] = new AcpRegistryBinaryPackage
                    {
                        Archive = "https://example.test/agent.zip",
                        Command = "./agent.exe",
                    }
                },
                Npx = new AcpRegistryPackageDistribution
                {
                    Package = "@sample/binary-agent@1.0.0",
                },
            },
        };
    }

    private static byte[] CreateZipArchiveBytes(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<Uri, byte[]> _responses;

        public StubHttpMessageHandler(byte[] defaultResponse)
            : this(defaultResponse, new Uri("https://example.test/")) { }

        public StubHttpMessageHandler(byte[] defaultResponse, Uri defaultUri)
        {
            _responses = new Dictionary<Uri, byte[]>
            {
                [defaultUri] = defaultResponse,
            };
        }

        public StubHttpMessageHandler(string content, Uri uri, byte[] archiveContent, Uri archiveUri)
        {
            _responses = new Dictionary<Uri, byte[]>
            {
                [uri] = System.Text.Encoding.UTF8.GetBytes(content),
                [archiveUri] = archiveContent,
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_responses.TryGetValue(request.RequestUri!, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                });
        }
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codealta-acp-tests-{Guid.NewGuid():N}");
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
