using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VRInputDemo
{
    /// <summary>
    /// Panel "Dłonie": stan śledzenia dłoni (OVRHand), rozpoznany gest, siła szczypnięcia
    /// dla każdego palca, współczynniki wyprostu palców (do strojenia progów) i ostatni mikrogest.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class HandInfoPanel : MonoBehaviour
    {
        public Text header;
        public Text leftText;
        public Text rightText;
        public XRInputSource left;
        public XRInputSource right;
        public float refreshInterval = 0.1f;

        float nextRefresh;
        readonly StringBuilder sb = new StringBuilder();

        static readonly OVRHand.HandFinger[] PinchFingers =
        {
            OVRHand.HandFinger.Index, OVRHand.HandFinger.Middle, OVRHand.HandFinger.Ring, OVRHand.HandFinger.Pinky
        };
        static readonly string[] PinchNames = { "wskazujący", "środkowy", "serdeczny", "mały" };

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;

            if (left == null) left = XRInputSource.Left;
            if (right == null) right = XRInputSource.Right;

            if (header != null)
            {
                string mode = (left != null && left.IsHandTracked) || (right != null && right.IsHandTracked)
                    ? "<color=#7CFC7C>dłonie</color>"
                    : "<color=#FFD040>kontrolery</color> (odłóż je, aby śledzić dłonie)";
                header.text = "<b>DŁONIE</b>  <color=#9AB>OVRHand / OVRSkeleton</color>\nTryb wejścia: " + mode;
            }
            if (leftText != null) leftText.text = Describe(left, "LEWA");
            if (rightText != null) rightText.text = Describe(right, "PRAWA");
        }

        string Describe(XRInputSource src, string title)
        {
            sb.Clear();
            sb.Append("<b>").Append(title).AppendLine("</b>");

            if (src == null || src.hand == null)
            {
                sb.AppendLine("<color=#FF7070>Brak OVRHand w scenie.</color>");
                sb.Append("Dodaj Building Block \"Hand Tracking\".");
                return sb.ToString();
            }

            var h = src.hand;
            var g = src.gestures;

            sb.AppendLine($"Śledzona: {DemoUI.YesNo(h.IsTracked)}   pewność: {h.HandConfidence}");
            if (!h.IsTracked)
            {
                sb.Append("<color=#A0A0A0>(dłoń niewidoczna)</color>");
                return sb.ToString();
            }

            sb.AppendLine($"Skala: {h.HandScale:0.00}   dominująca: {DemoUI.YesNo(h.IsDominantHand)}");
            if (g != null)
                sb.AppendLine($"Gest: <size=38><b><color=#FFE680>{HandGestureDetector.GestureName(g.Current)}</color></b></size>  {g.CurrentDuration:0.0} s");

            sb.AppendLine("Pinch (siła):");
            for (int i = 0; i < PinchFingers.Length; i++)
            {
                float s = h.GetFingerPinchStrength(PinchFingers[i]);
                bool on = h.GetFingerIsPinching(PinchFingers[i]);
                sb.Append("  ").Append(PinchNames[i]).Append(' ').Append(s.ToString("0.00")).Append(' ')
                  .Append(DemoUI.Bar(s, 8)).AppendLine(on ? " <color=#7CFC7C><b>PINCH</b></color>" : "");
            }

            if (g != null && g.HasJoints)
            {
                var r = g.Ratios;
                sb.AppendLine($"Wyprost K/W/Ś/S/M: {r[0]:0.00} {r[1]:0.00} {r[2]:0.00} {r[3]:0.00} {r[4]:0.00}");
            }
            else
            {
                sb.AppendLine("<color=#A0A0A0>Szkielet niedostępny</color>");
            }

            if (g != null)
            {
                float ago = Time.time - g.LastMicrogestureTime;
                sb.AppendLine(ago < 600f
                    ? $"Mikrogest: {HandGestureDetector.MicrogestureName(g.LastMicrogesture)} ({ago:0.0} s temu)"
                    : "Mikrogest: (jeszcze brak)");
            }

            sb.Append($"Pointer: {DemoUI.YesNo(h.IsPointerPoseValid)}  gest systemowy: {DemoUI.YesNo(h.IsSystemGestureInProgress)}");
            return sb.ToString();
        }
    }
}
