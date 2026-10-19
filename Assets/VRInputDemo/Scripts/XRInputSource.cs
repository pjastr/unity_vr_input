using UnityEngine;

namespace VRInputDemo
{
    public enum HandSide { Left, Right }

    /// <summary>
    /// Ujednolicone źródło wejścia dla jednej ręki. Automatycznie przełącza się między:
    ///  - kontrolerem Touch (OVRInput: trigger = wybór, grip = chwyt),
    ///  - śledzoną dłonią (OVRHand: pinch = wybór i chwyt, pięść = chwyt).
    /// Pozostałe skrypty (chwytanie, promień, przyciski poke, akcje) czytają stan tylko stąd,
    /// więc działają identycznie dla kontrolerów i dłoni.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class XRInputSource : MonoBehaviour
    {
        [Header("Powiązania (uzupełniane automatycznie, jeśli puste)")]
        public HandSide side = HandSide.Left;
        public OVRCameraRig rig;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public HandGestureDetector gestures;

        [Header("Progi analogowe trigger / grip (histereza)")]
        [Range(0f, 1f)] public float pressThreshold = 0.55f;
        [Range(0f, 1f)] public float releaseThreshold = 0.35f;

        [Header("Wizualizacja")]
        [Tooltip("Mała kulka w miejscu, którym wciska się przyciski (czubek palca / końcówka kontrolera).")]
        public bool showPokeMarker = true;

        public static XRInputSource Left { get; private set; }
        public static XRInputSource Right { get; private set; }
        public static XRInputSource Get(HandSide s) => s == HandSide.Left ? Left : Right;

        public OVRInput.Controller Controller =>
            side == HandSide.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

        public bool IsHandTracked { get; private set; }
        public bool IsControllerActive { get; private set; }
        public bool IsActive => IsHandTracked || IsControllerActive;

        public string Label => IsHandTracked
            ? (side == HandSide.Left ? "Lewa dłoń" : "Prawa dłoń")
            : (side == HandSide.Left ? "Lewy kontroler" : "Prawy kontroler");

        public Transform GripAnchor =>
            rig == null ? null : (side == HandSide.Left ? rig.leftHandAnchor : rig.rightHandAnchor);
        public Transform ControllerAnchor =>
            rig == null ? null : (side == HandSide.Left ? rig.leftControllerAnchor : rig.rightControllerAnchor);

        // ---- Pozy (w przestrzeni świata) ----
        public Vector3 GrabPoint { get; private set; }
        public Quaternion GrabRotation { get; private set; } = Quaternion.identity;
        public bool PointerValid { get; private set; }
        public Vector3 PointerOrigin { get; private set; }
        public Vector3 PointerDirection { get; private set; } = Vector3.forward;
        public bool PokeValid { get; private set; }
        public Vector3 PokePoint { get; private set; }

        // ---- Akcje ----
        public float SelectValue { get; private set; }
        public float GrabValue { get; private set; }
        public bool SelectHeld { get; private set; }
        public bool SelectDown { get; private set; }
        public bool SelectUp { get; private set; }
        public bool GrabHeld { get; private set; }
        public bool GrabDown { get; private set; }
        public bool GrabUp { get; private set; }
        /// <summary>Czym rozpoczęto chwyt: "grip", "pinch" lub "pięść".</summary>
        public string GrabMethod { get; private set; } = "";

        // ---- Ruch (do rzucania) ----
        public Vector3 Velocity { get; private set; }
        public Vector3 AngularVelocity { get; private set; }

        const int VelocitySamples = 6;
        readonly Vector3[] samplePos = new Vector3[VelocitySamples];
        readonly float[] sampleTime = new float[VelocitySamples];
        int sampleIndex, sampleCount;
        Quaternion previousRotation = Quaternion.identity;
        bool grabbedWithFist;
        float hapticEnd = -1f;
        Transform pokeMarker;

        void Awake()
        {
            AutoWire();
            if (showPokeMarker)
            {
                var marker = DemoUI.CreatePrimitive(PrimitiveType.Sphere, "PokeMarker", transform, Vector3.zero,
                    Vector3.one * 0.014f, new Color(0.2f, 0.9f, 1f), false);
                DemoUI.DisableShadows(marker);
                pokeMarker = marker.transform;
                marker.SetActive(false);
            }
        }

        void OnEnable()
        {
            if (side == HandSide.Left) Left = this; else Right = this;
        }

        void OnDisable()
        {
            if (Left == this) Left = null;
            if (Right == this) Right = null;
            if (hapticEnd > 0f) OVRInput.SetControllerVibration(0f, 0f, Controller);
            hapticEnd = -1f;
        }

        /// <summary>Znajduje OVRCameraRig, OVRHand i OVRSkeleton w scenie, jeśli nie przypisano ich ręcznie.</summary>
        public void AutoWire()
        {
            if (rig == null) rig = FindAnyObjectByType<OVRCameraRig>();

            if (hand == null)
            {
                var wanted = side == HandSide.Left ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
                var anchor = GripAnchor;
                foreach (var h in Resources.FindObjectsOfTypeAll<OVRHand>())
                {
                    if (h == null || !h.gameObject.scene.IsValid()) continue; // pomija prefaby w projekcie
                    if (h.GetHand() == wanted || (anchor != null && h.transform.IsChildOf(anchor)))
                    {
                        hand = h;
                        break;
                    }
                }
            }

            if (hand != null && skeleton == null)
            {
                skeleton = hand.GetComponent<OVRSkeleton>();
                if (skeleton == null) skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
            }

            if (gestures == null) gestures = GetComponent<HandGestureDetector>();
            if (gestures != null)
            {
                gestures.side = side;
                if (gestures.hand == null) gestures.hand = hand;
                if (gestures.skeleton == null) gestures.skeleton = skeleton;
            }
        }

        void Update()
        {
            bool handTracked = hand != null && hand.isActiveAndEnabled && hand.IsTracked;
            bool systemGesture = handTracked && hand.IsSystemGestureInProgress;

            IsHandTracked = handTracked;
            IsControllerActive = !handTracked && OVRInput.IsControllerConnected(Controller);

            bool selectRaw = false, grabRaw = false, fist = false, pinching = false;
            PointerValid = false;
            PokeValid = false;

            if (IsControllerActive)
            {
                // --- KONTROLER: wartości analogowe 0..1 z OVRInput ---
                SelectValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, Controller);
                GrabValue = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, Controller);
                selectRaw = Hysteresis(SelectHeld, SelectValue);
                grabRaw = Hysteresis(GrabHeld, GrabValue);

                Transform t = ControllerAnchor != null ? ControllerAnchor : GripAnchor;
                if (t != null)
                {
                    GrabPoint = t.position;
                    GrabRotation = t.rotation;
                    PointerOrigin = t.position + t.forward * 0.05f;
                    PointerDirection = t.forward;
                    PointerValid = true;
                    PokePoint = t.position + t.forward * 0.06f;
                    PokeValid = true;
                }
            }
            else if (handTracked && !systemGesture)
            {
                // --- DŁOŃ: pinch z OVRHand, pięść z detektora gestów ---
                pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                fist = gestures != null && gestures.Current == HandGesture.Fist;
                SelectValue = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                GrabValue = fist ? 1f : SelectValue;
                selectRaw = pinching;
                grabRaw = pinching || fist;
            }
            else
            {
                SelectValue = 0f;
                GrabValue = 0f;
            }

            SelectDown = selectRaw && !SelectHeld;
            SelectUp = !selectRaw && SelectHeld;
            SelectHeld = selectRaw;

            GrabDown = grabRaw && !GrabHeld;
            GrabUp = !grabRaw && GrabHeld;
            GrabHeld = grabRaw;

            if (GrabDown)
            {
                grabbedWithFist = IsHandTracked && fist && !pinching;
                GrabMethod = IsControllerActive ? "grip" : (grabbedWithFist ? "pięść" : "pinch");
            }

            if (handTracked && !systemGesture) UpdateHandPoses();

            UpdateVelocity();
            UpdatePokeMarker();
            UpdateHaptics();
        }

        void UpdateHandPoses()
        {
            Transform anchor = GripAnchor;
            GrabRotation = anchor != null ? anchor.rotation : Quaternion.identity;

            if (gestures != null && gestures.HasJoints)
            {
                // Chwyt pięścią trzymamy w środku dłoni, chwyt pinch między kciukiem a palcem wskazującym.
                bool usePalm = GrabHeld ? grabbedWithFist : gestures.Current == HandGesture.Fist;
                GrabPoint = usePalm ? gestures.PalmCenter : gestures.PinchPoint;
                PokePoint = gestures.IndexTip;
                PokeValid = true;
            }
            else if (anchor != null)
            {
                GrabPoint = anchor.position;
            }

            // Promień z dłoni: "PointerPose" dostarczany przez system (ramię -> dłoń).
            if (hand.IsPointerPoseValid && hand.PointerPose != null)
            {
                Transform pp = hand.PointerPose;
                if (pp.parent == null && rig != null && rig.trackingSpace != null)
                {
                    // Bez rodzica OVRHand zapisuje pozę w przestrzeni śledzenia - przeliczamy do świata.
                    PointerOrigin = rig.trackingSpace.TransformPoint(pp.localPosition);
                    PointerDirection = rig.trackingSpace.rotation * (pp.localRotation * Vector3.forward);
                }
                else
                {
                    PointerOrigin = pp.position;
                    PointerDirection = pp.forward;
                }
                PointerValid = true;
            }
        }

        void UpdateVelocity()
        {
            if (!IsActive)
            {
                sampleCount = 0;
                Velocity = Vector3.zero;
                AngularVelocity = Vector3.zero;
                previousRotation = GrabRotation;
                return;
            }

            if (IsControllerActive && rig != null && rig.trackingSpace != null)
            {
                // Kontroler: prędkości bezpośrednio z OVRInput (przestrzeń śledzenia -> świat).
                Velocity = rig.trackingSpace.TransformVector(OVRInput.GetLocalControllerVelocity(Controller));
                AngularVelocity = rig.trackingSpace.TransformDirection(OVRInput.GetLocalControllerAngularVelocity(Controller));
                sampleCount = 0;
                previousRotation = GrabRotation;
                return;
            }

            // Dłoń: prędkość liczona z historii pozycji (kilka ostatnich klatek).
            samplePos[sampleIndex] = GrabPoint;
            sampleTime[sampleIndex] = Time.time;
            sampleIndex = (sampleIndex + 1) % VelocitySamples;
            sampleCount = Mathf.Min(sampleCount + 1, VelocitySamples);

            int newest = (sampleIndex - 1 + VelocitySamples) % VelocitySamples;
            int oldest = (sampleIndex - sampleCount + VelocitySamples) % VelocitySamples;
            float dt = sampleTime[newest] - sampleTime[oldest];
            Velocity = dt > 1e-4f ? (samplePos[newest] - samplePos[oldest]) / dt : Vector3.zero;

            float frameDt = Mathf.Max(Time.deltaTime, 1e-4f);
            Quaternion delta = GrabRotation * Quaternion.Inverse(previousRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 w = (float.IsNaN(axis.x) || float.IsInfinity(axis.x))
                ? Vector3.zero
                : axis * (angle * Mathf.Deg2Rad / frameDt);
            AngularVelocity = Vector3.Lerp(AngularVelocity, w, 0.5f);
            previousRotation = GrabRotation;
        }

        void UpdatePokeMarker()
        {
            if (pokeMarker == null) return;
            bool show = PokeValid && IsActive;
            if (pokeMarker.gameObject.activeSelf != show) pokeMarker.gameObject.SetActive(show);
            if (show) pokeMarker.position = PokePoint;
        }

        /// <summary>Wibracja kontrolera (dla dłoni nic nie robi). Amplituda i częstotliwość 0..1.</summary>
        public void Haptic(float amplitude, float duration, float frequency = 0.6f)
        {
            if (!IsControllerActive) return;
            OVRInput.SetControllerVibration(Mathf.Clamp01(frequency), Mathf.Clamp01(amplitude), Controller);
            hapticEnd = Time.unscaledTime + Mathf.Max(0.02f, duration);
        }

        void UpdateHaptics()
        {
            if (hapticEnd > 0f && Time.unscaledTime >= hapticEnd)
            {
                OVRInput.SetControllerVibration(0f, 0f, Controller);
                hapticEnd = -1f;
            }
        }

        bool Hysteresis(bool held, float value) => held ? value > releaseThreshold : value > pressThreshold;
    }
}
