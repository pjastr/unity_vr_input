using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    ///  2. Zbuduj scenę demo - tworzy całą scenę z prymitywów + rig i dłonie z Meta XR Core SDK.
    ///  3. Build And Run (USB) - buduje APK (Development + Script Debugging) i uruchamia na goglach.
    /// </summary>
    public static class VRInputDemoSetup
    {
        public const string Root = "Assets/VRInputDemo";
        public const string ScenePath = Root + "/Scenes/InputDemo.unity";
        const string SettingsFolder = Root + "/Settings";
        const string MaterialsFolder = Root + "/Materials";
        const string CorePackage = "Packages/com.meta.xr.sdk.core";
        const string ApkPath = "Builds/VRInputDemo.apk";

        static readonly Vector3 HeadPos = new Vector3(0f, 1.6f, 0f);
        static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.9f);

        static List<GameObject> corePrefabsCache;

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
                "• Project Settings > XR Plug-in Management > zakładka Android: zaznacz OpenXR oraz grupę funkcji \"Meta XR\".\n" +
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
        // 2. BUDOWA SCENY
        // =====================================================================

        [MenuItem("VR Input Demo/2. Zbuduj scenę demo", false, 2)]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("VR Input Demo",
                    "Scena " + ScenePath + " już istnieje. Zbudować ją od nowa (nadpisać)?", "Nadpisz", "Anuluj"))
                return;

            EnsureUrp();
            Material lit = EnsureLitMaterial();
            DemoUI.LitMaterial = lit;
            corePrefabsCache = null;

            try
            {
                EditorUtility.DisplayProgressBar("VR Input Demo", "Tworzenie sceny...", 0.1f);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                BuildLighting();
                var env = new GameObject("Environment").transform;
                BuildEnvironment(env);

                EditorUtility.DisplayProgressBar("VR Input Demo", "OVRCameraRig, dłonie, kontrolery...", 0.3f);
                OVRCameraRig rig = CreateCameraRig();
                var ownVisuals = new List<GameObject>();
                OVRHand leftHand = CreateHand(rig, true, ownVisuals);
                OVRHand rightHand = CreateHand(rig, false, ownVisuals);
                CreateControllerVisual(rig, true, ownVisuals);
                CreateControllerVisual(rig, false, ownVisuals);

                var inputs = new GameObject("InputSources").transform;
                XRInputSource leftSrc = CreateInputSource(inputs, HandSide.Left, rig, leftHand);
                XRInputSource rightSrc = CreateInputSource(inputs, HandSide.Right, rig, rightHand);

                var locomotion = rig.gameObject.AddComponent<PlayerLocomotion>();
                locomotion.rig = rig;

                EditorUtility.DisplayProgressBar("VR Input Demo", "Obiekty interaktywne i panele...", 0.6f);
                var interactables = new GameObject("Interactables").transform;
                BuildGrabbables(interactables);
                BuildPokeButtons(interactables);
                BuildTargets(interactables);

                var panels = new GameObject("Panels");
                BuildPanels(panels.transform, leftSrc, rightSrc);

                BuildIsdkZone();

                var demo = new GameObject("Demo");
                demo.AddComponent<DemoResources>().litMaterial = lit;
                var actions = demo.AddComponent<DemoActions>();
                actions.left = leftSrc;
                actions.right = rightSrc;
                actions.locomotion = locomotion;
                actions.panelsRoot = panels;
                actions.ownVisualRoots = ownVisuals;

                SetHandTrackingSupport();

                EnsureFolder(Root + "/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuild();
                Selection.activeGameObject = rig.gameObject;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DemoUI.LitMaterial = null;
                corePrefabsCache = null;
            }

            EditorUtility.DisplayDialog("VR Input Demo",
                "Scena gotowa: " + ScenePath + "\n\n" +
                "Podłącz Quest 3S kablem USB i użyj: VR Input Demo > 3. Build And Run (USB)\n" +
                "(albo File > Build Profiles > Build And Run).\n\n" +
                "Strefa Interaction SDK jest za Twoimi plecami - komponenty ISDK dodaj Quick Actions (patrz INSTRUKCJA.md).",
                "OK");
        }

        // =====================================================================
        // 3. BUILD AND RUN
        // =====================================================================

        [MenuItem("VR Input Demo/3. Build And Run (USB)", false, 3)]
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

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[VRInputDemo] Build OK ({report.summary.totalSize / (1024f * 1024f):0.0} MB) -> {ApkPath}. Aplikacja uruchamia się na goglach.");
            else
                Debug.LogError($"[VRInputDemo] Build nieudany: {report.summary.result}. Sprawdź Console (błędy) i czy gogle są widoczne w 'adb devices'.");
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
        // META XR: RIG, DŁONIE, KONTROLERY
        // =====================================================================

        static OVRCameraRig CreateCameraRig()
        {
            OVRCameraRig rig = null;
            var prefab = FindCorePrefab(go => go.GetComponent<OVRCameraRig>() != null, go => go.name == "OVRCameraRig" ? 10 : 0);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = "OVRCameraRig";
                rig = inst.GetComponent<OVRCameraRig>();
            }
            if (rig == null)
            {
                Debug.LogWarning("[VRInputDemo] Nie znaleziono prefabu OVRCameraRig - tworzę rig z komponentu.");
                rig = new GameObject("OVRCameraRig").AddComponent<OVRCameraRig>();
            }

            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try { rig.EnsureGameObjectIntegrity(); }
            catch (Exception e) { Debug.LogWarning("[VRInputDemo] EnsureGameObjectIntegrity: " + e.Message); }

            var manager = Object.FindAnyObjectByType<OVRManager>();
            if (manager == null) manager = rig.gameObject.AddComponent<OVRManager>();
            // Poziom podłogi: y = 0 w scenie to podłoga w pokoju.
            SetEnum(manager, "FloorLevel", "_trackingOriginType", "m_TrackingOriginType", "trackingOriginType");

            if (rig.centerEyeAnchor != null)
            {
                var cam = rig.centerEyeAnchor.GetComponent<Camera>();
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
                    cam.nearClipPlane = 0.02f;
                    cam.farClipPlane = 100f;
                }
            }
            return rig;
        }

        static OVRHand CreateHand(OVRCameraRig rig, bool left, List<GameObject> ownVisuals)
        {
            Transform anchor = left ? rig.leftHandAnchor : rig.rightHandAnchor;
            if (anchor == null)
            {
                Debug.LogWarning("[VRInputDemo] Brak HandAnchor w OVRCameraRig - pomiń dłonie albo dodaj Building Block 'Hand Tracking'.");
                return null;
            }

            var prefab = FindCorePrefab(
                go => go.GetComponent<OVRHand>() != null && go.GetComponent<OVRSkeleton>() != null,
                go => ScoreHandPrefab(go, left));

            GameObject handGO;
            if (prefab != null)
            {
                handGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            }
            else
            {
                Debug.LogWarning("[VRInputDemo] Nie znaleziono prefabu dłoni - tworzę OVRHand + OVRSkeleton (wizualizacja: kulki w stawach).");
                handGO = new GameObject();
                handGO.transform.SetParent(anchor, false);
                handGO.AddComponent<OVRHand>();
                handGO.AddComponent<OVRSkeleton>();
            }

            handGO.name = left ? "LeftHand" : "RightHand";
            handGO.transform.localPosition = Vector3.zero;
            handGO.transform.localRotation = Quaternion.identity;
            SetHandSide(handGO, left);

            var hand = handGO.GetComponent<OVRHand>();
            if (rig.trackingSpace != null) SetObjectReference(hand, rig.trackingSpace, "_pointerPoseRoot");

            // Prefab z siatką dłoni ma SkinnedMeshRenderer; bez niego pokazujemy stawy jako kulki.
            if (handGO.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            {
                var viz = handGO.AddComponent<HandJointVisualizer>();
                viz.hand = hand;
                viz.skeleton = handGO.GetComponent<OVRSkeleton>();
            }

            ownVisuals.Add(handGO);
            return hand;
        }

        static int ScoreHandPrefab(GameObject go, bool left)
        {
            string n = go.name;
            int score = 0;
            if (n == "OVRHandPrefab") score += 5;
            if (n.Contains(left ? "Left" : "Right")) score += 10;
            if (n.Contains(left ? "Right" : "Left")) score -= 20;
            if (go.GetComponent("OVRMeshRenderer") != null) score += 3;
            return score;
        }

        /// <summary>
        /// Ustawia stronę dłoni we wszystkich polach typu enum komponentów OVR* (OVRHand, OVRSkeleton, OVRMesh):
        /// HandLeft &lt;-&gt; HandRight, XRHandLeft &lt;-&gt; XRHandRight. Działa niezależnie od wersji SDK.
        /// </summary>
        static void SetHandSide(GameObject go, bool left)
        {
            string want = left ? "Left" : "Right";
            string other = left ? "Right" : "Left";

            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (!typeName.StartsWith("OVR")) continue;
                bool handComponent = typeName == "OVRHand" || typeName == "OVRSkeleton" || typeName == "OVRMesh";

                var so = new SerializedObject(c);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.Enum) continue;
                    string[] names = it.enumNames;
                    int idx = it.enumValueIndex;
                    if (names == null || idx < 0 || idx >= names.Length) continue;

                    string cur = names[idx];
                    string target = null;
                    if (cur.Contains(other)) target = cur.Replace(other, want);
                    else if (cur == "None" && handComponent && Array.IndexOf(names, "Hand" + want) >= 0) target = "Hand" + want;
                    if (target == null || target == cur) continue;

                    int t = Array.IndexOf(names, target);
                    if (t >= 0)
                    {
                        it.enumValueIndex = t;
                        changed = true;
                    }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void CreateControllerVisual(OVRCameraRig rig, bool left, List<GameObject> ownVisuals)
        {
            Transform anchor = left ? rig.leftControllerAnchor : rig.rightControllerAnchor;
            if (anchor == null) return;
            var mask = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

            var prefab = FindCorePrefab(go => go.GetComponent("OVRControllerHelper") != null,
                go => go.name == "OVRControllerPrefab" ? 10 : 0);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
                inst.name = left ? "LeftControllerModel" : "RightControllerModel";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                SetInt(inst.GetComponent("OVRControllerHelper"), (int)mask, "m_controller");
                ownVisuals.Add(inst);
                return;
            }

            // Zapasowy model z prymitywów.
            var root = new GameObject(left ? "LeftControllerVisual" : "RightControllerVisual");
            root.transform.SetParent(anchor, false);
            var body = DemoUI.CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, -0.03f, -0.02f),
                new Vector3(0.035f, 0.045f, 0.035f), new Color(0.2f, 0.2f, 0.22f), false);
            body.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            DemoUI.CreatePrimitive(PrimitiveType.Cylinder, "Ring", root.transform, new Vector3(left ? 0.01f : -0.01f, 0.005f, 0.01f),
                new Vector3(0.075f, 0.006f, 0.075f), new Color(0.35f, 0.35f, 0.4f), false);
            DemoUI.DisableShadows(root);
            root.AddComponent<ControllerVisibility>().controller = mask;
            ownVisuals.Add(root);
        }

        static XRInputSource CreateInputSource(Transform parent, HandSide side, OVRCameraRig rig, OVRHand hand)
        {
            var go = new GameObject(side == HandSide.Left ? "LeftInput" : "RightInput");
            go.transform.SetParent(parent, false);

            OVRSkeleton skeleton = hand != null ? hand.GetComponent<OVRSkeleton>() : null;

            var detector = go.AddComponent<HandGestureDetector>();
            detector.side = side;
            detector.hand = hand;
            detector.skeleton = skeleton;

            var src = go.AddComponent<XRInputSource>();
            src.side = side;
            src.rig = rig;
            src.hand = hand;
            src.skeleton = skeleton;
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

                Debug.Log("[VRInputDemo] Hand Tracking Support = Controllers And Hands");
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
            "<color=#C8A0FF>Za Tobą: strefa Interaction SDK (komponenty ISDK dodane Quick Actions).</color>";

        // =====================================================================
        // STREFA INTERACTION SDK
        // =====================================================================

        static void BuildIsdkZone()
        {
            var zone = new GameObject("ISDK_Zone");
            zone.AddComponent<IsdkZone>();
            var z = zone.transform;

            CreateTable(z, "ISDK_Table", new Vector3(0f, 0f, -1.0f), new Vector2(1.4f, 0.6f));

            // Obiekty do Quick Actions "Grab" / "Distance Grab" / "Ray Grab" (Rigidbody już dodany).
            IsdkObject(z, "ISDK_Grab_Kostka", PrimitiveType.Cube, new Vector3(0.45f, 0.845f, -0.95f), Vector3.one * 0.08f, new Color(0.75f, 0.45f, 1f));
            IsdkObject(z, "ISDK_Grab_Kula", PrimitiveType.Sphere, new Vector3(0.25f, 0.85f, -0.95f), Vector3.one * 0.09f, new Color(0.55f, 0.4f, 0.95f));
            IsdkObject(z, "ISDK_DistanceGrab_Walec", PrimitiveType.Cylinder, new Vector3(-0.1f, 0.855f, -1.15f), new Vector3(0.07f, 0.05f, 0.07f), new Color(0.4f, 0.8f, 0.9f));
            IsdkObject(z, "ISDK_RayGrab_Kapsula", PrimitiveType.Capsule, new Vector3(-0.4f, 0.865f, -1.15f), new Vector3(0.05f, 0.06f, 0.05f), new Color(0.95f, 0.6f, 0.4f));

            // Podłoga do Quick Action "Teleport".
            var tp = DemoUI.CreatePrimitive(PrimitiveType.Cube, "ISDK_Teleport_Podloga", z, new Vector3(0f, 0.005f, -3.3f),
                new Vector3(2f, 0.01f, 2f), new Color(0.2f, 0.45f, 0.5f));
            DemoUI.DisableShadows(tp);

            // Canvas do Quick Actions "Poke Canvas" / "Ray Canvas".
            Vector3 canvasPos = new Vector3(0f, 1.3f, -1.75f);
            var canvas = DemoUI.CreatePanel("ISDK_UI_Canvas", z, canvasPos, DemoUI.FacePoint(canvasPos, HeadPos), new Vector2(700f, 420f),
                new Color(0.12f, 0.08f, 0.2f, 0.92f));
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            DemoUI.CreateText(canvas, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -90f), new Vector2(-20f, -10f), 30,
                TextAnchor.UpperCenter, Color.white, "<b>Strefa Interaction SDK</b>\nprzycisk UI: poke palcem lub promień");

            var btnGO = new GameObject("ISDK_Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var btnRt = (RectTransform)btnGO.transform;
            btnRt.SetParent(canvas, false);
            btnRt.sizeDelta = new Vector2(380f, 120f);
            btnRt.anchoredPosition = new Vector2(0f, -10f);
            var img = btnGO.GetComponent<Image>();
            img.color = new Color(0.3f, 0.45f, 0.95f);
            var btn = btnGO.GetComponent<Button>();
            btn.targetGraphic = img;
            DemoUI.CreateFillText(btnRt, "Label", 8f, 36, TextAnchor.MiddleCenter, Color.white, "Kliknij mnie");

            var counter = DemoUI.CreateText(canvas, "Counter", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 20f), new Vector2(-20f, 80f), 30,
                TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f), "Kliknięcia: 0");
            btnGO.AddComponent<IsdkButtonCounter>().label = counter;

            // Tablica z opisem, co gdzie dodać.
            Vector3 signPos = new Vector3(1.4f, 1.45f, -1.3f);
            var sign = DemoUI.CreatePanel("ISDK_Info", z, signPos, DemoUI.FacePoint(signPos, HeadPos), new Vector2(900f, 640f), PanelBg);
            DemoUI.CreateFillText(sign, "Text", 25f, 26, TextAnchor.UpperLeft, Color.white,
                "<b><color=#C8A0FF>INTERACTION SDK</color></b>\n" +
                "Obiekty w tej strefie obsługują komponenty ISDK, a nie skrypty demo.\n" +
                "W edytorze: prawy klik na obiekcie w Hierarchy > Interaction SDK:\n\n" +
                "• ISDK_Grab_* – Add Grab Interaction\n" +
                "• ISDK_DistanceGrab_Walec – Add Distance Grab Interaction\n" +
                "• ISDK_RayGrab_Kapsula – Add Ray Grab Interaction\n" +
                "• ISDK_UI_Canvas – Add Poke / Ray Interaction to Canvas\n" +
                "• ISDK_Teleport_Podloga – Add Teleport Interaction\n\n" +
                "W kreatorze: Fix All, potem Create (doda też rig ISDK).");
        }

        static void IsdkObject(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = DemoUI.CreatePrimitive(type, name, parent, pos, scale, color);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
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

        static GameObject FindCorePrefab(Func<GameObject, bool> filter, Func<GameObject, int> score)
        {
            if (corePrefabsCache == null)
            {
                corePrefabsCache = new List<GameObject>();
                if (AssetDatabase.IsValidFolder(CorePackage))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CorePackage }))
                    {
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                        if (go != null) corePrefabsCache.Add(go);
                    }
                }
                else
                {
                    Debug.LogWarning("[VRInputDemo] Nie znaleziono pakietu com.meta.xr.sdk.core.");
                }
            }

            GameObject best = null;
            int bestScore = int.MinValue;
            foreach (var go in corePrefabsCache)
            {
                bool ok;
                try { ok = filter(go); } catch { ok = false; }
                if (!ok) continue;
                int s = score != null ? score(go) : 0;
                if (s > bestScore) { bestScore = s; best = go; }
            }
            return best;
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

        static bool SetEnum(Object target, string valueName, params string[] propertyNames)
        {
            if (target == null) return false;
            var so = new SerializedObject(target);
            foreach (string pn in propertyNames)
            {
                var p = so.FindProperty(pn);
                if (p == null || p.propertyType != SerializedPropertyType.Enum) continue;
                int i = Array.IndexOf(p.enumNames, valueName);
                if (i < 0) continue;
                p.enumValueIndex = i;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
            Debug.LogWarning($"[VRInputDemo] Nie ustawiono {valueName} na {target.GetType().Name} - ustaw ręcznie w Inspectorze.");
            return false;
        }

        static void SetInt(Object target, int value, params string[] propertyNames)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            foreach (string pn in propertyNames)
            {
                var p = so.FindProperty(pn);
                if (p == null) continue;
                p.intValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }

        static void SetObjectReference(Object target, Object value, params string[] propertyNames)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            foreach (string pn in propertyNames)
            {
                var p = so.FindProperty(pn);
                if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;
                p.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }
    }

    /// <summary>Po otwarciu projektu: otwiera scenę demo albo proponuje jej zbudowanie.</summary>
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
                    "Scena demo jeszcze nie istnieje.\n\nZalecana kolejność:\n" +
                    "1. VR Input Demo > 1. Konfiguruj projekt dla Meta Quest\n" +
                    "2. VR Input Demo > 2. Zbuduj scenę demo\n\nSkonfigurować projekt teraz?",
                    "Konfiguruj teraz", "Później"))
            {
                VRInputDemoSetup.ConfigureProject();
            }
        }
    }
}
