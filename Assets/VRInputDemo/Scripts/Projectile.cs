using UnityEngine;

namespace VRInputDemo
{
    /// <summary>Pocisk - znika po określonym czasie.</summary>
    public class Projectile : MonoBehaviour
    {
        public float lifetime = 6f;

        void Start() => Destroy(gameObject, lifetime);
    }
}
