using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Zapasowy model kontrolera z prymitywów - pokazywany tylko, gdy kontroler jest połączony i śledzony.
    /// (Używany, gdy w pakiecie Meta nie znaleziono prefabu OVRControllerPrefab.)
    /// </summary>
    public class ControllerVisibility : MonoBehaviour
    {
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;

        Renderer[] renderers;
        int lastState = -1;

        void Awake() => renderers = GetComponentsInChildren<Renderer>(true);

        void Update()
        {
            bool show = OVRInput.IsControllerConnected(controller) && OVRInput.GetControllerPositionTracked(controller);
            int state = show ? 1 : 0;
            if (state == lastState) return;
            lastState = state;
            foreach (var r in renderers) if (r != null) r.enabled = show;
        }
    }
}
