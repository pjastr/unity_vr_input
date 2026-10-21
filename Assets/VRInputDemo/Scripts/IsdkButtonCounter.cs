using UnityEngine;
using UnityEngine.UI;

namespace VRInputDemo
{
    /// <summary>
    /// Licznik kliknięć przycisku UI w strefie ISDK. Sam przycisk jest zwykłym Unity UI Button -
    /// "klikalny" palcem lub promieniem staje się po nałożeniu bloków Poke Interaction / Ray Interaction.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class IsdkButtonCounter : MonoBehaviour
    {
        public Text label;
        public string caption = "Kliknięcia";

        int count;

        void Awake()
        {
            GetComponent<Button>().onClick.AddListener(OnClick);
            Refresh();
        }

        void OnClick()
        {
            count++;
            Refresh();
            DemoLog.Add($"ISDK: kliknięto przycisk UI ({count})");
        }

        void Refresh()
        {
            if (label != null) label.text = $"{caption}: {count}";
        }
    }
}
