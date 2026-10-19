using System.Collections.Generic;
using UnityEngine;

namespace VRInputDemo
{
    /// <summary>
    /// Prosty dziennik zdarzeń: ostatnie wpisy są pokazywane na panelu w VR (DemoLogPanel)
    /// i jednocześnie trafiają do logcat z prefiksem [VRInputDemo] (podgląd przez USB).
    /// </summary>
    public static class DemoLog
    {
        public const int MaxLines = 13;

        static readonly List<string> lines = new List<string>();

        /// <summary>Zwiększa się przy każdym nowym wpisie - panel odświeża tekst tylko gdy to potrzebne.</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<string> Lines => lines;

        public static void Add(string message)
        {
            lines.Add($"[{Time.time,6:0.0} s]  {message}");
            while (lines.Count > MaxLines) lines.RemoveAt(0);
            Version++;
            Debug.Log("[VRInputDemo] " + message);
        }

        // Czyści statyczny stan przy starcie (ważne, gdy w edytorze wyłączone jest "Domain Reload").
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lines.Clear();
            Version = 0;
        }
    }
}
