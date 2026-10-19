using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VRInputDemo
{
    /// <summary>
    /// Panel "Kontrolery": na żywo pokazuje wszystkie stany OVRInput dla obu kontrolerów Touch
    /// (wartości analogowe, przyciski, dotyk pojemnościowy, zbliżenie palca, pozę i prędkość)
    /// oraz prosty wskaźnik 3D z prymitywów (słupki trigger/grip, gałka, przyciski).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class ControllerInputPanel : MonoBehaviour
    {
        public Text header;
        public Text leftText;
        public Text rightText;
        [Tooltip("Pozycja Y wskaźników 3D względem środka panelu (w pikselach panelu = mm).")]
        public float gaugeY = -290f;
        public float refreshInterval = 0.1f;

        const float BarHeight = 150f;
        static readonly Color Idle = new Color(0.45f, 0.45f, 0.5f);
        static readonly Color Touched = new Color(1f, 0.85f, 0.25f);
        static readonly Color Pressed = new Color(0.3f, 1f, 0.4f);

        class Gauge
        {
            public Transform triggerFill, gripFill, stickDot;
            public ColorTint button1, button2, stickBase, menu;
        }

        Gauge leftGauge, rightGauge;
        float nextRefresh;
        readonly StringBuilder sb = new StringBuilder();

        void Start()
        {
            leftGauge = BuildGauge(-250f, true);
            rightGauge = BuildGauge(250f, false);
        }

        void Update()
        {
            UpdateGauge(leftGauge, OVRInput.Controller.LTouch);
            UpdateGauge(rightGauge, OVRInput.Controller.RTouch);

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;

            if (header != null)
                header.text = "<b>KONTROLERY</b>  <color=#9AB>OVRInput</color>\n" +
                              $"Aktywny: <b>{OVRInput.GetActiveController()}</b>    Połączone: {OVRInput.GetConnectedControllers()}";
            if (leftText != null) leftText.text = Describe(OVRInput.Controller.LTouch, "LEWY", "X", "Y");
            if (rightText != null) rightText.text = Describe(OVRInput.Controller.RTouch, "PRAWY", "A", "B");
        }

        string Describe(OVRInput.Controller c, string title, string b1, string b2)
        {
            sb.Clear();
            bool connected = OVRInput.IsControllerConnected(c);
            sb.Append("<b>").Append(title).Append("</b>  ")
              .AppendLine(connected ? "<color=#7CFC7C>połączony</color>" : "<color=#FF7070>niepołączony</color>");

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c);
            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, c);
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c);

            sb.AppendLine($"Trigger  {trigger:0.00} {DemoUI.Bar(trigger)}");
            sb.AppendLine($"Grip       {grip:0.00} {DemoUI.Bar(grip)}");
            sb.AppendLine($"Gałka   ({stick.x:+0.00;-0.00}, {stick.y:+0.00;-0.00})");
            sb.AppendLine($"{b1}: {ButtonState(OVRInput.Button.One, OVRInput.Touch.One, c)}    {b2}: {ButtonState(OVRInput.Button.Two, OVRInput.Touch.Two, c)}");
            sb.AppendLine($"Klik gałki: {DemoUI.YesNo(OVRInput.Get(OVRInput.Button.PrimaryThumbstick, c))}  dotyk: {DemoUI.YesNo(OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, c))}");
            sb.AppendLine($"Dotyk triggera: {DemoUI.YesNo(OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, c))}  blisko: {DemoUI.YesNo(OVRInput.Get(OVRInput.NearTouch.PrimaryIndexTrigger, c))}");
            sb.AppendLine($"Kciuk blisko przycisków: {DemoUI.YesNo(OVRInput.Get(OVRInput.NearTouch.PrimaryThumbButtons, c))}");
            if (c == OVRInput.Controller.LTouch)
                sb.AppendLine($"Menu: {DemoUI.YesNo(OVRInput.Get(OVRInput.Button.Start, c))}");

            Vector3 pos = OVRInput.GetLocalControllerPosition(c);
            Vector3 vel = OVRInput.GetLocalControllerVelocity(c);
            Vector3 ang = OVRInput.GetLocalControllerAngularVelocity(c);
            sb.AppendLine($"Pozycja ({pos.x:0.00}, {pos.y:0.00}, {pos.z:0.00}) m");
            sb.AppendLine($"Prędkość {vel.magnitude:0.00} m/s   obrót {ang.magnitude:0.0} rad/s");
            sb.Append($"Śledzenie poz.: {DemoUI.YesNo(OVRInput.GetControllerPositionTracked(c))}  rot.: {DemoUI.YesNo(OVRInput.GetControllerOrientationTracked(c))}");
            return sb.ToString();
        }

        static string ButtonState(OVRInput.Button button, OVRInput.Touch touch, OVRInput.Controller c)
        {
            if (OVRInput.Get(button, c)) return "<color=#7CFC7C>WCIŚNIĘTY</color>";
            if (OVRInput.Get(touch, c)) return "<color=#FFD040>dotyk</color>";
            return "<color=#A0A0A0>-</color>";
        }

        // ---------------- wskaźniki 3D ----------------

        Gauge BuildGauge(float x, bool isLeft)
        {
            var g = new Gauge();
            var root = new GameObject(isLeft ? "LeftGauge" : "RightGauge").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(x, gaugeY, -12f);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color bg = new Color(0.15f, 0.15f, 0.18f);
            Prim(PrimitiveType.Cube, "TriggerBg", root, new Vector3(-170f, 0f, 6f), new Vector3(34f, BarHeight + 6f, 4f), bg);
            g.triggerFill = Prim(PrimitiveType.Cube, "TriggerFill", root, new Vector3(-170f, 0f, 0f), new Vector3(28f, 1f, 8f), new Color(0.3f, 0.85f, 0.4f)).transform;
            Prim(PrimitiveType.Cube, "GripBg", root, new Vector3(-120f, 0f, 6f), new Vector3(34f, BarHeight + 6f, 4f), bg);
            g.gripFill = Prim(PrimitiveType.Cube, "GripFill", root, new Vector3(-120f, 0f, 0f), new Vector3(28f, 1f, 8f), new Color(0.3f, 0.6f, 1f)).transform;

            var stickBase = Prim(PrimitiveType.Cylinder, "StickBase", root, new Vector3(-10f, 0f, 6f), new Vector3(120f, 3f, 120f), bg);
            stickBase.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.stickBase = stickBase.GetComponent<ColorTint>();
            g.stickDot = Prim(PrimitiveType.Sphere, "StickDot", root, new Vector3(-10f, 0f, -12f), Vector3.one * 34f, Color.white).transform;

            g.button1 = Prim(PrimitiveType.Sphere, "Button1", root, new Vector3(110f, -40f, 0f), Vector3.one * 44f, Idle).GetComponent<ColorTint>();
            g.button2 = Prim(PrimitiveType.Sphere, "Button2", root, new Vector3(110f, 40f, 0f), Vector3.one * 44f, Idle).GetComponent<ColorTint>();
            if (isLeft)
                g.menu = Prim(PrimitiveType.Sphere, "Menu", root, new Vector3(175f, 40f, 0f), Vector3.one * 26f, Idle).GetComponent<ColorTint>();
            return g;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var go = DemoUI.CreatePrimitive(type, name, parent, pos, scale, color, false);
            DemoUI.DisableShadows(go);
            return go;
        }

        void UpdateGauge(Gauge g, OVRInput.Controller c)
        {
            if (g == null) return;
            SetBar(g.triggerFill, OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c));
            SetBar(g.gripFill, OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, c));

            Vector2 s = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c);
            g.stickDot.localPosition = new Vector3(-10f + s.x * 42f, s.y * 42f, -12f);
            g.stickBase.SetOverride(StateColor(OVRInput.Get(OVRInput.Button.PrimaryThumbstick, c), OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, c)));

            g.button1.SetOverride(StateColor(OVRInput.Get(OVRInput.Button.One, c), OVRInput.Get(OVRInput.Touch.One, c)));
            g.button2.SetOverride(StateColor(OVRInput.Get(OVRInput.Button.Two, c), OVRInput.Get(OVRInput.Touch.Two, c)));
            if (g.menu != null) g.menu.SetOverride(StateColor(OVRInput.Get(OVRInput.Button.Start, c), false));
        }

        static Color? StateColor(bool pressed, bool touched)
        {
            if (pressed) return Pressed;
            if (touched) return Touched;
            return null;
        }

        static void SetBar(Transform fill, float value)
        {
            float h = Mathf.Max(1f, value * BarHeight);
            fill.localScale = new Vector3(28f, h, 8f);
            Vector3 p = fill.localPosition;
            fill.localPosition = new Vector3(p.x, -BarHeight * 0.5f + h * 0.5f, p.z);
        }
    }
}
