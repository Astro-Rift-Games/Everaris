using System;
using UnityEngine;

namespace Grimhold.Backend
{
    /// <summary>
    /// Single source of truth for resolving the active <see cref="BackendConfiguration"/> across
    /// login, health checks, inventory, commerce, abilities, missions, reconciliation, and town/raid
    /// verifiers (FIX-58). No service should call <c>Resources.Load&lt;BackendConfiguration&gt;</c>
    /// directly.
    ///
    /// Resolution order:
    /// 1. Explicit runtime/test override (<see cref="SetActiveOverride"/>).
    /// 2. Development-only local toggle (Editor menu <c>Grimhold/Backend/Use Local Backend</c> via
    ///    <see cref="EditorUseLocalPrefKey"/>, or env var <see cref="EnvVarUseLocal"/>=1 in Editor
    ///    or Development builds) -> loads <see cref="LocalResourceName"/> without editing versioned assets.
    /// 3. Authenticated session config (<see cref="ApplicationAuthContext.BackendConfig"/>).
    /// 4. Login controller config (<see cref="LoginFlowController.Config"/>).
    /// 5. Caller-supplied serialized fallback (e.g. inspector reference on MainMenu/LoginFlow).
    /// 6. Default resource asset (<see cref="DefaultResourceName"/>).
    /// </summary>
    public static class BackendConfigurationProvider
    {
        public const string DefaultResourceName = "BackendConfiguration";
        public const string LocalResourceName = "BackendConfiguration_Local";
        public const string EditorUseLocalPrefKey = "Grimhold.Backend.UseLocalConfiguration";
        public const string EnvVarUseLocal = "GRIMHOLD_USE_LOCAL_BACKEND";

        private static BackendConfiguration _activeOverride;
        private static bool? _localOverrideForTests;
        private static Func<string, BackendConfiguration> _resourceLoader = LoadFromResources;

        /// <summary>True when development local-backend mode is active (never true in release builds).</summary>
        public static bool IsLocalOverrideEnabled()
        {
            if (_localOverrideForTests.HasValue)
            {
                return _localOverrideForTests.Value;
            }

#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(EditorUseLocalPrefKey, false))
            {
                return true;
            }
            return IsLocalEnvironmentVariableEnabled();
#elif DEVELOPMENT_BUILD
            return IsLocalEnvironmentVariableEnabled();
#else
            return false;
#endif
        }

        public static void SetActiveOverride(BackendConfiguration configuration)
        {
            _activeOverride = configuration;
        }

        public static void ClearActiveOverride()
        {
            _activeOverride = null;
        }

        internal static void ConfigureForTesting(
            bool? localOverrideEnabled = null,
            Func<string, BackendConfiguration> resourceLoader = null)
        {
            _localOverrideForTests = localOverrideEnabled;
            _resourceLoader = resourceLoader ?? LoadFromResources;
        }

        internal static void ResetTestingOverrides()
        {
            _activeOverride = null;
            _localOverrideForTests = null;
            _resourceLoader = LoadFromResources;
        }

        /// <summary>
        /// Resolves the active <see cref="BackendConfiguration"/> from the single source of truth.
        /// </summary>
        public static BackendConfiguration Resolve(BackendConfiguration serializedFallback = null)
        {
            if (_activeOverride != null)
            {
                return _activeOverride;
            }

            if (IsLocalOverrideEnabled())
            {
                BackendConfiguration local = _resourceLoader(LocalResourceName);
                if (local != null)
                {
                    return local;
                }

                Debug.LogWarning(
                    $"[{nameof(BackendConfigurationProvider)}] Local backend override is enabled, " +
                    $"but Resources/{LocalResourceName}.asset was not found. Falling back to active/default configuration.");
            }

            if (ApplicationAuthContext.Instance != null && ApplicationAuthContext.Instance.BackendConfig != null)
            {
                return ApplicationAuthContext.Instance.BackendConfig;
            }

            if (LoginFlowController.Instance != null && LoginFlowController.Instance.Config != null)
            {
                return LoginFlowController.Instance.Config;
            }

            if (serializedFallback != null)
            {
                return serializedFallback;
            }

            return _resourceLoader(DefaultResourceName);
        }

        /// <summary>
        /// Resolves the active configuration, or creates a transient default instance if no asset is available.
        /// </summary>
        public static BackendConfiguration ResolveOrDefault(BackendConfiguration serializedFallback = null)
        {
            BackendConfiguration resolved = Resolve(serializedFallback);
            if (resolved != null)
            {
                return resolved;
            }

            Debug.LogWarning($"[{nameof(BackendConfigurationProvider)}] No BackendConfiguration found. Using defaults.");
            return ScriptableObject.CreateInstance<BackendConfiguration>();
        }

        private static BackendConfiguration LoadFromResources(string resourceName) =>
            Resources.Load<BackendConfiguration>(resourceName);

        private static bool IsLocalEnvironmentVariableEnabled()
        {
            string value = Environment.GetEnvironmentVariable(EnvVarUseLocal);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "1" || normalized == "true" || normalized == "local" || normalized == "yes";
        }
    }
}
