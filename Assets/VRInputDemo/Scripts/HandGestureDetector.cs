using UnityEngine;

namespace VRInputDemo
{
    public enum HandGesture
    {
        None,
        OpenPalm,
        Fist,
        Point,
        Victory,
        ThumbsUp,
        ThumbsDown,
        Pinch
    }

    /// <summary>
    /// Rozpoznaje proste statyczne gesty dłoni na podstawie szkieletu OVRSkeleton
    /// oraz odczytuje szczypanie (pinch) i mikrogesty z OVRHand.
    ///
    /// Metoda: dla każdego palca liczony jest współczynnik wyprostu
    ///     ratio = |czubek - nadgarstek| / |kostka (MCP) - nadgarstek|
    /// Palec wyprostowany ma ~1.8-2.0, zgięty w pięść ~0.8-1.1. Dla kciuka liczona jest odległość
    /// czubka kciuka od kostki palca wskazującego, znormalizowana długością dłoni.
    /// Wartości są widoczne na panelu "Dłonie", więc progi łatwo dostroić w Inspectorze.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class HandGestureDetector : MonoBehaviour
    {
        public HandSide side = HandSide.Left;
        public OVRHand hand;
        public OVRSkeleton skeleton;

        [Header("Progi (dostrój na podstawie wartości z panelu)")]
        [Tooltip("Palec uznany za wyprostowany, gdy współczynnik > próg.")]
        public float fingerExtendedRatio = 1.5f;
        [Tooltip("Palec uznany za zgięty, gdy współczynnik < próg.")]
        public float fingerCurledRatio = 1.25f;
        [Tooltip("Kciuk odstaje od dłoni, gdy (czubek kciuka - kostka wskazującego) / długość dłoni > próg.")]
        public float thumbExtendedRatio = 0.7f;
        [Tooltip("Jak bardzo kciuk musi być skierowany w górę/dół (składowa Y kierunku, 0..1).")]
        public float thumbDirectionThreshold = 0.5f;
        [Tooltip("Czas (s), przez jaki gest musi być stabilny, zanim zostanie uznany.")]
        public float stableTime = 0.12f;
        public bool logGestureChanges = true;

        // ---- Wyniki ----
        public HandGesture Current { get; private set; } = HandGesture.None;
        public float CurrentSince { get; private set; }
        public float CurrentDuration => Time.time - CurrentSince;

        /// <summary>Współczynniki wyprostu: 0 kciuk, 1 wskazujący, 2 środkowy, 3 serdeczny, 4 mały.</summary>
        public readonly float[] Ratios = new float[5];

        public bool HasJoints { get; private set; }
        public Vector3 WristPosition { get; private set; }
        public Vector3 PalmCenter { get; private set; }
        public Vector3 IndexTip { get; private set; }
        public Vector3 ThumbTip { get; private set; }
        public Vector3 PinchPoint { get; private set; }
        public Vector3 ThumbDirection { get; private set; }

        /// <summary>Mikrogest wykryty w tej klatce (NoGesture, gdy brak).</summary>
        public OVRHand.MicrogestureType MicrogestureThisFrame { get; private set; } = OVRHand.MicrogestureType.NoGesture;
        public OVRHand.MicrogestureType LastMicrogesture { get; private set; } = OVRHand.MicrogestureType.NoGesture;
        public float LastMicrogestureTime { get; private set; } = -1000f;

        public string SideLabel => side == HandSide.Left ? "Lewa dłoń" : "Prawa dłoń";

        HandGesture candidate = HandGesture.None;
        float candidateSince;
        int cachedSkeletonVersion = -1;
        OVRHand.MicrogestureType previousMicrogesture = OVRHand.MicrogestureType.NoGesture;

        Transform wrist, palm;
        readonly Transform[] knuckles = new Transform[5];
        readonly Transform[] tips = new Transform[5];

        void Update()
        {
            MicrogestureThisFrame = OVRHand.MicrogestureType.NoGesture;

            if (hand == null || !hand.isActiveAndEnabled || !hand.IsTracked)
            {
                HasJoints = false;
                previousMicrogesture = OVRHand.MicrogestureType.NoGesture;
                SetCandidate(HandGesture.None);
                return;
            }

            UpdateMicrogestures();

            if (!UpdateJoints())
            {
                SetCandidate(hand.GetFingerIsPinching(OVRHand.HandFinger.Index) ? HandGesture.Pinch : HandGesture.None);
                return;
            }

            SetCandidate(hand.IsSystemGestureInProgress ? HandGesture.None : Classify());
        }

        void UpdateMicrogestures()
        {
            var mg = hand.GetMicrogestureType();
            bool valid = mg != OVRHand.MicrogestureType.NoGesture && mg != OVRHand.MicrogestureType.Invalid;
            if (valid && mg != previousMicrogesture)
            {
                MicrogestureThisFrame = mg;
                LastMicrogesture = mg;
                LastMicrogestureTime = Time.time;
                if (logGestureChanges) DemoLog.Add($"{SideLabel}: mikrogest {MicrogestureName(mg)}");
            }
            previousMicrogesture = mg;
        }

        bool UpdateJoints()
        {
            if (skeleton == null || !skeleton.IsInitialized || !skeleton.IsDataValid)
            {
                HasJoints = false;
                return false;
            }

            if (cachedSkeletonVersion != skeleton.SkeletonChangedCount || wrist == null) CacheBones();

            HasJoints = wrist != null && tips[0] != null;
            for (int f = 1; f < 5 && HasJoints; f++) HasJoints = tips[f] != null && knuckles[f] != null;
            if (!HasJoints) return false;

            Vector3 w = wrist.position;
            WristPosition = w;
            float handLength = Vector3.Distance(w, knuckles[1].position);
            if (handLength < 1e-4f) { HasJoints = false; return false; }

            for (int f = 1; f < 5; f++)
            {
                float k = Mathf.Max(1e-4f, Vector3.Distance(knuckles[f].position, w));
                Ratios[f] = Vector3.Distance(tips[f].position, w) / k;
            }
            Ratios[0] = Vector3.Distance(tips[0].position, knuckles[1].position) / handLength;

            IndexTip = tips[1].position;
            ThumbTip = tips[0].position;
            PinchPoint = (IndexTip + ThumbTip) * 0.5f;
            PalmCenter = palm != null ? palm.position : Vector3.Lerp(w, knuckles[2].position, 0.55f);

            Vector3 thumbBase = knuckles[0] != null ? knuckles[0].position : w;
            Vector3 dir = ThumbTip - thumbBase;
            ThumbDirection = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
            return true;
        }

        HandGesture Classify()
        {
            if (hand.GetFingerIsPinching(OVRHand.HandFinger.Index)) return HandGesture.Pinch;

            bool thumbOut = Ratios[0] > thumbExtendedRatio;

            if (Curl(1) && Curl(2) && Curl(3) && Curl(4))
            {
                if (thumbOut)
                {
                    if (ThumbDirection.y > thumbDirectionThreshold) return HandGesture.ThumbsUp;
                    if (ThumbDirection.y < -thumbDirectionThreshold) return HandGesture.ThumbsDown;
                }
                return HandGesture.Fist;
            }
            if (Ext(1) && Ext(2) && Ext(3) && Ext(4)) return HandGesture.OpenPalm;
            if (Ext(1) && Curl(2) && Curl(3) && Curl(4)) return HandGesture.Point;
            if (Ext(1) && Ext(2) && Curl(3) && Curl(4)) return HandGesture.Victory;
            return HandGesture.None;
        }

        bool Ext(int finger) => Ratios[finger] > fingerExtendedRatio;
        bool Curl(int finger) => Ratios[finger] < fingerCurledRatio;

        void SetCandidate(HandGesture g)
        {
            if (g != candidate)
            {
                candidate = g;
                candidateSince = Time.time;
            }

            // Pinch reaguje natychmiast (OVRHand ma własną histerezę), pozostałe gesty po czasie stabilizacji.
            float need = (candidate == HandGesture.Pinch || Current == HandGesture.Pinch) ? 0f : stableTime;
            if (Current != candidate && Time.time - candidateSince >= need)
            {
                Current = candidate;
                CurrentSince = Time.time;
                if (logGestureChanges && Current != HandGesture.None && Current != HandGesture.Pinch)
                    DemoLog.Add($"{SideLabel}: gest {GestureName(Current)}");
            }
        }

        /// <summary>
        /// Wyszukuje potrzebne kości po nazwie. Działa dla obu wersji szkieletu Meta:
        /// starszego OVR (Hand_WristRoot, Hand_Index1, Hand_IndexTip...) i OpenXR
        /// (XRHand_Wrist, XRHand_IndexProximal, XRHand_IndexTip...).
        /// </summary>
        void CacheBones()
        {
            cachedSkeletonVersion = skeleton.SkeletonChangedCount;
            wrist = palm = null;
            for (int i = 0; i < 5; i++) { knuckles[i] = null; tips[i] = null; }

            var bones = skeleton.Bones;
            if (bones == null) return;
            var type = skeleton.GetSkeletonType();

            foreach (var bone in bones)
            {
                if (bone == null || bone.Transform == null) continue;
                string label = OVRSkeleton.BoneLabelFromBoneId(type, bone.Id);
                string name = bone.Transform.name;
                Assign(label, bone.Transform);
                Assign(name, bone.Transform);
            }
        }

        void Assign(string n, Transform t)
        {
            if (string.IsNullOrEmpty(n)) return;

            if (n.EndsWith("WristRoot") || n.EndsWith("_Wrist")) { if (wrist == null) wrist = t; return; }
            if (n.EndsWith("_Palm")) { if (palm == null) palm = t; return; }

            TrySet(n, t, 0, "ThumbTip", "Thumb2", "ThumbProximal");
            TrySet(n, t, 1, "IndexTip", "Index1", "IndexProximal");
            TrySet(n, t, 2, "MiddleTip", "Middle1", "MiddleProximal");
            TrySet(n, t, 3, "RingTip", "Ring1", "RingProximal");
            TrySet(n, t, 4, "PinkyTip", "Pinky1", "LittleProximal");
            if (n.EndsWith("LittleTip") && tips[4] == null) tips[4] = t;
        }

        void TrySet(string n, Transform t, int finger, string tipSuffix, string knuckleSuffixA, string knuckleSuffixB)
        {
            if (n.EndsWith(tipSuffix)) { if (tips[finger] == null) tips[finger] = t; }
            else if (n.EndsWith(knuckleSuffixA) || n.EndsWith(knuckleSuffixB)) { if (knuckles[finger] == null) knuckles[finger] = t; }
        }

        public static string GestureName(HandGesture g)
        {
            switch (g)
            {
                case HandGesture.OpenPalm: return "Otwarta dłoń";
                case HandGesture.Fist: return "Pięść";
                case HandGesture.Point: return "Wskazywanie";
                case HandGesture.Victory: return "Znak V";
                case HandGesture.ThumbsUp: return "Kciuk w górę";
                case HandGesture.ThumbsDown: return "Kciuk w dół";
                case HandGesture.Pinch: return "Pinch (szczypnięcie)";
                default: return "brak";
            }
        }

        public static string MicrogestureName(OVRHand.MicrogestureType mg)
        {
            switch (mg)
            {
                case OVRHand.MicrogestureType.SwipeLeft: return "przesunięcie w lewo";
                case OVRHand.MicrogestureType.SwipeRight: return "przesunięcie w prawo";
                case OVRHand.MicrogestureType.SwipeForward: return "przesunięcie do przodu";
                case OVRHand.MicrogestureType.SwipeBackward: return "przesunięcie do tyłu";
                case OVRHand.MicrogestureType.ThumbTap: return "stuknięcie kciukiem";
                default: return "brak";
            }
        }
    }
}
