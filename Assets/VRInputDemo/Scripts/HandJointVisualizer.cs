using System.Collections.Generic;
using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Zapasowa wizualizacja dłoni: małe kulki w stawach z OVRSkeleton.
    /// Dodawana tylko wtedy, gdy obiekt dłoni nie ma siatki (SkinnedMeshRenderer).
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public float jointSize = 0.012f;
        public Color color = new Color(0.85f, 0.8f, 0.75f);

        class Joint
        {
            public Transform bone;
            public GameObject sphere;
        }

        readonly List<Joint> joints = new List<Joint>();
        int cachedVersion = -1;

        void Awake()
        {
            if (hand == null) hand = GetComponent<OVRHand>();
            if (skeleton == null) skeleton = GetComponent<OVRSkeleton>();
        }

        void LateUpdate()
        {
            bool ok = skeleton != null && skeleton.IsInitialized && skeleton.IsDataValid && (hand == null || hand.IsTracked);
            if (ok && cachedVersion != skeleton.SkeletonChangedCount) Rebuild();

            foreach (var j in joints)
            {
                if (j.sphere == null) continue;
                bool show = ok && j.bone != null;
                if (j.sphere.activeSelf != show) j.sphere.SetActive(show);
                if (show) j.sphere.transform.position = j.bone.position;
            }
        }

        void Rebuild()
        {
            foreach (var j in joints) if (j.sphere != null) Destroy(j.sphere);
            joints.Clear();
            cachedVersion = skeleton.SkeletonChangedCount;
            if (skeleton.Bones == null) return;

            foreach (var b in skeleton.Bones)
            {
                if (b == null || b.Transform == null) continue;
                var s = DemoUI.CreatePrimitive(PrimitiveType.Sphere, "Joint", transform, Vector3.zero, Vector3.one * jointSize, color, false);
                DemoUI.DisableShadows(s);
                joints.Add(new Joint { bone = b.Transform, sphere = s });
            }
        }
    }
}
