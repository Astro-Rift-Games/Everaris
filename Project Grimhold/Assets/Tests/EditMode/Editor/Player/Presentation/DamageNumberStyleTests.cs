using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Tests.EditMode.Player.Presentation
{
    public sealed class DamageNumberStyleTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ImpactStyle_FatalOnlyAndPoolReuseResets(bool fatal)
        {
            var root = new GameObject("Numbers", typeof(RectTransform));
            var template = new GameObject("Template", typeof(RectTransform), typeof(TextMeshProUGUI));
            var host = new GameObject("Presenter");
            host.SetActive(false);
            try
            {
                TMP_Text text = template.GetComponent<TMP_Text>();
                text.color = Color.cyan;
                var presenter = host.AddComponent<CombatFeedbackPresenter>();
                Set(presenter, "_impactRoot", root.GetComponent<RectTransform>());
                Set(presenter, "_impactTemplate", text);
                Set(presenter, "_impactPoolSize", 1);
                var killColor = typeof(CombatFeedbackPresenter).GetField("_killColor", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(killColor, Is.Not.Null, "Serialized kill color must exist.");
                killColor.SetValue(presenter, Color.yellow);
                Set(presenter, "_killScaleMultiplier", 1.5f);
                Invoke(presenter, "EnsureImpactSlots");
                var impact = typeof(CombatFeedbackPresenter).GetMethod("PresentImpact", BindingFlags.Instance | BindingFlags.NonPublic);
                impact.Invoke(presenter, new object[] { Vector2.zero, 25f, fatal, DamageType.Physical });
                TMP_Text pooled = root.GetComponentInChildren<TMP_Text>();
                Assert.That(pooled.text, Is.EqualTo("25"));
                Assert.That(pooled.color, Is.EqualTo(fatal ? Color.yellow : Color.cyan));
                Assert.That(pooled.rectTransform.localScale.x, Is.EqualTo(fatal ? 1.5f : 1f).Within(.001f));
                impact.Invoke(presenter, new object[] { Vector2.zero, 7f, false, DamageType.Physical });
                Assert.That(root.GetComponentsInChildren<TMP_Text>(), Has.Length.EqualTo(1));
                Assert.That(pooled.text, Is.EqualTo("7"));
                Assert.That(pooled.color, Is.EqualTo(Color.cyan));
                Assert.That(pooled.rectTransform.localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void MagicalImpact_UsesPaletteUnlessFatal_AndPhysicalReuseRestoresAuthoredColor(bool fatal, bool usePalette)
        {
            var root = new GameObject("Numbers", typeof(RectTransform));
            var template = new GameObject("Template", typeof(RectTransform), typeof(TextMeshProUGUI));
            var host = new GameObject("Presenter");
            host.SetActive(false);
            try
            {
                var presenter = host.AddComponent<CombatFeedbackPresenter>();
                TMP_Text text = template.GetComponent<TMP_Text>();
                text.color = Color.cyan; // Deliberately non-white: preserve the authored template.
                Set(presenter, "_impactRoot", root.GetComponent<RectTransform>());
                Set(presenter, "_impactTemplate", text);
                Set(presenter, "_impactPoolSize", 1);
                var paletteField = typeof(CombatFeedbackPresenter).GetField("_tintPalette", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(paletteField, Is.Not.Null, "Optional damage-number palette must exist.");
                var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackVfxTintPalette>("Assets/Scriptable Objects/AttackVfxTintPalette.asset");
                Assert.That(palette.TryGetTint(DamageType.Magical, out Color violet), Is.True);
                paletteField.SetValue(presenter, usePalette ? palette : null);
                Invoke(presenter, "EnsureImpactSlots");
                var impact = typeof(CombatFeedbackPresenter).GetMethod("PresentImpact", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(impact.GetParameters(), Has.Length.EqualTo(4), "Impact style must consume the confirmed damage type.");
                impact.Invoke(presenter, new object[] { Vector2.zero, 25f, fatal, DamageType.Magical });
                TMP_Text pooled = root.GetComponentInChildren<TMP_Text>();
                Assert.That(pooled.color, Is.EqualTo(fatal ? new Color(1f, .75f, .2f, 1f) : usePalette ? violet : Color.cyan));
                Assert.That(pooled.rectTransform.localScale.x, Is.EqualTo(fatal ? 1.35f : 1f).Within(.001f));
                impact.Invoke(presenter, new object[] { Vector2.zero, 7f, false, DamageType.Physical });
                Assert.That(pooled.color, Is.EqualTo(Color.cyan));
                Assert.That(pooled.text, Is.EqualTo("7"));
                Assert.That(pooled.rectTransform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(root.GetComponentsInChildren<TMP_Text>(), Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(root);
            }
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
