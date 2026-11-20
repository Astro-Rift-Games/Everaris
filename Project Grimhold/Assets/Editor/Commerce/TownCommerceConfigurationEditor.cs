using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TownCommerceConfiguration))]
public sealed class TownCommerceConfigurationEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        TownCommerceConfiguration config = (TownCommerceConfiguration)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Configuración del Sistema de Comercio del Pueblo (Docs 22 y 23).\n" +
            "Permite a los Game Designers configurar pools rotativos, permanentes, modificadores, demanda y multiplicadores económicos.",
            MessageType.Info);
        EditorGUILayout.Space(5);

        // Export Button
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("💾 Exportar Configuración a Backend (commerceConfig.json)", GUILayout.Height(38)))
        {
            if (config.TryValidate(out string error))
            {
                if (config.ExportToBackendFile())
                {
                    EditorUtility.DisplayDialog("Comercio Grimhold", "¡Configuración exportada exitosamente a backend/src/config/commerceConfig.json!", "Aceptar");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error de Exportación", "No se pudo escribir el archivo commerceConfig.json. Revisa la consola.", "Aceptar");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Error de Validación", $"La configuración contiene errores y no se exportó:\n\n{error}", "Aceptar");
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);
        if (GUILayout.Button("🔍 Validar Configuración", GUILayout.Height(28)))
        {
            if (config.TryValidate(out string error))
            {
                EditorUtility.DisplayDialog("Validación Exitosa", "Todos los pools, multiplicadores y referencias son válidos.", "Aceptar");
            }
            else
            {
                EditorUtility.DisplayDialog("Error de Validación", error, "Aceptar");
            }
        }

        EditorGUILayout.Space(10);
        DrawDefaultInspector();
    }

    [MenuItem("Grimhold/Commerce/Export Commerce Config to Backend")]
    public static void ExportActiveCommerceConfig()
    {
        string[] guids = AssetDatabase.FindAssets("t:TownCommerceConfiguration");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Comercio Grimhold", "No se encontró ningún asset de TownCommerceConfiguration en el proyecto.", "Aceptar");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(path);
        if (config != null)
        {
            if (config.TryValidate(out string error))
            {
                config.ExportToBackendFile();
                EditorUtility.DisplayDialog("Comercio Grimhold", $"Configuración '{config.name}' exportada exitosamente.", "Aceptar");
            }
            else
            {
                EditorUtility.DisplayDialog("Error de Validación", error, "Aceptar");
            }
        }
    }
}
