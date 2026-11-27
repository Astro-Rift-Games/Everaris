using System.IO;
using System.Linq;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BackendConfigurationProviderTests
{
    private BackendConfiguration _deployed;
    private BackendConfiguration _local;
    private BackendConfiguration _custom;

    [SetUp]
    public void SetUp()
    {
        BackendConfigurationProvider.ResetTestingOverrides();

        _deployed = ScriptableObject.CreateInstance<BackendConfiguration>();
        _deployed.BaseUrl = "https://projectgrimhold.onrender.com";

        _local = ScriptableObject.CreateInstance<BackendConfiguration>();
        _local.BaseUrl = "http://localhost:3000";

        _custom = ScriptableObject.CreateInstance<BackendConfiguration>();
        _custom.BaseUrl = "http://127.0.0.1:4123";
    }

    [TearDown]
    public void TearDown()
    {
        BackendConfigurationProvider.ResetTestingOverrides();
        if (_deployed != null) Object.DestroyImmediate(_deployed);
        if (_local != null) Object.DestroyImmediate(_local);
        if (_custom != null) Object.DestroyImmediate(_custom);
    }

    [Test]
    public void LocalOverrideEnabled_ResolvesLocalConfiguration_EvenWithSerializedDeployedFallback()
    {
        BackendConfigurationProvider.ConfigureForTesting(
            localOverrideEnabled: true,
            resourceLoader: name => name == BackendConfigurationProvider.LocalResourceName ? _local : _deployed);

        BackendConfiguration resolved = BackendConfigurationProvider.Resolve(serializedFallback: _deployed);

        Assert.That(resolved, Is.SameAs(_local));
        Assert.That(resolved.BaseUrl, Is.EqualTo("http://localhost:3000"));
    }

    [Test]
    public void LocalOverrideEnabled_AppliesUniformlyToLoginInventoryAndReconciliationServices()
    {
        BackendConfigurationProvider.ConfigureForTesting(
            localOverrideEnabled: true,
            resourceLoader: name => name == BackendConfigurationProvider.LocalResourceName ? _local : _deployed);

        var go = new GameObject("Fix58ServiceWiringTest");
        try
        {
            var invService = go.AddComponent<RemoteInventoryService>();
            var reconcileService = go.AddComponent<ProfileReconciliationService>();
            invService.Initialize(null, null);
            reconcileService.Initialize(null, null);

            var invConfig = (BackendConfiguration)typeof(RemoteInventoryService)
                .GetField("_backendConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(invService);
            var recConfig = (BackendConfiguration)typeof(ProfileReconciliationService)
                .GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(reconcileService);

            Assert.That(invConfig, Is.SameAs(_local));
            Assert.That(recConfig, Is.SameAs(_local));
            Assert.That(invConfig.BaseUrl, Is.EqualTo("http://localhost:3000"));
            Assert.That(recConfig.BaseUrl, Is.EqualTo("http://localhost:3000"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LocalOverrideDisabled_UsesSerializedFallbackOrDefaultAsset()
    {
        BackendConfigurationProvider.ConfigureForTesting(
            localOverrideEnabled: false,
            resourceLoader: name => name == BackendConfigurationProvider.LocalResourceName ? _local : _deployed);

        Assert.That(BackendConfigurationProvider.Resolve(serializedFallback: _custom), Is.SameAs(_custom));
        Assert.That(BackendConfigurationProvider.Resolve(), Is.SameAs(_deployed));
    }

    [Test]
    public void ExplicitActiveOverride_TakesHighestPrecedence()
    {
        BackendConfigurationProvider.ConfigureForTesting(
            localOverrideEnabled: true,
            resourceLoader: name => name == BackendConfigurationProvider.LocalResourceName ? _local : _deployed);
        BackendConfigurationProvider.SetActiveOverride(_custom);

        Assert.That(BackendConfigurationProvider.Resolve(serializedFallback: _deployed), Is.SameAs(_custom));
    }

    [Test]
    public void NoServiceScriptOutsideProvider_LoadsBackendConfigurationByNameFromResources()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
        var offendingFiles = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("BackendConfigurationProvider.cs"))
            .Where(path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("Resources.Load<BackendConfiguration>") ||
                       content.Contains("Resources.Load<Grimhold.Backend.BackendConfiguration>");
            })
            .ToArray();

        Assert.That(offendingFiles, Is.Empty,
            "No service script outside BackendConfigurationProvider may load BackendConfiguration by name from Resources.");
    }
}
