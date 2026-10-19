using UnityEngine;

namespace VRInputDemo
{
    public enum PokeAction { SpawnBall, RandomizeColors, ToggleGravity, ResetScene }

    /// <summary>
    /// Fizyczny przycisk "poke": wciskany czubkiem palca wskazującego (dłoń) lub końcówką kontrolera.
    /// Nie używa fizyki - co klatkę sprawdza pozycję punktu PokePoint w lokalnym układzie przycisku.
    /// Wciśnięcie liczy się tylko wtedy, gdy palec wszedł od góry (nie z boku / od spodu).
    /// Lokalny układ: początek = środek górnej powierzchni podstawy, oś Y w górę.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public class PokeButton : MonoBehaviour
    {
        public PokeAction action;
        public string label = "";
        [Tooltip("Ruchoma nakładka przycisku (dziecko).")]
        public Transform cap;
        [Tooltip("Rozmiar nakładki w osiach X i Z (m).")]
        public Vector2 size = new Vector2(0.09f, 0.09f);
        [Tooltip("Wysokość nakładki w spoczynku (m).")]
        public float capHeight = 0.02f;
        [Tooltip("Skok przycisku (m).")]
        public float travel = 0.014f;
        public Color pressedColor = new Color(1f, 1f, 0.6f);

        readonly bool[] armed = new bool[2];
        readonly bool[] pressed = new bool[2];
        ColorTint capTint;
        float flash;

        void Awake()
        {
            if (cap != null) capTint = cap.GetComponent<ColorTint>();
        }

        void Update()
        {
            float top = capHeight;

            for (int i = 0; i < 2; i++)
            {
                var src = XRInputSource.Get(i == 0 ? HandSide.Left : HandSide.Right);
                if (src == null || !src.IsActive || !src.PokeValid)
                {
                    armed[i] = false;
                    pressed[i] = false;
                    continue;
                }

                Vector3 p = transform.InverseTransformPoint(src.PokePoint);
                bool insideXZ = Mathf.Abs(p.x) <= size.x * 0.5f + 0.01f && Mathf.Abs(p.z) <= size.y * 0.5f + 0.01f;

                if (!insideXZ)
                {
                    armed[i] = false;
                    pressed[i] = false;
                    continue;
                }

                if (p.y > capHeight)
                {
                    // Palec nad przyciskiem - "uzbrojenie" (musi nadejść od góry).
                    armed[i] = p.y < capHeight + 0.1f;
                    pressed[i] = false;
                    continue;
                }

                if (!armed[i]) continue;

                float pushedTop = Mathf.Max(capHeight - travel, p.y);
                top = Mathf.Min(top, pushedTop);

                if (!pressed[i] && p.y <= capHeight - travel * 0.7f)
                {
                    pressed[i] = true;
                    Press(src, "poke");
                }
                else if (pressed[i] && p.y > capHeight - travel * 0.3f)
                {
                    pressed[i] = false;
                }
            }

            if (cap != null)
            {
                cap.localPosition = new Vector3(0f, top - capHeight * 0.5f, 0f);
            }

            if (flash > 0f)
            {
                flash -= Time.deltaTime;
                if (flash <= 0f && capTint != null) capTint.SetOverride(null);
            }
        }

        /// <summary>Wywołuje akcję przycisku (z palca/kontrolera albo zdalnie promieniem).</summary>
        public void Press(XRInputSource src, string how)
        {
            flash = 0.25f;
            if (capTint != null) capTint.SetOverride(pressedColor);
            if (src != null) src.Haptic(0.7f, 0.06f);

            string who = src != null ? src.Label : "?";
            DemoLog.Add($"{who}: przycisk \"{label}\" ({how})");

            if (DemoActions.Instance != null)
                DemoActions.Instance.Perform(action, transform.position + Vector3.up * 0.3f);
        }
    }
}
