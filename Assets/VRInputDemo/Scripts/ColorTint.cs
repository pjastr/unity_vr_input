using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Koloruje obiekt przez MaterialPropertyBlock (bez tworzenia kopii materiału).
    /// Ustawia zarówno _BaseColor (URP Lit) jak i _Color (Built-in Standard).
    /// Obsługuje podświetlenie (hover) i chwilowe nadpisanie koloru (np. wciśnięty przycisk).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ColorTint : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] Color baseColor = Color.white;

        Renderer rend;
        MaterialPropertyBlock block;
        float highlight;
        Color? overrideColor;

        public Color BaseColor => baseColor;

        public void SetBaseColor(Color color)
        {
            baseColor = color;
            Apply();
        }

        /// <summary>0 = brak podświetlenia, 1 = pełne (kolor rozjaśniony w stronę bieli).</summary>
        public void SetHighlight(float amount)
        {
            if (Mathf.Approximately(amount, highlight)) return;
            highlight = amount;
            Apply();
        }

        /// <summary>Chwilowo zastępuje kolor bazowy; null przywraca kolor bazowy.</summary>
        public void SetOverride(Color? color)
        {
            if (overrideColor == color) return;
            overrideColor = color;
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void Apply()
        {
            if (rend == null) rend = GetComponent<Renderer>();
            if (rend == null) return;
            if (block == null) block = new MaterialPropertyBlock();

            Color c = overrideColor ?? baseColor;
            c = Color.Lerp(c, Color.white, highlight * 0.55f);

            rend.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            rend.SetPropertyBlock(block);
        }
    }
}
