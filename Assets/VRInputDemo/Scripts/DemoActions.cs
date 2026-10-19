using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Centralne mapowanie wejść na akcje demo:
    ///  - przyciski kontrolerów (A, B, X, Y, Menu, klik gałki) - OVRInput.GetDown,
    ///  - gesty dłoni trzymane przez określony czas (V, kciuk w górę/dół) - HandGestureDetector,
    ///  - mikrogesty kciuka (przesunięcia, stuknięcie) - OVRHand.GetMicrogestureType.
    /// Każda akcja trafia do dziennika zdarzeń (panel + logcat).
    /// </summary>
    [DefaultExecutionOrder(130)]
    public class DemoActions : MonoBehaviour
    {
        public static DemoActions Instance { get; private set; }

        [Header("Powiązania")]
        public XRInputSource left;
        public XRInputSource right;
        public PlayerLocomotion locomotion;
        [Tooltip("Obiekt nadrzędny paneli (pokaż/ukryj).")]
        public GameObject panelsRoot;

        [Header("Ustawienia")]
        public int maxSpawned = 30;
        public float projectileSpeed = 9f;
        [Tooltip("Ile sekund trzymać kciuk w górę/dół, aby wywołać akcję.")]
        public float holdToConfirm = 1f;
        [Tooltip("Długość kroku wykonywanego mikrogestem (m).")]
        public float microgestureStep = 0.75f;

        [Header("Współpraca z Interaction SDK")]
        [Tooltip("Gdy w scenie jest rig Interaction SDK, ukryj własne modele dłoni/kontrolerów (ISDK rysuje swoje).")]
        public bool hideOwnVisualsWhenIsdkRigPresent = true;
        [Tooltip("Gdy w scenie jest rig Interaction SDK, wyłącz własną lokomocję gałkami (ISDK ma własną).")]
        public bool disableOwnLocomotionWhenIsdkRigPresent;
        [Tooltip("Renderery własnych dłoni i kontrolerów (uzupełnia skrypt budujący scenę).")]
        public List<GameObject> ownVisualRoots = new List<GameObject>();

        readonly List<GameObject> spawned = new List<GameObject>();
        bool gravityOn = true;
        bool hapticDemoRunning;

        HandGesture leftGesture, rightGesture;
        bool leftFired, rightFired;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (left == null) left = XRInputSource.Left;
            if (right == null) right = XRInputSource.Right;
            if (locomotion == null) locomotion = FindAnyObjectByType<PlayerLocomotion>();

            HandleIsdkRig();
            DemoLog.Add("Start demo. Użyj kontrolerów albo odłóż je i pokaż dłonie.");
        }

        void Update()
        {
            // ---------------- KONTROLERY (OVRInput) ----------------
            // Button.One/Two z maską RTouch = A/B, z maską LTouch = X/Y.
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Shoot(right, "przycisk A");
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) RandomizeColors("Przycisk B");
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) SpawnBallNear(left, "Przycisk X");
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) StartHapticDemo();
            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch)) ResetScene("Przycisk Menu");
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch)) TogglePanels("Klik lewej gałki");

            // ---------------- DŁONIE (gesty i mikrogesty) ----------------
            HandleHand(left, ref leftGesture, ref leftFired);
            HandleHand(right, ref rightGesture, ref rightFired);

            spawned.RemoveAll(go => go == null);
        }

        void HandleHand(XRInputSource src, ref HandGesture lastGesture, ref bool fired)
        {
            if (src == null || !src.IsHandTracked || src.gestures == null)
            {
                lastGesture = HandGesture.None;
                fired = false;
                return;
            }

            var g = src.gestures;

            // Mikrogesty: zdarzenia jednorazowe (kciuk przesuwany po boku palca wskazującego).
            switch (g.MicrogestureThisFrame)
            {
                case OVRHand.MicrogestureType.SwipeLeft:
                    if (locomotion != null) locomotion.SnapTurn(-locomotion.snapAngle, $"{src.Label}: mikrogest -> obrót w lewo");
                    break;
                case OVRHand.MicrogestureType.SwipeRight:
                    if (locomotion != null) locomotion.SnapTurn(locomotion.snapAngle, $"{src.Label}: mikrogest -> obrót w prawo");
                    break;
                case OVRHand.MicrogestureType.SwipeForward:
                    if (locomotion != null) locomotion.Step(microgestureStep, $"{src.Label}: mikrogest -> krok do przodu");
                    break;
                case OVRHand.MicrogestureType.SwipeBackward:
                    if (locomotion != null) locomotion.Step(-microgestureStep, $"{src.Label}: mikrogest -> krok do tyłu");
                    break;
                case OVRHand.MicrogestureType.ThumbTap:
                    Shoot(src, "mikrogest: stuknięcie kciukiem");
                    break;
            }

            // Gesty statyczne: akcja raz na każde "wejście" w gest, po czasie przytrzymania.
            if (g.Current != lastGesture)
            {
                lastGesture = g.Current;
                fired = false;
            }
            if (fired) return;

            float held = g.CurrentDuration;
            if (g.Current == HandGesture.Victory && held > 0.3f)
            {
                SpawnBall(g.PalmCenter + Vector3.up * 0.15f, $"{src.Label}: gest V");
                fired = true;
            }
            else if (g.Current == HandGesture.ThumbsUp && held > holdToConfirm)
            {
                ResetScene($"{src.Label}: kciuk w górę");
                fired = true;
            }
            else if (g.Current == HandGesture.ThumbsDown && held > holdToConfirm)
            {
                TogglePanels($"{src.Label}: kciuk w dół");
                fired = true;
            }
        }

        // ======================= AKCJE =======================

        /// <summary>Akcje wywoływane przez przyciski poke na stole (wpis do dziennika robi sam przycisk).</summary>
        public void Perform(PokeAction action, Vector3 origin)
        {
            switch (action)
            {
                case PokeAction.SpawnBall: SpawnBall(origin, null); break;
                case PokeAction.RandomizeColors: RandomizeColors(null); break;
                case PokeAction.ToggleGravity: SetGravity(!gravityOn, null); break;
                case PokeAction.ResetScene: ResetScene(null); break;
            }
        }

        public void Shoot(XRInputSource src, string reason)
        {
            if (src == null || !src.PointerValid)
            {
                DemoLog.Add($"{reason}: brak pozy wskaźnika - nie można strzelić");
                return;
            }

            Vector3 dir = src.PointerDirection.normalized;
            var go = DemoUI.CreatePrimitive(PrimitiveType.Sphere, "Pocisk", null, src.PointerOrigin + dir * 0.08f,
                Vector3.one * 0.05f, new Color(1f, 0.55f, 0.1f));
            DemoUI.DisableShadows(go);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.useGravity = gravityOn;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = dir * projectileSpeed;
            go.AddComponent<Projectile>();
            Track(go);

            src.Haptic(0.8f, 0.08f, 0.9f);
            DemoLog.Add($"{src.Label}: strzał ({reason})");
        }

        public void SpawnBallNear(XRInputSource src, string reason)
        {
            Vector3 pos;
            if (src != null && src.IsActive)
                pos = src.GrabPoint + src.GrabRotation * Vector3.forward * 0.15f + Vector3.up * 0.05f;
            else
                pos = HeadPosition() + HeadForward() * 0.5f;
            SpawnBall(pos, reason);
            if (src != null) src.Haptic(0.3f, 0.05f);
        }

        public void SpawnBall(Vector3 position, string reason)
        {
            var go = DemoUI.CreatePrimitive(PrimitiveType.Sphere, "Kula", null, position, Vector3.one * 0.09f,
                Random.ColorHSV(0f, 1f, 0.5f, 0.9f, 0.7f, 1f));
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            ApplyGravitySettings(rb);
            go.AddComponent<Grabbable>().spawned = true;
            Track(go);
            if (reason != null) DemoLog.Add($"{reason} -> nowa kula");
        }

        public void RandomizeColors(string reason)
        {
            foreach (var g in Grabbable.All)
                if (g.Tint != null) g.Tint.SetBaseColor(Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.7f, 1f));
            if (reason != null) DemoLog.Add($"{reason} -> losowe kolory obiektów");
        }

        public void SetGravity(bool on, string reason)
        {
            gravityOn = on;
            foreach (var g in Grabbable.All)
            {
                ApplyGravitySettings(g.Body);
                if (!on && !g.IsHeld) g.Body.AddForce(Vector3.up * 0.6f + Random.insideUnitSphere * 0.2f, ForceMode.VelocityChange);
            }
            if (reason != null) DemoLog.Add($"{reason} -> grawitacja {(on ? "włączona" : "wyłączona")}");
            else DemoLog.Add($"Grawitacja {(on ? "włączona" : "wyłączona")}");
        }

        void ApplyGravitySettings(Rigidbody rb)
        {
            rb.useGravity = gravityOn;
            rb.linearDamping = gravityOn ? 0f : 0.8f;
            rb.angularDamping = gravityOn ? 0.05f : 0.8f;
        }

        public void ResetScene(string reason)
        {
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            if (!gravityOn) SetGravity(true, null);
            foreach (var g in Grabbable.All.ToArray())
                if (!g.spawned) g.ResetPose();
            if (reason != null) DemoLog.Add($"{reason} -> reset sceny");
        }

        public void TogglePanels(string reason)
        {
            if (panelsRoot == null) return;
            panelsRoot.SetActive(!panelsRoot.activeSelf);
            DemoLog.Add($"{reason} -> panele {(panelsRoot.activeSelf ? "widoczne" : "ukryte")}");
        }

        void StartHapticDemo()
        {
            if (!hapticDemoRunning) StartCoroutine(HapticDemo());
        }

        IEnumerator HapticDemo()
        {
            hapticDemoRunning = true;
            DemoLog.Add("Przycisk Y: test haptyki (narastanie L, wysoka częstotliwość P, 3 impulsy)");

            // 1) Lewy: amplituda rośnie 0 -> 1, niska częstotliwość
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                OVRInput.SetControllerVibration(0.2f, t / 0.8f, OVRInput.Controller.LTouch);
                yield return null;
            }
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            yield return new WaitForSeconds(0.15f);

            // 2) Prawy: wysoka częstotliwość
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                OVRInput.SetControllerVibration(1f, 0.6f, OVRInput.Controller.RTouch);
                yield return null;
            }
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.15f);

            // 3) Oba: trzy krótkie impulsy
            for (int i = 0; i < 3; i++)
            {
                OVRInput.SetControllerVibration(0.5f, 1f, OVRInput.Controller.Touch);
                yield return new WaitForSeconds(0.1f);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.Touch);
                yield return new WaitForSeconds(0.15f);
            }
            hapticDemoRunning = false;
        }

        void Track(GameObject go)
        {
            spawned.Add(go);
            while (spawned.Count > maxSpawned)
            {
                if (spawned[0] != null) Destroy(spawned[0]);
                spawned.RemoveAt(0);
            }
        }

        Vector3 HeadPosition()
        {
            var rig = locomotion != null ? locomotion.rig : null;
            return rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.position : new Vector3(0f, 1.6f, 0f);
        }

        Vector3 HeadForward()
        {
            var rig = locomotion != null ? locomotion.rig : null;
            return rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.forward : Vector3.forward;
        }

        /// <summary>
        /// Wykrywa rig Interaction SDK dodany Quick Actionem lub Building Blockiem
        /// (szuka obiektu z "InteractionRig" / "Interactions Rig" w nazwie).
        /// </summary>
        void HandleIsdkRig()
        {
            bool isdkPresent = false;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                string n = t.name.Replace(" ", "");
                if (n.IndexOf("InteractionRig", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("InteractionsRig", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isdkPresent = true;
                    break;
                }
            }
            if (!isdkPresent) return;

            DemoLog.Add("Wykryto rig Interaction SDK - strefa ISDK aktywna.");

            if (hideOwnVisualsWhenIsdkRigPresent)
            {
                foreach (var root in ownVisualRoots)
                {
                    if (root == null) continue;
                    // Komponenty Meta, które same włączają renderery co klatkę - najpierw je wyłączamy.
                    foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
                    {
                        string type = b.GetType().Name;
                        if (type == "OVRMeshRenderer" || type == "OVRSkeletonRenderer" || type == "OVRControllerHelper")
                            b.enabled = false;
                    }
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    foreach (var c in root.GetComponentsInChildren<ControllerVisibility>(true)) c.enabled = false;
                    foreach (var v in root.GetComponentsInChildren<HandJointVisualizer>(true)) v.enabled = false;
                }
            }

            if (disableOwnLocomotionWhenIsdkRigPresent && locomotion != null)
                locomotion.enableThumbsticks = false;
        }
    }
}
