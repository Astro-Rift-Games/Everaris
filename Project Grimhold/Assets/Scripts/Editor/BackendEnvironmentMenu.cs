using Grimhold.Backend;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only toggle to switch every Unity service between the deployed backend (Render) and
/// the local development backend (<c>Resources/BackendConfiguration_Local.asset</c>) without
/// modifying any versioned scene, prefab, or ScriptableObject asset (FIX-58).
/// </summary>
public static class BackendEnvironmentMenu
{
    private const string DeployedMenuPath = "Grimhold/Backend/Use Deployed Backend (Render)";
    private const string LocalMenuPath = "Grimhold/Backend/Use Local Backend (localhost:3000)";

    [MenuItem(DeployedMenuPath, priority = 100)]
    private static void SelectDeployedBackend()
    {
        EditorPrefs.SetBool(BackendConfigurationProvider.EditorUseLocalPrefKey, false);
        Debug.Log("[BackendEnvironmentMenu] Backend target set to Deployed (BackendConfiguration.asset).");
    }

    [MenuItem(DeployedMenuPath, true)]
    private static bool ValidateSelectDeployedBackend()
    {
        bool useLocal = EditorPrefs.GetBool(BackendConfigurationProvider.EditorUseLocalPrefKey, false);
        Menu.SetChecked(DeployedMenuPath, !useLocal);
        return true;
    }

    [MenuItem(LocalMenuPath, priority = 101)]
    private static void SelectLocalBackend()
    {
        EditorPrefs.SetBool(BackendConfigurationProvider.EditorUseLocalPrefKey, true);
        Debug.Log("[BackendEnvironmentMenu] Backend target set to Local (BackendConfiguration_Local.asset -> localhost:3000).");
    }

    [MenuItem(LocalMenuPath, true)]
    private static bool ValidateSelectLocalBackend()
    {
        bool useLocal = EditorPrefs.GetBool(BackendConfigurationProvider.EditorUseLocalPrefKey, false);
        Menu.SetChecked(LocalMenuPath, useLocal);
        return true;
    }
}
