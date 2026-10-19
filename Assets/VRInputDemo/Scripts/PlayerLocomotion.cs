using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Poruszanie się (na kontrolerach): lewa gałka - płynny ruch w kierunku patrzenia,
    /// prawa gałka w lewo/prawo - obrót skokowy, klik prawej gałki - powrót na start.
    /// Metody SnapTurn/Step są też wywoływane przez mikrogesty dłoni (DemoActions).
    /// Przesuwa cały OVRCameraRig (głowa porusza się względem niego dzięki śledzeniu).
    /// </summary>
    public class PlayerLocomotion : MonoBehaviour
    {
        public OVRCameraRig rig;
        public float moveSpeed = 1.5f;
        public float snapAngle = 45f;
        [Range(0.1f, 1f)] public float stickDeadZone = 0.15f;
        public bool enableThumbsticks = true;

        Vector3 startPosition;
        Quaternion startRotation;
        bool turnArmed = true;
        bool moving;

        Transform Root => rig.transform;
        Transform Head => rig.centerEyeAnchor != null ? rig.centerEyeAnchor : rig.transform;

        void Awake()
        {
            if (rig == null) rig = GetComponent<OVRCameraRig>();
            if (rig == null) rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) { enabled = false; return; }
            startPosition = Root.position;
            startRotation = Root.rotation;
        }

        void Update()
        {
            if (!enableThumbsticks) return;

            // Lewa gałka: ruch
            Vector2 move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            bool nowMoving = move.magnitude > stickDeadZone;
            if (nowMoving)
            {
                Vector3 dir = Flat(Head.forward) * move.y + Flat(Head.right) * move.x;
                Root.position += dir * (moveSpeed * Time.deltaTime);
            }
            if (nowMoving && !moving) DemoLog.Add("Lewa gałka: chodzenie");
            moving = nowMoving;

            // Prawa gałka: obrót skokowy z "uzbrajaniem" (trzeba wrócić do środka przed kolejnym obrotem)
            float x = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
            if (turnArmed && Mathf.Abs(x) > 0.75f)
            {
                SnapTurn(Mathf.Sign(x) * snapAngle, x < 0 ? "Prawa gałka: obrót w lewo" : "Prawa gałka: obrót w prawo");
                turnArmed = false;
            }
            else if (Mathf.Abs(x) < 0.3f)
            {
                turnArmed = true;
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
                ResetPosition("Klik prawej gałki: powrót na start");
        }

        public void SnapTurn(float degrees, string reason)
        {
            if (rig == null) return;
            Root.RotateAround(Head.position, Vector3.up, degrees);
            if (!string.IsNullOrEmpty(reason)) DemoLog.Add(reason);
        }

        public void Step(float meters, string reason)
        {
            if (rig == null) return;
            Root.position += Flat(Head.forward) * meters;
            if (!string.IsNullOrEmpty(reason)) DemoLog.Add(reason);
        }

        public void ResetPosition(string reason)
        {
            if (rig == null) return;
            Root.SetPositionAndRotation(startPosition, startRotation);
            if (!string.IsNullOrEmpty(reason)) DemoLog.Add(reason);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }
    }
}
