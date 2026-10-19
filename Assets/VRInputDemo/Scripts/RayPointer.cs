using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Promień wskazujący z kontrolera (oś kontrolera) lub z dłoni (PointerPose z OVRHand).
    /// Akcja "select" (trigger / pinch):
    ///  - na RayTarget: zmiana koloru celu,
    ///  - na PokeButton: zdalne wciśnięcie przycisku,
    ///  - na Grabbable: przyciągnięcie obiektu do ręki (rzut balistyczny).
    /// Nad obiektami strefy ISDK promień się chowa - tam działa promień z Interaction SDK.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class RayPointer : MonoBehaviour
    {
        public XRInputSource source;
        public SimpleGrabber grabber;
        public float maxDistance = 8f;
        public float beamWidth = 0.004f;
        public Color idleColor = new Color(0.9f, 0.9f, 0.95f);
        public Color hoverColor = new Color(0.3f, 0.9f, 1f);
        public Color pressColor = new Color(1f, 0.85f, 0.2f);

        Transform beam, cursor;
        ColorTint beamTint, cursorTint;
        RayTarget hoverTarget;
        Grabbable hoverGrabbable;
        PokeButton hoverButton;

        void Awake()
        {
            if (source == null) source = GetComponent<XRInputSource>();
            if (grabber == null) grabber = GetComponent<SimpleGrabber>();

            var b = DemoUI.CreatePrimitive(PrimitiveType.Cube, "RayBeam", transform, Vector3.zero, Vector3.one, idleColor, false);
            var c = DemoUI.CreatePrimitive(PrimitiveType.Sphere, "RayCursor", transform, Vector3.zero, Vector3.one * 0.025f, idleColor, false);
            DemoUI.DisableShadows(b);
            DemoUI.DisableShadows(c);
            beam = b.transform;
            cursor = c.transform;
            beamTint = b.GetComponent<ColorTint>();
            cursorTint = c.GetComponent<ColorTint>();
            SetVisible(false, false);
        }

        void OnDisable()
        {
            SetHover(null, null, null);
            SetVisible(false, false);
        }

        void Update()
        {
            bool show = source != null && source.IsActive && source.PointerValid && (grabber == null || !grabber.IsHolding);
            if (!show)
            {
                SetHover(null, null, null);
                SetVisible(false, false);
                return;
            }

            Vector3 origin = source.PointerOrigin;
            Vector3 dir = source.PointerDirection.normalized;
            float length = maxDistance;
            RayTarget target = null;
            Grabbable grabbable = null;
            PokeButton button = null;
            bool hitSomething = false;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                // Obiekty z Interaction SDK obsługuje ISDK - nasz promień ustępuje miejsca.
                if (hit.collider.GetComponentInParent<IsdkZone>() != null)
                {
                    SetHover(null, null, null);
                    SetVisible(false, false);
                    return;
                }

                hitSomething = true;
                length = hit.distance;
                target = hit.collider.GetComponentInParent<RayTarget>();
                button = target == null ? hit.collider.GetComponentInParent<PokeButton>() : null;
                if (target == null && button == null && hit.rigidbody != null) grabbable = hit.rigidbody.GetComponent<Grabbable>();
                cursor.position = hit.point;
            }

            SetHover(target, grabbable, button);

            beam.SetPositionAndRotation(origin + dir * (length * 0.5f), Quaternion.LookRotation(dir));
            beam.localScale = new Vector3(beamWidth, beamWidth, length);
            SetVisible(true, hitSomething);

            bool interactive = target != null || grabbable != null || button != null;
            Color col = source.SelectHeld ? pressColor : (interactive ? hoverColor : idleColor);
            beamTint.SetBaseColor(col);
            cursorTint.SetBaseColor(col);

            if (source.SelectDown)
            {
                if (target != null) target.Select(source);
                else if (button != null) button.Press(source, "promień");
                else if (grabbable != null && !grabbable.IsHeld) Pull(grabbable);
            }
        }

        void Pull(Grabbable g)
        {
            // Rzut balistyczny: obiekt doleci do ręki po czasie t.
            const float t = 0.6f;
            Vector3 target = source.GrabPoint + Vector3.up * 0.05f;
            Vector3 v = (target - g.transform.position) / t - 0.5f * (g.Body.useGravity ? Physics.gravity : Vector3.zero) * t;
            g.Body.isKinematic = false;
            g.Body.linearVelocity = v;
            g.Body.angularVelocity = Random.insideUnitSphere * 2f;
            source.Haptic(0.6f, 0.08f);
            DemoLog.Add($"{source.Label}: przyciąganie promieniem \"{g.name}\"");
        }

        void SetHover(RayTarget t, Grabbable g, PokeButton b)
        {
            if (t != hoverTarget)
            {
                if (hoverTarget != null) hoverTarget.SetHover(false);
                hoverTarget = t;
                if (hoverTarget != null) { hoverTarget.SetHover(true); source.Haptic(0.12f, 0.02f); }
            }
            if (g != hoverGrabbable)
            {
                if (hoverGrabbable != null) hoverGrabbable.SetHover(false);
                hoverGrabbable = g;
                if (hoverGrabbable != null) { hoverGrabbable.SetHover(true); source.Haptic(0.12f, 0.02f); }
            }
            if (b != hoverButton)
            {
                hoverButton = b;
                if (hoverButton != null) source.Haptic(0.12f, 0.02f);
            }
        }

        void SetVisible(bool beamVisible, bool cursorVisible)
        {
            if (beam != null && beam.gameObject.activeSelf != beamVisible) beam.gameObject.SetActive(beamVisible);
            if (cursor != null && cursor.gameObject.activeSelf != cursorVisible) cursor.gameObject.SetActive(cursorVisible);
        }
    }
}
