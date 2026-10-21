using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VRInputDemo.EditorTools
{
    /// <summary>
    /// Menu "VR Input Demo" w edytorze:
    ///  1. Konfiguruj projekt dla Meta Quest - ustawienia Androida/Quest, URP, hand tracking.
    ///  2. Zbuduj scenę demo - otoczenie z prymitywów, panele, logika demo i strefa ISDK.
    ///     Rig, dłonie, kontrolery i interakcje ISDK dodaje się z okna Building Blocks.
    ///  3. Sprawdź bloki w scenie - raport, których Building Blocks brakuje.
    ///  Ułóż bloki ISDK na stanowiskach - przenosi obiekty utworzone przez bloki ISDK na stół strefy ISDK.
    ///  4. Build And Run (USB) - buduje APK (Development + Script Debugging) i uruchamia na goglach.
    /// </summary>
    public static class VRInputDemoSetup
    {
        public const string Root = "Assets/VRInputDemo";
        public const string ScenePath = Root + "/Scenes/InputDemo.unity";
        const string SettingsFolder = Root + "/Settings";
        const string MaterialsFolder = Root + "/Materials";
        const string ApkPath = "Builds/VRInputDemo.apk";
        const string IsdkZoneName = "ISDK_Zone";

        /// <summary>Obiekty główne tworzone przez skrypt (przy odświeżaniu sceny są usuwane i tworzone od nowa).</summary>
        static readonly string[] DemoRootNames = { "Directional Light", "Environment", "Interactables", "Panels", "InputSources", "Demo" };

        static readonly Vector3 HeadPos = new Vector3(0f, 1.6f, 0f);
        static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.9f);

        // =====================================================================
        // 1. KONFIGURACJA PROJEKTU
        // =====================================================================

        [MenuItem("VR Input Demo/1. Konfiguruj projekt dla Meta Quest", false, 1)]
        public static void ConfigureProject()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.companyName = "VRInputDemo";
            PlayerSettings.productName = "VR Input Demo";
            PlayerSettings.SetApplicationIdentifier(android, "com.vrinputdemo.quest");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32; // Android 12L - minimum dla Horizon OS
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // Build do debugowania przez USB.
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = true;
            EditorUserBuildSettings.buildAppBundle = false;

            EnsureUrp();
            SetHandTrackingSupport();
            if (File.Exists(ScenePath)) AddSceneToBuild();
            AssetDatabase.SaveAssets();

            const string next =
                "Dalej:\n" +
                "• Project Settings > XR Plug-in Management > zakładka Android: zaznacz OpenXR oraz grupę \"Meta XR\".\n" +
                "• Meta > Tools > Project Setup Tool: \"Fix All\" (Android).\n" +
                "• VR Input Demo > 2. Zbuduj scenę demo.";

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (EditorUtility.DisplayDialog("VR Input Demo",
                        "Ustawienia Quest zapisane (IL2CPP, ARM64, min. API 32, Vulkan, Linear, URP, dłonie + kontrolery).\n\n" +
                        "Przełączyć platformę na Android teraz? Może to potrwać kilka minut.\n\n" + next,
                        "Przełącz na Android", "Później"))
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("VR Input Demo", "Ustawienia Quest zapisane.\n\n" + next, "OK");
            }
        }

        // =====================================================================
        // 2. BUDOWA / ODŚWIEŻENIE SCENY
        // =====================================================================

        [MenuItem("VR Input Demo/2. Zbuduj scenę demo", false, 2)]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool exists = File.Exists(ScenePath);
            if (exists && !EditorUtility.DisplayDialog("VR Input Demo",
                    "Scena już istnieje. Odświeżyć zawartość demo?\n\n" +
                    "• Building Blocks (Camera Rig, Hand Tracking, Controller Tracking, Interactions Rig) zostaną zachowane.\n" +
                    "• Strefa ISDK (" + IsdkZoneName + ") z dodanymi blokami też zostanie zachowana.",
                    "Odśwież", "Anuluj"))
                return;

            EnsureUrp();
            Material lit = EnsureLitMaterial();
            DemoUI.LitMaterial = lit;

            try
            {
                UnityEngine.SceneManagement.Scene scene;
                if (exists)
                {
                    scene = EditorSceneManager.OpenScene(ScenePath);
                    foreach (var go in scene.GetRootGameObjects())
                        if (DemoRootNames.Contains(go.name)) Object.DestroyImmediate(go);
                }
                else
                {
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }

                BuildLighting();
                BuildEnvironment(new GameObject("Environment").transform);

                var interactables = new GameObject("Interactables").transform;
                BuildGrabbables(interactables);
                BuildPokeButtons(interactables);
                BuildTargets(interactables);

                // Źródła wejścia: OVRCameraRig i OVRHand znajdą same w trakcie działania (bloki).
                var inputs = new GameObject("InputSources").transform;
                XRInputSource leftSrc = CreateInputSource(inputs, HandSide.Left);
                XRInputSource rightSrc = CreateInputSource(inputs, HandSide.Right);

                var panels = new GameObject("Panels");
                BuildPanels(panels.transform, leftSrc, rightSrc);

                var oldZone = GameObject.Find(IsdkZoneName);
                if (oldZone != null && oldZone.transform.Find("ISDK_4_UI_Canvas") != null &&
                    EditorUtility.DisplayDialog("VR Input Demo",
                        "Strefa ISDK ma stary układ (prymitywy i panel UI). Przebudować ją na stanowiska dla bloków?\n\n" +
                        "Bloki ISDK dodane do starej strefy zostaną usunięte – dodasz je ponownie (punkt 8 instrukcji).",
                        "Przebuduj strefę", "Zostaw"))
                {
                    Object.DestroyImmediate(oldZone);
                }
                if (GameObject.Find(IsdkZoneName) == null) BuildIsdkZone();

                var demo = new GameObject("Demo");
                demo.AddComponent<DemoResources>().litMaterial = lit;
                var locomotion = demo.AddComponent<PlayerLocomotion>();
                var actions = demo.AddComponent<DemoActions>();
                actions.left = leftSrc;
                actions.right = rightSrc;
                actions.locomotion = locomotion;
                actions.panelsRoot = panels;

                SetHandTrackingSupport();

                EnsureFolder(Root + "/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuild();
            }
            finally
            {
                DemoUI.LitMaterial = null;
            }

            string report = CheckScene(true, out bool ready);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Input Demo",
                "Scena gotowa: " + ScenePath + "\n\n" + report +
                (ready ? "" : "\n\nDodaj brakujące bloki: Meta > Tools > Building Blocks (szczegóły w INSTRUKCJA.md, punkt 3)."),
                "OK");
        }

        // =====================================================================
        // 3. SPRAWDZENIE BLOKÓW
        // =====================================================================

        [MenuItem("VR Input Demo/3. Sprawdź bloki w scenie", false, 3)]
        public static void CheckSceneMenu()
        {
            string report = CheckScene(true, out bool _);
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Input Demo – Building Blocks", report, "OK");
        }

        /// <summary>
        /// Sprawdza otwartą scenę: czy są wymagane Building Blocks i czy nie ma duplikatów.
        /// Przy fix = true ustawia też Tracking Origin = Floor Level i obsługę dłoni.
        /// </summary>
        static string CheckScene(bool fix, out bool ready)
        {
            var sb = new StringBuilder();

            var rigs = Object.FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool rigOk = rigs.Length == 1;
            Line(sb, rigOk, "Camera Rig",
                rigs.Length == 0 ? "brak – dodaj blok Camera Rig" :
                rigs.Length > 1 ? $"{rigs.Length} rigi – zostaw jeden" : "OK");

            int left = 0, right = 0;
            foreach (var h in Resources.FindObjectsOfTypeAll<OVRHand>())
            {
                if (h == null || !h.gameObject.scene.IsValid()) continue;
                if (h.GetHand() == OVRPlugin.Hand.HandLeft) left++;
                else if (h.GetHand() == OVRPlugin.Hand.HandRight) right++;
            }
            bool handsOk = left >= 1 && right >= 1;
            Line(sb, handsOk, "Hand Tracking", handsOk ? "OK (lewa i prawa)" : $"lewa: {left}, prawa: {right} – dodaj blok Hand Tracking");

            Type helperType = FindType("OVRControllerHelper");
            int controllers = helperType != null
                ? Object.FindObjectsByType(helperType, FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                : 0;
            Line(sb, controllers > 0, "Controller Tracking", controllers > 0 ? "OK" : "brak modeli kontrolerów – dodaj blok Controller Tracking (zalecane)");

            bool isdkRig = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(t => IsIsdkRigName(t.name));
            Line(sb, isdkRig, "Interactions Rig", isdkRig ? "OK" : "brak – potrzebny tylko dla strefy ISDK (punkt 8 instrukcji)");
            if (isdkRig)
            {
                var blockKeys = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(t => t.name.StartsWith("[BuildingBlock]") && !IsNonIsdkBlock(t))
                    .Select(t => MatchStation(BlockKey(t))).Where(st => st != null).Distinct().ToList();
                int found = blockKeys.Count;
                sb.Append("         bloki ISDK: ").Append(found).Append('/').Append(IsdkStations.Length);
                if (found < IsdkStations.Length)
                    sb.Append(" (brak: ").Append(string.Join(", ", IsdkStations.Where(st => !blockKeys.Contains(st)).Select(st => st.block))).Append(')');
                sb.AppendLine();
            }

            // Zbędna kamera spoza rigu (np. domyślna Main Camera).
            var strayCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => rigs.All(r => !c.transform.IsChildOf(r.transform))).ToList();
            if (strayCameras.Count > 0)
                Line(sb, false, "Main Camera", "w scenie jest kamera poza rigiem (" + strayCameras[0].name + ") – usuń ją");

            var manager = Object.FindAnyObjectByType<OVRManager>();
            if (manager != null && fix)
            {
                SetEnum(manager, "FloorLevel", false, "_trackingOriginType", "m_TrackingOriginType", "trackingOriginType");
                SetHandTrackingSupport();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            if (manager == null && rigs.Length > 0)
                Line(sb, false, "OVRManager", "brak – dodaj blok Camera Rig ponownie");

            ready = rigOk && handsOk && strayCameras.Count == 0;
            sb.Append(ready ? "\nScena jest gotowa do uruchomienia." : "\nScena wymaga uzupełnienia.");
            return sb.ToString();
        }

        static void Line(StringBuilder sb, bool ok, string what, string info) =>
            sb.Append(ok ? "[OK]    " : "[BRAK] ").Append(what).Append(" – ").AppendLine(info);

        static bool IsIsdkRigName(string name)
        {
            string n = name.Replace(" ", "");
            return n.IndexOf("InteractionRig", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("InteractionsRig", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // =====================================================================
        // 4. BUILD AND RUN
        // =====================================================================

        [MenuItem("VR Input Demo/4. Build And Run (USB)", false, 4)]
        public static void BuildAndRun()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("VR Input Demo", "Najpierw zbuduj scenę (krok 2).", "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("VR Input Demo",
                    "Aktywna platforma to nie Android. Uruchom krok 1 i przełącz platformę (albo File > Build Profiles).", "OK");
                return;
            }
            if (EditorSceneManager.GetActiveScene().path == ScenePath)
            {
                string report = CheckScene(false, out bool ready);
                if (!ready && !EditorUtility.DisplayDialog("VR Input Demo",
                        report + "\n\nZbudować mimo to?", "Buduj", "Anuluj"))
                    return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath) ?? "Builds");
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.AutoRunPlayer | BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport buildReport = BuildPipeline.BuildPlayer(options);
            if (buildReport.summary.result == BuildResult.Succeeded)
                Debug.Log($"[VRInputDemo] Build OK ({buildReport.summary.totalSize / (1024f * 1024f):0.0} MB) -> {ApkPath}. Aplikacja uruchamia się na goglach.");
            else
                Debug.LogError($"[VRInputDemo] Build nieudany: {buildReport.summary.result}. Sprawdź Console (błędy) i czy gogle są widoczne w 'adb devices'.");
        }

        [MenuItem("VR Input Demo/Otwórz scenę demo", false, 20)]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("VR Input Demo", "Scena jeszcze nie istnieje (krok 2).", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("VR Input Demo/Otwórz instrukcję (INSTRUKCJA.md)", false, 21)]
        public static void OpenDocs()
        {
            string path = Path.GetFullPath("INSTRUKCJA.md");
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
            else Debug.LogWarning("Nie znaleziono " + path);
        }

        // =====================================================================
        // URP + MATERIAŁ
        // =====================================================================

        static UniversalRenderPipelineAsset EnsureUrp()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                EnsureFolder(SettingsFolder);
                string assetPath = SettingsFolder + "/VRInputDemo_URP.asset";
                urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
                if (urp == null)
                {
                    var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, SettingsFolder + "/VRInputDemo_URP_Renderer.asset");
                    urp = UniversalRenderPipelineAsset.Create(rendererData);
                    AssetDatabase.CreateAsset(urp, assetPath);
                }
                GraphicsSettings.defaultRenderPipeline = urp;
            }

            // Ustawienia przyjazne dla Quest (mobilne GPU, rendering stereo).
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.renderScale = 1f;
            urp.shadowDistance = 15f;
            EditorUtility.SetDirty(urp);

            // Poziomy jakości bez własnego potoku -> używają domyślnego (naszego URP).
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != urp)
                    QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            return urp;
        }

        static Material EnsureLitMaterial()
        {
            EnsureFolder(MaterialsFolder);
            string path = MaterialsFolder + "/DemoLit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader) { name = "DemoLit" };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // =====================================================================
        // ŹRÓDŁA WEJŚCIA
        // =====================================================================

        static XRInputSource CreateInputSource(Transform parent, HandSide side)
        {
            var go = new GameObject(side == HandSide.Left ? "LeftInput" : "RightInput");
            go.transform.SetParent(parent, false);

            var detector = go.AddComponent<HandGestureDetector>();
            detector.side = side;

            var src = go.AddComponent<XRInputSource>();
            src.side = side;
            src.gestures = detector;

            var grabber = go.AddComponent<SimpleGrabber>();
            grabber.source = src;

            var ray = go.AddComponent<RayPointer>();
            ray.source = src;
            ray.grabber = grabber;
            return src;
        }

        /// <summary>Hand Tracking Support = Controllers And Hands (OVRProjectConfig, przez refleksję - odporne na zmiany API).</summary>
        static void SetHandTrackingSupport()
        {
            try
            {
                Type t = FindType("OVRProjectConfig");
                if (t == null) { Debug.LogWarning("[VRInputDemo] Brak OVRProjectConfig - ustaw Hand Tracking Support ręcznie w OVRManager."); return; }

                object config = null;
                var getter = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetProjectConfig");
                if (getter != null) config = getter.Invoke(null, DefaultArgs(getter));
                if (config == null)
                {
                    var prop = t.GetProperty("CachedProjectConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (prop != null) config = prop.GetValue(null);
                }
                if (config == null) { Debug.LogWarning("[VRInputDemo] Nie udało się pobrać OVRProjectConfig."); return; }

                var field = t.GetField("handTrackingSupport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || !field.FieldType.IsEnum || !Enum.GetNames(field.FieldType).Contains("ControllersAndHands"))
                {
                    Debug.LogWarning("[VRInputDemo] Nie znaleziono pola handTrackingSupport - ustaw ręcznie: OVRManager > Quest Features > Hand Tracking Support.");
                    return;
                }
                field.SetValue(config, Enum.Parse(field.FieldType, "ControllersAndHands"));

                var commit = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "CommitProjectConfig" && m.GetParameters().Length == 1);
                if (commit != null) commit.Invoke(null, new[] { config });
                else if (config is Object unityObj) { EditorUtility.SetDirty(unityObj); AssetDatabase.SaveAssets(); }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VRInputDemo] Ustawienie Hand Tracking Support nie powiodło się: " + e.Message);
            }
        }

        // =====================================================================
        // OTOCZENIE I OBIEKTY
        // =====================================================================

        static void BuildLighting()
        {
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.4f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.22f);
        }

        static void BuildEnvironment(Transform env)
        {
            DemoUI.CreatePrimitive(PrimitiveType.Plane, "Floor", env, Vector3.zero, new Vector3(3f, 1f, 3f), new Color(0.24f, 0.26f, 0.3f));

            var grid = new GameObject("Grid").transform;
            grid.SetParent(env, false);
            Color line = new Color(0.36f, 0.39f, 0.45f);
            for (int i = -6; i <= 6; i++)
            {
                DemoUI.CreatePrimitive(PrimitiveType.Cube, "LineX", grid, new Vector3(i, 0.002f, 0f), new Vector3(0.01f, 0.002f, 12f), line, false);
                DemoUI.CreatePrimitive(PrimitiveType.Cube, "LineZ", grid, new Vector3(0f, 0.002f, i), new Vector3(12f, 0.002f, 0.01f), line, false);
            }
            DemoUI.DisableShadows(grid.gameObject);

            var start = DemoUI.CreatePrimitive(PrimitiveType.Cylinder, "StartMarker", env, new Vector3(0f, 0.004f, 0f),
                new Vector3(0.8f, 0.002f, 0.8f), new Color(0.15f, 0.55f, 0.55f), false);
            DemoUI.DisableShadows(start);

            CreateTable(env, "Table", new Vector3(0f, 0f, 0.6f), new Vector2(1.5f, 0.6f));
        }

        static void CreateTable(Transform parent, string name, Vector3 floorCenter, Vector2 size)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = floorCenter;
            DemoUI.CreatePrimitive(PrimitiveType.Cube, "Top", t, new Vector3(0f, 0.775f, 0f), new Vector3(size.x, 0.05f, size.y), new Color(0.55f, 0.4f, 0.27f));
            Color leg = new Color(0.32f, 0.23f, 0.16f);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                DemoUI.CreatePrimitive(PrimitiveType.Cube, "Leg", t,
                    new Vector3(sx * (size.x * 0.5f - 0.05f), 0.375f, sz * (size.y * 0.5f - 0.05f)), new Vector3(0.05f, 0.75f, 0.05f), leg);
            }
        }

        static void BuildGrabbables(Transform parent)
        {
            var root = new GameObject("Grabbables").transform;
            root.SetParent(parent, false);
            CreateGrabbable(root, "Czerwona kostka", PrimitiveType.Cube, new Vector3(-0.6f, 0.845f, 0.5f), Vector3.one * 0.08f, new Color(0.9f, 0.25f, 0.25f));
            CreateGrabbable(root, "Niebieska kula", PrimitiveType.Sphere, new Vector3(-0.45f, 0.85f, 0.5f), Vector3.one * 0.09f, new Color(0.25f, 0.5f, 0.95f));
            CreateGrabbable(root, "Zielona kapsuła", PrimitiveType.Capsule, new Vector3(-0.3f, 0.865f, 0.5f), new Vector3(0.05f, 0.06f, 0.05f), new Color(0.3f, 0.8f, 0.35f));
            CreateGrabbable(root, "Żółty walec", PrimitiveType.Cylinder, new Vector3(-0.15f, 0.855f, 0.5f), new Vector3(0.07f, 0.05f, 0.07f), new Color(0.95f, 0.85f, 0.25f));
            CreateGrabbable(root, "Fioletowa kostka", PrimitiveType.Cube, new Vector3(-0.5f, 0.865f, 0.72f), Vector3.one * 0.12f, new Color(0.6f, 0.35f, 0.85f));
            CreateGrabbable(root, "Pomarańczowa kula", PrimitiveType.Sphere, new Vector3(-0.25f, 0.835f, 0.72f), Vector3.one * 0.06f, new Color(1f, 0.6f, 0.2f));

            Label(parent, "Chwytaj: grip / pinch / pięść  •  rzucaj  •  przyciągaj promieniem", new Vector3(-0.38f, 1.02f, 0.92f), new Vector2(720f, 50f), 24);
        }

        static void CreateGrabbable(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = DemoUI.CreatePrimitive(type, name, parent, pos, scale, color);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.AddComponent<Grabbable>();
        }

        static void BuildPokeButtons(Transform parent)
        {
            var root = new GameObject("PokeButtons").transform;
            root.SetParent(parent, false);
            CreatePokeButton(root, new Vector3(0.1f, 0.82f, 0.5f), PokeAction.SpawnBall, "Nowa kula", new Color(0.3f, 0.75f, 0.95f));
            CreatePokeButton(root, new Vector3(0.27f, 0.82f, 0.5f), PokeAction.RandomizeColors, "Kolory", new Color(0.95f, 0.5f, 0.8f));
            CreatePokeButton(root, new Vector3(0.44f, 0.82f, 0.5f), PokeAction.ToggleGravity, "Grawitacja", new Color(0.55f, 0.9f, 0.4f));
            CreatePokeButton(root, new Vector3(0.61f, 0.82f, 0.5f), PokeAction.ResetScene, "Reset", new Color(0.95f, 0.35f, 0.3f));

            Label(parent, "Wciskaj palcem / końcówką kontrolera albo wskaż promieniem", new Vector3(0.36f, 1.02f, 0.92f), new Vector2(640f, 50f), 24);
        }

        static void CreatePokeButton(Transform parent, Vector3 topCenter, PokeAction action, string label, Color color)
        {
            var root = new GameObject("PokeButton_" + action).transform;
            root.SetParent(parent, false);
            root.position = topCenter;

            DemoUI.CreatePrimitive(PrimitiveType.Cube, "Base", root, new Vector3(0f, -0.01f, 0f), new Vector3(0.13f, 0.02f, 0.13f), new Color(0.18f, 0.18f, 0.2f));
            var cap = DemoUI.CreatePrimitive(PrimitiveType.Cube, "Cap", root, new Vector3(0f, 0.01f, 0f), new Vector3(0.09f, 0.02f, 0.09f), color);

            var button = root.gameObject.AddComponent<PokeButton>();
            button.action = action;
            button.label = label;
            button.cap = cap.transform;
            button.size = new Vector2(0.09f, 0.09f);
            button.capHeight = 0.02f;
            button.travel = 0.014f;

            Label(root, label, topCenter + new Vector3(0f, 0.07f, 0.1f), new Vector2(160f, 46f), 24);
        }

        static void BuildTargets(Transform parent)
        {
            var root = new GameObject("RayTargets").transform;
            root.SetParent(parent, false);
            CreateTarget(root, "Cel lewy", PrimitiveType.Sphere, new Vector3(-2.6f, 1.6f, 3.6f), 0.6f, new Color(0.9f, 0.4f, 0.3f), 0f);
            CreateTarget(root, "Cel prawy", PrimitiveType.Sphere, new Vector3(2.6f, 1.6f, 3.6f), 0.6f, new Color(0.3f, 0.6f, 0.95f), 0f);
            CreateTarget(root, "Cel górny", PrimitiveType.Cube, new Vector3(0f, 3.1f, 4.2f), 0.5f, new Color(0.95f, 0.8f, 0.3f), 25f);
        }

        static void CreateTarget(Transform parent, string name, PrimitiveType type, Vector3 pos, float size, Color color, float spin)
        {
            var go = DemoUI.CreatePrimitive(type, name, parent, pos, Vector3.one * size, color);
            go.AddComponent<RayTarget>().spinSpeed = spin;
        }

        static void Label(Transform parent, string text, Vector3 position, Vector2 sizePx, int fontSize)
        {
            var panel = DemoUI.CreatePanel("Label", parent, position, DemoUI.FacePoint(position, HeadPos, false), sizePx,
                new Color(0.05f, 0.05f, 0.08f, 0.75f));
            DemoUI.CreateFillText(panel, "Text", 4f, fontSize, TextAnchor.MiddleCenter, Color.white, text);
        }

        // =====================================================================
        // PANELE
        // =====================================================================

        static void BuildPanels(Transform root, XRInputSource left, XRInputSource right)
        {
            // --- Kontrolery (z lewej) ---
            Vector3 cPos = new Vector3(-1.45f, 1.45f, 1.25f);
            var cp = DemoUI.CreatePanel("ControllerPanel", root, cPos, DemoUI.FacePoint(cPos, HeadPos), new Vector2(1000f, 920f), PanelBg);
            var cip = cp.gameObject.AddComponent<ControllerInputPanel>();
            cip.header = DemoUI.CreateText(cp, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -110f), new Vector2(-30f, -15f), 32, TextAnchor.UpperLeft, Color.white, "KONTROLERY");
            cip.leftText = DemoUI.CreateText(cp, "Left", new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(30f, -640f), new Vector2(-10f, -125f), 27, TextAnchor.UpperLeft, Color.white, "");
            cip.rightText = DemoUI.CreateText(cp, "Right", new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(10f, -640f), new Vector2(-30f, -125f), 27, TextAnchor.UpperLeft, Color.white, "");
            DemoUI.CreateText(cp, "Footer", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 44f), 22, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                "trigger | grip | gałka | przyciski    szary = puszczony, żółty = dotyk, zielony = wciśnięty");
            cip.gaugeY = -300f;

            // --- Dłonie (z prawej) ---
            Vector3 hPos = new Vector3(1.45f, 1.45f, 1.25f);
            var hp = DemoUI.CreatePanel("HandPanel", root, hPos, DemoUI.FacePoint(hPos, HeadPos), new Vector2(1000f, 920f), PanelBg);
            var hip = hp.gameObject.AddComponent<HandInfoPanel>();
            hip.left = left;
            hip.right = right;
            hip.header = DemoUI.CreateText(hp, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -110f), new Vector2(-30f, -15f), 32, TextAnchor.UpperLeft, Color.white, "DŁONIE");
            hip.leftText = DemoUI.CreateText(hp, "Left", new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(30f, 20f), new Vector2(-10f, -125f), 27, TextAnchor.UpperLeft, Color.white, "");
            hip.rightText = DemoUI.CreateText(hp, "Right", new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(10f, 20f), new Vector2(-30f, -125f), 27, TextAnchor.UpperLeft, Color.white, "");

            // --- Pomoc (na wprost) ---
            Vector3 helpPos = new Vector3(0f, 1.75f, 2.6f);
            var help = DemoUI.CreatePanel("HelpPanel", root, helpPos, DemoUI.FacePoint(helpPos, HeadPos), new Vector2(1500f, 900f), PanelBg);
            DemoUI.CreateFillText(help, "Text", 30f, 28, TextAnchor.UpperLeft, Color.white, HelpText);

            // --- Dziennik zdarzeń (pod pomocą) ---
            Vector3 logPos = new Vector3(0f, 0.8f, 2.6f);
            var log = DemoUI.CreatePanel("LogPanel", root, logPos, DemoUI.FacePoint(logPos, HeadPos), new Vector2(1500f, 600f), PanelBg);
            DemoUI.CreateText(log, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -60f), new Vector2(-30f, -12f), 30, TextAnchor.UpperLeft, new Color(0.6f, 0.85f, 1f),
                "<b>DZIENNIK ZDARZEŃ</b>  (także w logcat z prefiksem [VRInputDemo])");
            var logText = DemoUI.CreateText(log, "Lines", Vector2.zero, Vector2.one, new Vector2(30f, 15f), new Vector2(-30f, -65f), 26, TextAnchor.UpperLeft, Color.white, "");
            log.gameObject.AddComponent<DemoLogPanel>().text = logText;
        }

        const string HelpText =
            "<size=40><b>VR INPUT DEMO</b></size>   <color=#9AB>Meta Quest 3S  •  OVRInput / OVRHand / Interaction SDK</color>\n\n" +
            "<b><color=#FFD27F>KONTROLERY</color></b>\n" +
            "Trigger – wybór promieniem (cele, przyciski, przyciąganie obiektów)\n" +
            "Grip – chwyć obiekt w zasięgu ręki; puść w ruchu = rzut\n" +
            "A – strzał pociskiem     B – losowe kolory obiektów\n" +
            "X – nowa kula przy lewej ręce     Y – test wibracji (haptyka)\n" +
            "Menu – reset sceny     klik lewej gałki – pokaż/ukryj panele\n" +
            "Lewa gałka – chodzenie     prawa gałka – obrót o 45°     klik prawej – powrót na start\n\n" +
            "<b><color=#9FE8A0>DŁONIE</color></b>  (odłóż kontrolery i pokaż dłonie)\n" +
            "Pinch (kciuk + wskazujący) – chwyt z bliska albo wybór promieniem z dłoni\n" +
            "Pięść – chwyt obiektu     wyprostowany palec wskazujący – wciskanie przycisków\n" +
            "Znak V – nowa kula nad dłonią\n" +
            "Kciuk w górę (1 s) – reset sceny     kciuk w dół (1 s) – pokaż/ukryj panele\n" +
            "Mikrogesty kciukiem po palcu wskazującym: lewo/prawo – obrót,\n" +
            "    przód/tył – krok, stuknięcie – strzał\n\n" +
            "<color=#C8A0FF>Za Tobą: strefa Interaction SDK (obiekty z Building Blocks ISDK).</color>";

        // =====================================================================
        // STREFA INTERACTION SDK – stanowiska dla samodzielnych obiektów z Building Blocks
        // =====================================================================

        /// <summary>Stanowisko w strefie ISDK: gdzie położyć obiekt z danego bloku.</summary>
        class IsdkStation
        {
            public string name;        // nazwa obiektu stanowiska w scenie
            public string block;       // nazwa bloku w oknie Building Blocks
            public string hint;        // co sprawdzić w goglach
            public string[] keywords;  // fragment nazwy obiektu bloku lub jego komponentów (małe litery, bez spacji)
            public Vector3 position;   // pozycja stanowiska (świat)
            public Vector3 blockOffset;// gdzie położyć obiekt bloku względem stanowiska
            public float yaw;          // obrót obiektu bloku wokół Y
            public float scale;        // 0 = zostaw skalę nadaną przez blok
        }

        // Gracz po odwróceniu patrzy w stronę -Z, więc obiekty płaskie (przycisk, cel) obracamy o 180°.
        static readonly IsdkStation[] IsdkStations =
        {
            new IsdkStation { name = "Stanowisko_1_Grab", block = "Grab Interaction", hint = "chwyć i rzuć",
                keywords = new[] { "grab" }, position = new Vector3(0.45f, 0.8f, -0.95f), blockOffset = new Vector3(0f, 0.08f, 0f) },
            new IsdkStation { name = "Stanowisko_2_TouchHandGrab", block = "Touch Hand Grab", hint = "chwyć dłonią – palce obejmują obiekt",
                keywords = new[] { "touch" }, position = new Vector3(0.1f, 0.8f, -0.95f), blockOffset = new Vector3(0f, 0.08f, 0f) },
            new IsdkStation { name = "Stanowisko_3_DistanceGrab", block = "Distance Grab", hint = "chwyć z daleka",
                keywords = new[] { "distance" }, position = new Vector3(-0.4f, 0.8f, -1.15f), blockOffset = new Vector3(0f, 0.08f, 0f) },
            new IsdkStation { name = "Stanowisko_4_Poke", block = "Poke Interaction", hint = "wciśnij przycisk palcem / kontrolerem",
                keywords = new[] { "poke" }, position = new Vector3(-0.45f, 0.8f, -0.8f), blockOffset = new Vector3(0f, 0.2f, 0f), yaw = 180f },
            new IsdkStation { name = "Stanowisko_5_Ray", block = "Ray Interaction", hint = "wskaż promieniem i wybierz",
                keywords = new[] { "rayinteract" }, position = new Vector3(0f, 1.5f, -2.3f), blockOffset = Vector3.zero, yaw = 180f, scale = 0.5f },
            new IsdkStation { name = "Stanowisko_6_Teleport", block = "Teleport", hint = "wskaż miejsce i przenieś się",
                keywords = new[] { "teleport" }, position = new Vector3(0f, 0f, -3.3f), blockOffset = Vector3.zero },
        };

        // Bloki, których nie ruszamy (rig, dłonie, kontrolery itp.).
        static readonly string[] NonIsdkBlockKeywords =
            { "camerarig", "handtracking", "controllertracking", "interactionrig", "interactionsrig", "passthrough" };

        static void BuildIsdkZone()
        {
            var zone = new GameObject(IsdkZoneName);
            zone.AddComponent<IsdkZone>();
            var z = zone.transform;

            CreateTable(z, "ISDK_Table", new Vector3(0f, 0f, -1.0f), new Vector2(1.4f, 0.6f));

            for (int i = 0; i < IsdkStations.Length; i++)
            {
                var st = IsdkStations[i];
                var stationGo = new GameObject(st.name);
                stationGo.transform.SetParent(z, false);
                stationGo.transform.position = st.position;

                // Płaski znacznik miejsca (bez kolidera) i podpis.
                bool onFloor = st.position.y < 0.1f;
                bool floating = st.position.y > 1f;
                if (!floating)
                {
                    var marker = DemoUI.CreatePrimitive(PrimitiveType.Cylinder, "Marker", stationGo.transform, new Vector3(0f, 0.003f, 0f),
                        onFloor ? new Vector3(2f, 0.002f, 2f) : new Vector3(0.14f, 0.002f, 0.14f), new Color(0.45f, 0.3f, 0.75f), false);
                    DemoUI.DisableShadows(marker);
                }
                Vector3 labelPos = st.position + (onFloor ? new Vector3(0f, 0.3f, 1.1f) : floating ? new Vector3(0f, -0.35f, 0f) : new Vector3(0f, 0.32f, 0f));
                if (st.name.Contains("Poke")) labelPos = st.position + new Vector3(0f, 0.38f, 0f);
                Label(stationGo.transform, $"{i + 1}  {st.block}", labelPos, new Vector2(300f, 50f), 26);
            }

            // Tablica w VR z opisem stanowisk.
            Vector3 signPos = new Vector3(1.4f, 1.45f, -1.3f);
            var sign = DemoUI.CreatePanel("ISDK_Info", z, signPos, DemoUI.FacePoint(signPos, HeadPos), new Vector2(820f, 560f), PanelBg);
            var sb = new StringBuilder("<b><color=#C8A0FF>INTERACTION SDK</color></b>\nObiekty z Building Blocks – obsługuje je ISDK,\nnie skrypty demo.\n\n");
            for (int i = 0; i < IsdkStations.Length; i++)
                sb.Append(i + 1).Append("  <b>").Append(IsdkStations[i].block).Append("</b> – ").Append(IsdkStations[i].hint).Append('\n');
            DemoUI.CreateFillText(sign, "Text", 25f, 27, TextAnchor.UpperLeft, Color.white, sb.ToString());
        }

        [MenuItem("VR Input Demo/Ułóż bloki ISDK na stanowiskach", false, 5)]
        public static void ArrangeIsdkBlocks()
        {
            var zoneGo = GameObject.Find(IsdkZoneName);
            if (zoneGo == null)
            {
                EditorUtility.DisplayDialog("VR Input Demo", "W scenie nie ma strefy " + IsdkZoneName + ". Uruchom krok 2 (Zbuduj scenę demo).", "OK");
                return;
            }
            Transform zone = zoneGo.transform;

            var stations = new Dictionary<IsdkStation, Transform>();
            foreach (var st in IsdkStations)
            {
                var t = zone.Find(st.name);
                if (t != null) stations[st] = t;
            }

            // 1) Znajdź obiekty "[BuildingBlock] ..." należące do bloków ISDK.
            var moved = new Dictionary<IsdkStation, List<Transform>>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("[BuildingBlock]")) continue;
                if (IsNonIsdkBlock(t)) continue;
                string key = BlockKey(t);

                IsdkStation station = MatchStation(key);
                if (station == null || !stations.ContainsKey(station)) continue;

                Transform movable = MovableRoot(t, zone, stations.Values);
                if (movable == null) continue;

                if (!moved.TryGetValue(station, out var list)) moved[station] = list = new List<Transform>();
                if (!list.Contains(movable)) list.Add(movable);
            }

            // 2) Przenieś na stanowiska.
            foreach (var pair in moved)
            {
                var st = pair.Key;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    Transform obj = pair.Value[i];
                    Undo.SetTransformParent(obj, stations[st], "Ułóż bloki ISDK");
                    Undo.RecordObject(obj, "Ułóż bloki ISDK");
                    obj.localPosition = st.blockOffset + new Vector3(0.15f * i, 0f, 0f);
                    obj.localRotation = Quaternion.Euler(0f, st.yaw, 0f);
                    if (st.scale > 0f) obj.localScale = Vector3.one * st.scale;
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var report = new StringBuilder("Bloki ISDK na stanowiskach:\n\n");
            for (int i = 0; i < IsdkStations.Length; i++)
            {
                var st = IsdkStations[i];
                int n = moved.TryGetValue(st, out var l) ? l.Count : 0;
                report.Append(n > 0 ? "[OK]   " : "[--]   ").Append(i + 1).Append(' ').Append(st.block)
                      .AppendLine(n > 0 ? "" : " – nie dodano bloku");
            }
            report.Append("\nZapisz scenę (Ctrl+S). Jeśli przycisk lub cel jest odwrócony tyłem, obróć go o 180° w osi Y.");
            EditorUtility.DisplayDialog("VR Input Demo", report.ToString(), "OK");
        }

        static bool IsNonIsdkBlock(Transform t)
        {
            string n = t.name.Replace(" ", "").ToLowerInvariant();
            return NonIsdkBlockKeywords.Any(n.Contains);
        }

        /// <summary>
        /// Klucz rozpoznawania bloku: nazwa obiektu + nazwy typów komponentów w nim i w dzieciach
        /// (np. TouchHandGrabInteractable, DistanceHandGrabInteractable, PokeInteractable, RayInteractable).
        /// Różne bloki chwytu mogą mieć tę samą nazwę obiektu, ale różne komponenty.
        /// </summary>
        static string BlockKey(Transform t)
        {
            var sb = new StringBuilder(t.name);
            foreach (var c in t.GetComponentsInChildren<Component>(true))
                if (c != null) sb.Append('|').Append(c.GetType().Name);
            return sb.ToString().Replace(" ", "").ToLowerInvariant();
        }

        static IsdkStation MatchStation(string key)
        {
            // Kolejność ma znaczenie: "distance" i "touch" przed ogólnym "grab".
            foreach (string k in new[] { "teleport", "distance", "touch", "poke", "rayinteract", "grab" })
            {
                if (!key.Contains(k)) continue;
                return IsdkStations.FirstOrDefault(s => s.keywords.Contains(k));
            }
            return null;
        }

        /// <summary>
        /// Najwyższy obiekt, który należy przenieść: wspinamy się w górę hierarchii, aż do korzenia sceny,
        /// strefy ISDK albo stanowiska. Nie ruszamy obiektów demo ani rigu kamery.
        /// </summary>
        static Transform MovableRoot(Transform t, Transform zone, IEnumerable<Transform> stations)
        {
            var stationSet = new HashSet<Transform>(stations);
            while (t.parent != null && t.parent != zone && !stationSet.Contains(t.parent)) t = t.parent;

            if (t.parent == null)
            {
                if (DemoRootNames.Contains(t.name) || t.GetComponentInChildren<OVRCameraRig>(true) != null) return null;
            }
            return t;
        }

        // =====================================================================
        // POMOCNICZE
        // =====================================================================

        static void AddSceneToBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        static object[] DefaultArgs(MethodInfo m) =>
            m.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();

        static bool SetEnum(Object target, string valueName, bool warn, params string[] propertyNames)
        {
            if (target == null) return false;
            var so = new SerializedObject(target);
            foreach (string pn in propertyNames)
            {
                var p = so.FindProperty(pn);
                if (p == null || p.propertyType != SerializedPropertyType.Enum) continue;
                int i = Array.IndexOf(p.enumNames, valueName);
                if (i < 0) continue;
                if (p.enumValueIndex != i)
                {
                    p.enumValueIndex = i;
                    so.ApplyModifiedProperties();
                }
                return true;
            }
            if (warn) Debug.LogWarning($"[VRInputDemo] Nie ustawiono {valueName} na {target.GetType().Name} - ustaw ręcznie w Inspectorze.");
            return false;
        }
    }

    /// <summary>Po otwarciu projektu: otwiera scenę demo albo proponuje konfigurację.</summary>
    [InitializeOnLoad]
    static class VRInputDemoFirstRun
    {
        const string Key = "VRInputDemo.FirstRunChecked";

        static VRInputDemoFirstRun()
        {
            if (Application.isBatchMode || SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, true);
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = EditorSceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path);

            if (File.Exists(VRInputDemoSetup.ScenePath))
            {
                if (untitled && !active.isDirty) EditorSceneManager.OpenScene(VRInputDemoSetup.ScenePath);
                return;
            }

            if (EditorUtility.DisplayDialog("VR Input Demo",
                    "Scena demo jeszcze nie istnieje.\n\nKolejność (szczegóły w INSTRUKCJA.md):\n" +
                    "1. VR Input Demo > 1. Konfiguruj projekt dla Meta Quest\n" +
                    "2. VR Input Demo > 2. Zbuduj scenę demo\n" +
                    "3. Building Blocks: Camera Rig, Hand Tracking, Controller Tracking\n\n" +
                    "Skonfigurować projekt teraz?",
                    "Konfiguruj teraz", "Później"))
            {
                VRInputDemoSetup.ConfigureProject();
            }
        }
    }
}
