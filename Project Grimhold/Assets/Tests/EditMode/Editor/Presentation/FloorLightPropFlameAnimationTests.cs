using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class FloorLightPropFlameAnimationTests
{
    private const string ArtFolder = "Assets/Art/Props/";
    private const string AnimationFolder = "Assets/Animations/Props/";
    private const string PrefabFolder = "Assets/Prefabs/Lighting Props/";
    private const string CatacombsScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string PropSortingLayer = "Characters";
    private const int BaseSortingOrder = 100;
    private const int FlameSortingOrder = 101;
    private const float Tolerance = 0.001f;

    public sealed class Prop
    {
        public readonly string Prefab;
        public readonly string Art;
        public readonly int Frames;
        public readonly float SampleRate;
        public readonly Vector2 ImageOrigin;

        public Prop(string prefab, string art, int frames, float sampleRate, Vector2 imageOrigin)
        {
            Prefab = prefab;
            Art = art;
            Frames = frames;
            SampleRate = sampleRate;
            ImageOrigin = imageOrigin;
        }

        public string BasePath => ArtFolder + Art + "-Lit-Base.png";
        public string SheetPath => ArtFolder + Art + "-Flame-Sheet.png";
        public string LitPath => ArtFolder + Art + "-Lit.png";
        public string UnlitPath => ArtFolder + Art + "-Unlit.png";
        public string ClipPath => AnimationFolder + Art + "_Flame_Flicker.anim";
        public string ControllerPath => AnimationFolder + Art + "Flame.controller";
        public string PrefabPath => PrefabFolder + Prefab + ".prefab";

        public override string ToString() => Prefab;
    }

    private static readonly Prop[] Props =
    {
        new("Brazier", "Brazier", 10, 7f, new Vector2(64f, 64.5f)),
        new("Candle", "Candle", 6, 8f, new Vector2(64.5f, 65.5f)),
        new("Multiple Candles", "Candles", 8, 8f, new Vector2(64.5f, 67f)),
    };

    [TestCaseSource(nameof(Props))]
    public void LitBase_UsesPixelArtImportSettings_AndTheLitImageOrigin(Prop prop)
    {
        AssertPixelArtImportSettings(prop.BasePath);
        Sprite baseSprite = LoadSingleSprite(prop.BasePath, prop.Art + "-Lit-Base_0");

        Assert.That((baseSprite.rect.position + baseSprite.pivot - prop.ImageOrigin).magnitude, Is.LessThan(Tolerance),
            "The flameless base must sit at the lit sprite's image origin.");
    }

    [TestCaseSource(nameof(Props))]
    public void FlameSheet_HasUniformFramesPivotedOnTheLitImageOrigin(Prop prop)
    {
        AssertPixelArtImportSettings(prop.SheetPath);
        Sprite[] frames = LoadSheetSprites(prop);

        for (int i = 0; i < prop.Frames; i++)
        {
            Assert.That(frames[i].rect, Is.EqualTo(new Rect(i * 128f, 0f, 128f, 128f)), frames[i].name);
            Assert.That((frames[i].pivot - prop.ImageOrigin).magnitude, Is.LessThan(Tolerance), frames[i].name);
        }
    }

    [TestCaseSource(nameof(Props))]
    public void BasePlusFirstFlameFrame_RebuildsTheOriginalLitArtPixelExact(Prop prop)
    {
        Texture2D lit = ReadPng(prop.LitPath);
        Texture2D rebuilt = ReadPng(prop.BasePath);
        Texture2D sheet = ReadPng(prop.SheetPath);
        try
        {
            Color32[] basePixels = rebuilt.GetPixels32();
            Color32[] flame = sheet.GetPixels32();
            Color32[] litPixels = lit.GetPixels32();
            int size = lit.width;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 f = flame[y * sheet.width + x];
                    Color32 composed = f.a > 0 ? f : basePixels[y * size + x];
                    Color32 expected = litPixels[y * size + x];
                    if (expected.a == 0 && composed.a == 0)
                        continue;
                    Assert.That(composed, Is.EqualTo(expected), $"{prop.Art} pixel ({x},{y})");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(lit);
            Object.DestroyImmediate(rebuilt);
            Object.DestroyImmediate(sheet);
        }
    }

    [TestCaseSource(nameof(Props))]
    public void Clip_LoopsEveryFlameFrame_WithoutTransformsOrEvents(Prop prop)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(prop.ClipPath);
        Assert.That(clip, Is.Not.Null, prop.ClipPath);
        Assert.That(clip.legacy, Is.False);
        Assert.That(clip.frameRate, Is.EqualTo(prop.SampleRate));
        Assert.That(clip.length, Is.EqualTo(prop.Frames / prop.SampleRate).Within(0.0001f));
        Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.True);
        Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Empty);
        Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);

        EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].type, Is.EqualTo(typeof(SpriteRenderer)));
        Assert.That(bindings[0].propertyName, Is.EqualTo("m_Sprite"));
        Assert.That(bindings[0].path, Is.Empty, "The Animator lives on FlameVisual itself.");

        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
        Sprite[] frames = LoadSheetSprites(prop);
        Assert.That(keys, Has.Length.EqualTo(prop.Frames));
        for (int i = 0; i < prop.Frames; i++)
        {
            Assert.That(keys[i].time, Is.EqualTo(i / prop.SampleRate).Within(0.0001f));
            Assert.That(keys[i].value, Is.EqualTo(frames[i]));
        }
    }

    [TestCaseSource(nameof(Props))]
    public void Controller_HasASingleDefaultLoopingFlickerState(Prop prop)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(prop.ControllerPath);
        Assert.That(controller, Is.Not.Null, prop.ControllerPath);
        Assert.That(controller.layers, Has.Length.EqualTo(1));
        Assert.That(controller.parameters, Is.Empty);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        Assert.That(machine.states, Has.Length.EqualTo(1));
        Assert.That(machine.anyStateTransitions, Is.Empty);
        Assert.That(machine.defaultState, Is.Not.Null);
        Assert.That(machine.defaultState.speed, Is.EqualTo(1f));
        Assert.That(machine.defaultState.transitions, Is.Empty);
        Assert.That(machine.defaultState.motion, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimationClip>(prop.ClipPath)));
    }

    [TestCaseSource(nameof(Props))]
    public void Prefab_ShowsTheFlamelessBaseWithAnAnimatedFlame_SortedAsOneProp(Prop prop)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prop.PrefabPath);
        Assert.That(prefab, Is.Not.Null, prop.PrefabPath);
        Sprite baseSprite = LoadSingleSprite(prop.BasePath, prop.Art + "-Lit-Base_0");
        Sprite unlit = LoadSingleSprite(prop.UnlitPath, prop.Art + "-Unlit_0");

        var lightProp = prefab.GetComponent<DungeonLightProp>();
        Assert.That(lightProp.LitSprite, Is.EqualTo(baseSprite));
        Assert.That(lightProp.ExtinguishedSprite, Is.EqualTo(unlit));

        var body = prefab.GetComponent<SpriteRenderer>();
        Assert.That(body.sprite, Is.EqualTo(baseSprite));
        Assert.That(body.sortingLayerName, Is.EqualTo(PropSortingLayer));
        Assert.That(body.sortingOrder, Is.EqualTo(BaseSortingOrder));

        Transform flameTransform = prefab.transform.Find("FlameVisual");
        Assert.That(flameTransform, Is.Not.Null);
        Assert.That(lightProp.FlameObject, Is.EqualTo(flameTransform.gameObject));
        Assert.That(flameTransform.localPosition, Is.EqualTo(Vector3.zero), "Flame frames share the base image origin.");
        var flame = flameTransform.GetComponent<SpriteRenderer>();
        Assert.That(flame.sprite, Is.EqualTo(LoadSheetSprites(prop)[0]));
        Assert.That(flame.sortingLayerName, Is.EqualTo(PropSortingLayer));
        Assert.That(flame.sortingOrder, Is.EqualTo(FlameSortingOrder));

        var animator = flameTransform.GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.runtimeAnimatorController,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimatorController>(prop.ControllerPath)));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Normal));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));

        var group = prefab.GetComponent<SortingGroup>();
        Assert.That(group, Is.Not.Null,
            "Base and flame must Y-sort as one prop; a loose flame renderer would draw over characters in front of it.");
        Assert.That(group.sortingLayerName, Is.EqualTo(PropSortingLayer));
        Assert.That(group.sortingOrder, Is.EqualTo(BaseSortingOrder));
    }

    [Test]
    public void CatacombsMap_FloorLightProps_InheritTheAnimatedPrefabs()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(CatacombsScenePath);
        try
        {
            int checkedProps = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (DungeonLightProp instance in root.GetComponentsInChildren<DungeonLightProp>(true))
                {
                    string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject);
                    Prop prop = Props.FirstOrDefault(p => p.PrefabPath == source);
                    if (prop == null)
                        continue;

                    checkedProps++;
                    Sprite baseSprite = LoadSingleSprite(prop.BasePath, prop.Art + "-Lit-Base_0");
                    Assert.That(instance.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(baseSprite), instance.name);
                    Assert.That(instance.LitSprite, Is.EqualTo(baseSprite), instance.name);
                    Transform flame = instance.transform.Find("FlameVisual");
                    Assert.That(flame.GetComponent<Animator>(), Is.Not.Null, instance.name);
                    Assert.That(instance.GetComponent<SortingGroup>(), Is.Not.Null, instance.name);
                }
            }

            Assert.That(checkedProps, Is.GreaterThan(0), "CatacombsMap should contain painted braziers or candles.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void AssertPixelArtImportSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Assert.That(importer, Is.Not.Null, path);
        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple), path);
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f), path);
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
        Assert.That(importer.mipmapEnabled, Is.False, path);
        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.Tight), path);
    }

    private static Sprite LoadSingleSprite(string path, string expectedName)
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        Assert.That(sprites, Has.Length.EqualTo(1), path);
        Assert.That(sprites[0].name, Is.EqualTo(expectedName), path);
        return sprites[0];
    }

    private static Sprite[] LoadSheetSprites(Prop prop)
    {
        Sprite[] all = AssetDatabase.LoadAllAssetsAtPath(prop.SheetPath).OfType<Sprite>().ToArray();
        Assert.That(all, Has.Length.EqualTo(prop.Frames), prop.SheetPath);
        return Enumerable.Range(0, prop.Frames)
            .Select(i => all.Single(s => s.name == $"{prop.Art}-Flame-Sheet_{i}"))
            .ToArray();
    }

    private static Texture2D ReadPng(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.That(File.Exists(path), Is.True, path);
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
        return texture;
    }
}
