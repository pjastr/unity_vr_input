using System.Collections.Generic;
using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Obiekt, który można chwycić skryptem SimpleGrabber (kontroler: grip, dłoń: pinch lub pięść)
    /// albo przyciągnąć promieniem (RayPointer). Zapamiętuje pozycję startową do resetu sceny.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        public static readonly List<Grabbable> All = new List<Grabbable>();

        [Tooltip("Obiekt utworzony w trakcie działania (np. kula) - przy resecie jest usuwany zamiast cofany.")]
        public bool spawned;

        public Rigidbody Body { get; private set; }
        public SimpleGrabber Holder { get; private set; }
        public bool IsHeld => Holder != null;
        public ColorTint Tint { get; private set; }

        Vector3 startPosition;
        Quaternion startRotation;
        int hoverCount;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Tint = GetComponent<ColorTint>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            if (Holder != null) Holder.ForceRelease();
        }

        public void SetHover(bool hovered)
        {
            hoverCount = Mathf.Max(0, hoverCount + (hovered ? 1 : -1));
            if (Tint != null) Tint.SetHighlight(hoverCount > 0 ? 1f : 0f);
        }

        internal void OnGrabbed(SimpleGrabber grabber)
        {
            if (Holder != null && Holder != grabber) Holder.ForceRelease(); // przejęcie drugą ręką
            Holder = grabber;
            Body.isKinematic = true;
        }

        internal void OnReleased(Vector3 velocity, Vector3 angularVelocity)
        {
            Holder = null;
            Body.isKinematic = false;
            Body.linearVelocity = velocity;
            Body.angularVelocity = angularVelocity;
        }

        public void ResetPose()
        {
            if (Holder != null) Holder.ForceRelease();
            Body.isKinematic = false;
            transform.SetPositionAndRotation(startPosition, startRotation);
            Body.position = startPosition;
            Body.rotation = startRotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }

        void Update()
        {
            // Obiekt spadł poza scenę.
            if (!IsHeld && transform.position.y < -5f)
            {
                if (spawned) Destroy(gameObject);
                else ResetPose();
            }
        }
    }
}
