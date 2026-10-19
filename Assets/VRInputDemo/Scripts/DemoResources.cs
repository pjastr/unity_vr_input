using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Trzyma referencję do materiału używanego przez obiekty tworzone w trakcie działania aplikacji
    /// (kule, pociski, wizualizacje). Dzięki referencji w scenie shader URP/Lit trafia do builda.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class DemoResources : MonoBehaviour
    {
        public Material litMaterial;

        void Awake()
        {
            if (litMaterial != null) DemoUI.LitMaterial = litMaterial;
        }
    }
}
