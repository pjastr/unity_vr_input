using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VRInputDemo
{
    /// <summary>
    /// Fabryka prostych elementów: prymitywy Unity w kolorze oraz panele tekstowe (world-space Canvas).
    /// Używana zarówno przez skrypt edytora (budowa sceny), jak i w trakcie działania aplikacji.
    /// </summary>
    public static class DemoUI
    {
        /// <summary>Skala Canvasu: 1 piksel UI = 1 mm w świecie.</summary>
        public const float PixelSize = 0.001f;

        static Font font;

        /// <summary>
        /// Materiał (URP/Lit) dla tworzonych prymitywów. W edytorze ustawia go skrypt budujący scenę,
        /// w buildzie - komponent DemoResources (materiał musi być referencją w scenie, inaczej shader
        /// nie trafi do builda).
        /// </summary>
        public static Material LitMaterial { get; set; }

        public static Font DefaultFont
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale, Color color, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) DestroySafe(col);
            }

            var rend = go.GetComponent<Renderer>();
            if (rend != null && LitMaterial != null) rend.sharedMaterial = LitMaterial;

            go.AddComponent<ColorTint>().SetBaseColor(color);
            return go;
        }

        public static void DisableShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>Tworzy panel (world-space Canvas) z półprzezroczystym tłem. Rozmiar w pikselach (= mm).</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector3 position, Quaternion rotation,
            Vector2 sizePx, Color background)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * PixelSize;
            rt.SetPositionAndRotation(position, rotation);

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var bgRt = (RectTransform)bg.transform;
            bgRt.SetParent(rt, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var img = bg.GetComponent<Image>();
            img.color = background;
            img.raycastTarget = false;

            return rt;
        }

        /// <summary>Tworzy tekst rozciągnięty między kotwicami (anchorMin/Max) z marginesami (offsetMin/Max).</summary>
        public static Text CreateText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, int fontSize, TextAnchor alignment, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var t = go.GetComponent<Text>();
            t.font = DefaultFont;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = color;
            t.text = content;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Tekst wypełniający cały panel z marginesem.</summary>
        public static Text CreateFillText(RectTransform parent, string name, float margin, int fontSize,
            TextAnchor alignment, Color color, string content)
        {
            return CreateText(parent, name, Vector2.zero, Vector2.one, new Vector2(margin, margin),
                new Vector2(-margin, -margin), fontSize, alignment, color, content);
        }

        /// <summary>Obrót panelu tak, by jego przód był zwrócony do punktu viewer (np. głowy gracza).</summary>
        public static Quaternion FacePoint(Vector3 position, Vector3 viewer, bool keepUpright = true)
        {
            Vector3 dir = position - viewer;
            if (keepUpright) dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        public static string Bar(float value, int width = 10)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * width), 0, width);
            return "[" + new string('#', filled) + new string('.', width - filled) + "]";
        }

        public static string YesNo(bool value) =>
            value ? "<color=#7CFC7C>TAK</color>" : "<color=#A0A0A0>nie</color>";
    }
}
