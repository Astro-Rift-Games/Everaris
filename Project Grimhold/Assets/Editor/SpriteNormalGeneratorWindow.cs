using System;
using UnityEditor;
using UnityEngine;

internal sealed class SpriteNormalGeneratorWindow : EditorWindow
{
    private Texture2D _source;
    private float _relief = 2f, _detail = 1f;
    private int _smoothing = 1;
    private bool _repeat;
    private string _message;

    [MenuItem("Tools/Grimhold/Sprite Normal Map Generator")]
    private static void Open() => GetWindow<SpriteNormalGeneratorWindow>("Sprite Normals");

    private void OnGUI()
    {
        _source = (Texture2D)EditorGUILayout.ObjectField("Pilot sprite (PNG)", _source, typeof(Texture2D), false);
        _relief = EditorGUILayout.Slider("Silhouette relief", _relief, 0f, 8f);
        _detail = EditorGUILayout.Slider("Luminance detail", _detail, 0f, 8f);
        _smoothing = EditorGUILayout.IntSlider("Smoothing passes", _smoothing, 0, 5);
        _repeat = EditorGUILayout.Toggle("Wrap each tile", _repeat);
        using (new EditorGUI.DisabledScope(_source == null))
        {
            if (GUILayout.Button("Generate and link _NormalMap"))
            {
                try
                {
                    SpriteNormalImporter.Generate(AssetDatabase.GetAssetPath(_source), _relief, _detail, _smoothing, _repeat);
                    _message = "Normal map linked. Existing sprites and slices were retained.";
                }
                catch (Exception ex) { _message = ex.Message; }
            }
        }
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
    }
}
