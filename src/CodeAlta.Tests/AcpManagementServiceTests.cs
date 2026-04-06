using CodeAlta.Acp;
using CodeAlta.Agent;
using CodeAlta.Agent.Acp;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpManagementServiceTests
{
    [TestMethod]
    public async Task LoadSnapshotAsync_ProjectsRegistryInstallConfigAndRuntimeState()
    {
        using var temp = TempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var installedStore = new AcpInstalledBackendStore(catalogOptions);
        var configStore = new CodeAltaConfigStore(catalogOptions);
        using var registryService = new AcpAgentRegistryService(catalogOptions, installedStore);
        using var registryClient = new AcpRegistryClient();

        await registryClient.SaveToFileAsync(
            registryService.RegistryCachePath,
            new AcpRegistryDocument
            {
                Version = "1.0.0",
                Agents =
                [
                    new AcpRegistryAgentManifest
                    {
                        Id = "sample-agent",
                        Name = "Sample Agent",
                        Version = "2.0.0",
                        Description = "Registry agent",
                        Repository = "https://example.test/repo",
                        Distribution = new AcpRegistryDistribution
                        {
                            Npx = new AcpRegistryPackageDistribution
                            {
                                Package = "@sample/agent@2.0.0",
                            },
                        },
                    },
                ],
            }).ConfigureAwait(false);

        installedStore.Save(new AcpBackendDefinition
        {
            AgentId = "sample-agent",
            DisplayName = "Installed Sample Agent",
            RegistryId = "sample-agent",
            Command = "npx",
            Arguments = ["--yes", "@sample/agent@2.0.0"],
        });
        configStore.SaveGlobalAcpBackendDefinition(new AcpBackendDefinition
        {
            AgentId = "sample-agent",
            DisplayName = "Configured Sample Agent",
            RegistryId = "sample-agent",
            Command = "npx",
            Arguments = ["--yes", "@sample/agent@2.0.0", "--debug"],
        });

        var runtimeState = new ChatBackendState(
            AcpAgentBackendFactoryExtensions.CreateBackendId("sample-agent"),
            "Configured Sample Agent")
        {
            Availability = ChatBackendAvailability.Ready,
            StatusMessage = "Connected · debug",
        };
        runtimeState.Models.Add(new AgentModelInfo("model-a", DisplayName: "Model A"));

        var service = new AcpManagementService(
            catalogOptions,
            registryService,
            configStore,
            installedStore,
            new Dictionary<string, ChatBackendState>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimeState.BackendId.Value] = runtimeState,
            });

        var snapshot = await service.LoadSnapshotAsync(refreshRegistry: false).ConfigureAwait(false);
        var item = snapshot.Items.Single(static candidate => candidate.AgentId == "sample-agent");

        Assert.AreEqual("1.0.0", snapshot.RegistryVersion);
        Assert.IsNotNull(snapshot.RegistryFetchedAtUtc);
        Assert.IsTrue(item.IsInRegistry);
        Assert.IsTrue(item.IsInstalled);
        Assert.IsTrue(item.HasConfiguration);
        Assert.IsTrue(item.IsEnabled);
        Assert.AreEqual("Configured Sample Agent", item.DisplayName);
        Assert.AreEqual("npx --yes @sample/agent@2.0.0 --debug", item.CommandSummary);
        Assert.AreEqual("Connected · debug", item.RuntimeStatus);
        Assert.AreEqual(1, item.RuntimeModelCount);
        CollectionAssert.AreEqual(new[] { "Model A" }, item.RuntimeModels.ToArray());
    }

    [TestMethod]
    public async Task LoadSnapshotAsync_IncludesManualConfiguredAgentsWithoutRegistry()
    {
        using var temp = TempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var installedStore = new AcpInstalledBackendStore(catalogOptions);
        var configStore = new CodeAltaConfigStore(catalogOptions);
        using var registryService = new AcpAgentRegistryService(catalogOptions, installedStore);

        configStore.SaveGlobalAcpBackendDefinition(new AcpBackendDefinition
        {
            AgentId = "manual-agent",
            DisplayName = "Manual Agent",
            Enabled = false,
            Command = @"C:\missing\manual-agent.exe",
        });

        var service = new AcpManagementService(
            catalogOptions,
            registryService,
            configStore,
            installedStore,
            new Dictionary<string, ChatBackendState>(StringComparer.OrdinalIgnoreCase));

        var snapshot = await service.LoadSnapshotAsync(refreshRegistry: false).ConfigureAwait(false);
        var item = snapshot.Items.Single(static candidate => candidate.AgentId == "manual-agent");

        Assert.IsFalse(item.IsInRegistry);
        Assert.IsFalse(item.IsInstalled);
        Assert.IsTrue(item.HasConfiguration);
        Assert.IsTrue(item.IsManual);
        Assert.IsFalse(item.IsEnabled);
        Assert.IsTrue(item.IsBroken);
    }

    [TestMethod]
    public async Task LoadSnapshotAsync_BuildsInstallCommandPreviewForRegistryOnlyAgent()
    {
        using var temp = TempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var installedStore = new AcpInstalledBackendStore(catalogOptions);
        var configStore = new CodeAltaConfigStore(catalogOptions);
        using var registryService = new AcpAgentRegistryService(catalogOptions, installedStore);
        using var registryClient = new AcpRegistryClient();

        await registryClient.SaveToFileAsync(
            registryService.RegistryCachePath,
            new AcpRegistryDocument
            {
                Version = "1.0.0",
                Agents =
                [
                    new AcpRegistryAgentManifest
                    {
                        Id = "registry-only-agent",
                        Name = "Registry Only Agent",
                        Version = "1.2.3",
                        Distribution = new AcpRegistryDistribution
                        {
                            Npx = new AcpRegistryPackageDistribution
                            {
                                Package = "@sample/registry-only-agent@1.2.3",
                                Arguments = ["--stdio"],
                            },
                        },
                    },
                ],
            }).ConfigureAwait(false);

        var service = new AcpManagementService(
            catalogOptions,
            registryService,
            configStore,
            installedStore,
            new Dictionary<string, ChatBackendState>(StringComparer.OrdinalIgnoreCase));

        var snapshot = await service.LoadSnapshotAsync(refreshRegistry: false).ConfigureAwait(false);
        var item = snapshot.Items.Single(static candidate => candidate.AgentId == "registry-only-agent");

        Assert.IsNotNull(item.CommandSummary);
        StringAssert.Contains(item.CommandSummary, "@sample/registry-only-agent@1.2.3");
        StringAssert.Contains(item.CommandSummary, "--stdio");
        Assert.IsFalse(item.IsInstalled);
        Assert.IsFalse(item.HasConfiguration);
    }

    [TestMethod]
    public void SaveConfigurationAndResetConfiguration_RoundTripManualAgent()
    {
        using var temp = TempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var installedStore = new AcpInstalledBackendStore(catalogOptions);
        var configStore = new CodeAltaConfigStore(catalogOptions);
        using var registryService = new AcpAgentRegistryService(catalogOptions, installedStore);
        var service = new AcpManagementService(
            catalogOptions,
            registryService,
            configStore,
            installedStore,
            new Dictionary<string, ChatBackendState>(StringComparer.OrdinalIgnoreCase));

        service.SaveConfiguration(new AcpBackendDefinition
        {
            AgentId = "manual-agent",
            DisplayName = "Manual Agent",
            Command = "npx",
            Arguments = ["--yes", "@manual/agent"],
        });

        Assert.IsTrue(service.AgentIdExists("manual-agent"));
        var editable = service.CreateEditableDefinition("manual-agent");
        Assert.AreEqual("Manual Agent", editable.DisplayName);
        CollectionAssert.AreEqual(new[] { "--yes", "@manual/agent" }, editable.Arguments);

        Assert.IsTrue(service.ResetConfiguration("manual-agent"));
        Assert.IsFalse(service.AgentIdExists("manual-agent"));
    }

    [TestMethod]
    public void RemoveAgent_RemovesInstalledManifestConfigAndArtifacts()
    {
        using var temp = TempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var installedStore = new AcpInstalledBackendStore(catalogOptions);
        var configStore = new CodeAltaConfigStore(catalogOptions);
        using var registryService = new AcpAgentRegistryService(catalogOptions, installedStore);
        var service = new AcpManagementService(
            catalogOptions,
            registryService,
            configStore,
            installedStore,
            new Dictionary<string, ChatBackendState>(StringComparer.OrdinalIgnoreCase));

        installedStore.Save(new AcpBackendDefinition
        {
            AgentId = "sample-agent",
            DisplayName = "Sample Agent",
            Command = "npx",
        });
        configStore.SaveGlobalAcpBackendDefinition(new AcpBackendDefinition
        {
            AgentId = "sample-agent",
            DisplayName = "Configured Sample Agent",
            Command = "npx",
        });
        Directory.CreateDirectory(Path.Combine(catalogOptions.AcpInstallsRoot, "sample-agent"));
        Directory.CreateDirectory(Path.Combine(catalogOptions.AcpDownloadsRoot, "sample-agent"));
        Directory.CreateDirectory(Path.Combine(catalogOptions.AcpStateRoot, "sample-agent"));

        var removed = service.RemoveAgent("sample-agent", removeArtifacts: true);

        Assert.IsTrue(removed);
        Assert.AreEqual(0, installedStore.Load().Count);
        Assert.IsNull(configStore.LoadGlobalAcpBackendDefinition("sample-agent"));
        Assert.IsFalse(Directory.Exists(Path.Combine(catalogOptions.AcpInstallsRoot, "sample-agent")));
        Assert.IsFalse(Directory.Exists(Path.Combine(catalogOptions.AcpDownloadsRoot, "sample-agent")));
        Assert.IsFalse(Directory.Exists(Path.Combine(catalogOptions.AcpStateRoot, "sample-agent")));
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codealta-acp-ui-tests-{Guid.NewGuid():N}");
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
