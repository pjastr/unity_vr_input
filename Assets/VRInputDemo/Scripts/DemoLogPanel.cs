using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VRInputDemo
{
    /// <summary>Wyświetla zawartość DemoLog na panelu w świecie VR.</summary>
    public class DemoLogPanel : MonoBehaviour
    {
        public Text text;

        int shownVersion = -1;
        readonly StringBuilder sb = new StringBuilder();

        void Update()
        {
            if (text == null || shownVersion == DemoLog.Version) return;
            shownVersion = DemoLog.Version;

            sb.Clear();
            var lines = DemoLog.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                // Najnowszy wpis wyróżniony kolorem.
                if (i == lines.Count - 1) sb.Append("<color=#FFE680>").Append(lines[i]).Append("</color>");
                else sb.Append(lines[i]);
                sb.Append('\n');
            }
            text.text = sb.ToString();
        }
    }
}
