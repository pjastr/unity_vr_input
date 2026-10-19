using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Chwytanie z bliska bez Interaction SDK: szuka najbliższego Grabbable w promieniu od punktu chwytu,
    /// po akcji "grab" przypina obiekt do ręki, po puszczeniu nadaje mu prędkość ręki (rzut).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class SimpleGrabber : MonoBehaviour
    {
        public XRInputSource source;
        [Tooltip("Zasięg chwytu kontrolerem (m).")]
        public float controllerRadius = 0.07f;
        [Tooltip("Zasięg chwytu dłonią (m).")]
        public float handRadius = 0.09f;
        [Tooltip("Mnożnik prędkości przy rzucie.")]
        public float throwMultiplier = 1.2f;

        public Grabbable Held { get; private set; }
        public bool IsHolding => Held != null;
        public Grabbable Hovered => hovered;

        Grabbable hovered;
        Vector3 positionOffset;
        Quaternion rotationOffset;

        void Awake()
        {
            if (source == null) source = GetComponent<XRInputSource>();
        }

        void OnDisable()
        {
            SetHovered(null);
            ForceRelease();
        }

        void Update()
        {
            if (source == null || !source.IsActive)
            {
                SetHovered(null);
                if (Held != null) ForceRelease();
                return;
            }

            if (Held == null)
            {
                SetHovered(FindNearest());
                if (source.GrabDown && hovered != null) Grab(hovered);
            }
            else if (!source.GrabHeld)
            {
                Release();
            }
        }

        void LateUpdate()
        {
            if (Held == null || source == null) return;
            Quaternion rot = source.GrabRotation;
            Held.transform.SetPositionAndRotation(source.GrabPoint + rot * positionOffset, rot * rotationOffset);
        }

        Grabbable FindNearest()
        {
            Vector3 p = source.GrabPoint;
            float radius = source.IsHandTracked ? handRadius : controllerRadius;
            var hits = Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore);

            Grabbable best = null;
            float bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var rb = hit.attachedRigidbody;
                if (rb == null) continue;
                var g = rb.GetComponent<Grabbable>();
                if (g == null) continue;
                float d = (hit.ClosestPoint(p) - p).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = g;
                }
            }
            return best;
        }

        void Grab(Grabbable g)
        {
            SetHovered(null);
            Held = g;
            g.OnGrabbed(this);

            Quaternion inv = Quaternion.Inverse(source.GrabRotation);
            positionOffset = inv * (g.transform.position - source.GrabPoint);
            rotationOffset = inv * g.transform.rotation;

            source.Haptic(0.5f, 0.06f);
            DemoLog.Add($"{source.Label}: chwyt \"{g.name}\" ({source.GrabMethod})");
        }

        void Release()
        {
            var g = Held;
            Held = null;
            Vector3 v = source.Velocity * throwMultiplier;
            g.OnReleased(v, source.AngularVelocity);
            source.Haptic(0.25f, 0.04f);
            DemoLog.Add(v.magnitude > 1f
                ? $"{source.Label}: rzut \"{g.name}\" ({v.magnitude:0.0} m/s)"
                : $"{source.Label}: puszczono \"{g.name}\"");
        }

        /// <summary>Puszcza obiekt bez nadawania prędkości (np. gdy chwyci go druga ręka).</summary>
        public void ForceRelease()
        {
            if (Held == null) return;
            var g = Held;
            Held = null;
            g.OnReleased(Vector3.zero, Vector3.zero);
        }

        void SetHovered(Grabbable g)
        {
            if (hovered == g) return;
            if (hovered != null) hovered.SetHover(false);
            hovered = g;
            if (hovered != null)
            {
                hovered.SetHover(true);
                if (source != null) source.Haptic(0.15f, 0.02f);
            }
        }
    }
}
