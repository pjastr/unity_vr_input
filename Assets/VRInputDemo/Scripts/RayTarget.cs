using UnityEngine;

namespace VRInputDemo
{
    /// <summary>Cel dla promienia i pocisków: podświetla się, a po wybraniu/trafieniu zmienia kolor.</summary>
    [RequireComponent(typeof(Collider))]
    public class RayTarget : MonoBehaviour
    {
        [Tooltip("Obrót wokół osi Y (stopnie/s) - tylko dla ozdoby.")]
        public float spinSpeed;

        ColorTint tint;
        Vector3 baseScale;
        float punch;
        int hoverCount;

        void Awake()
        {
            tint = GetComponent<ColorTint>();
            baseScale = transform.localScale;
        }

        public void SetHover(bool hovered)
        {
            hoverCount = Mathf.Max(0, hoverCount + (hovered ? 1 : -1));
            if (tint != null) tint.SetHighlight(hoverCount > 0 ? 0.7f : 0f);
        }

        public void Select(XRInputSource source)
        {
            source.Haptic(0.6f, 0.1f);
            Hit($"{source.Label}: wybór promieniem -> {name}");
        }

        public void Hit(string message)
        {
            if (tint != null) tint.SetBaseColor(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f));
            punch = 1f;
            DemoLog.Add(message);
        }

        void Update()
        {
            if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            if (punch > 0f)
            {
                punch = Mathf.Max(0f, punch - Time.deltaTime * 3f);
                transform.localScale = baseScale * (1f + 0.25f * punch);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody != null && collision.rigidbody.GetComponent<Projectile>() != null)
                Hit($"Trafienie pociskiem -> {name}");
        }
    }
}
