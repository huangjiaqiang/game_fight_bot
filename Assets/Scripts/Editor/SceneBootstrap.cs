#if UNITY_EDITOR
using System.IO;
using FightBot.Game;
using FightBot.Motion;
using FightBot.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FightBot.EditorTools
{
    /// <summary>
    /// 项目自举: 在 Unity 编辑器顶部菜单点击 FightBot > Build Scenes,
    /// 一键生成 Mecha.prefab + Battle.unity + Menu.unity + Android 配置.
    /// 用户首次打开项目时执行一次即可.
    /// </summary>
    public static class SceneBootstrap
    {
        const string PrefabPath = "Assets/Prefabs/Mecha.prefab";
        const string BattlePath = "Assets/Scenes/Battle.unity";
        const string MenuPath = "Assets/Scenes/Menu.unity";

        [MenuItem("FightBot/1. Build All (Scenes + Prefab + Settings)")]
        public static void BuildAll()
        {
            EnsureFolders();
            EnsureLayers();
            ConfigureAndroid();

            var mechaPrefab = BuildMechaPrefab();
            BuildBattleScene(mechaPrefab);
            BuildMenuScene();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[SceneBootstrap] 全部完成. 打开 Assets/Scenes/Battle.unity 试运行.");
            EditorUtility.DisplayDialog("FightBot 自举",
                "已完成:\n" +
                "- 创建 Mecha.prefab\n" +
                "- 创建 Battle / Menu 场景\n" +
                "- 配置 Android Player Settings\n" +
                "- 添加 Enemy 图层\n\n" +
                "下一步: 打开 Assets/Scenes/Battle.unity 按 Play.", "OK");
        }

        [MenuItem("FightBot/2. Configure Android Player Settings")]
        public static void ConfigureAndroid()
        {
            var ps = PlayerSettings;
            ps.applicationIdentifier = "com.fightbot.mecha";
            ps.companyName = "FightBot";
            ps.productName = "FightBot Mecha";
            ps.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            ps.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            ps.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            ps.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            ps.SetArchitecture(BuildTargetGroup.Android, 2); // ARM64
            // Sentis GPU 需要 Android API 24+
            Debug.Log("[SceneBootstrap] Android PlayerSettings 已配置 (minSdk 24, targetSdk 34, landscape, IL2CPP ARM64).");
        }

        static void EnsureLayers()
        {
            // 确保 Enemy 图层存在
            var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(tagManager);
            var layers = so.FindProperty("layers");
            bool hasEnemy = false;
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == "Enemy") { hasEnemy = true; break; }
            }
            if (!hasEnemy)
            {
                // 找第一个空位 (从 8 开始是用户层)
                for (int i = 8; i < layers.arraySize; i++)
                {
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    {
                        layers.GetArrayElementAtIndex(i).stringValue = "Enemy";
                        so.ApplyModifiedProperties();
                        Debug.Log($"[SceneBootstrap] 已添加 Enemy 图层 (idx={i}).");
                        break;
                    }
                }
            }
        }

        static void EnsureFolders()
        {
            const string resourcesModelAssetPath = "Assets/ML/Models/movenet_singlepose_lightning";
            // 检查模型有没有对应的 .asset (Sentis ModelAsset)
            var tflite = AssetDatabase.LoadAssetAtPath<TextAsset>(resourcesModelAssetAssetPath + ".tflite");
            // 让用户知道怎么处理 tflite
            var existing = AssetDatabase.LoadAssetAtPath<ModelAsset>(resourcesModelAssetAssetPath + ".asset");
            if (existing == null)
            {
                Debug.LogWarning("[SceneBootstrap] 未找到 Sentis ModelAsset (.asset) 关联到 tflite.\n" +
                                 "请在 Unity 里点 Assets/ML/Models/movenet_singlepose_lightning.tflite, " +
                                 "Inspector 中按 Generate ModelAsset, 然后将其拖到 Mecha.prefab 的 MotionPipeline.MoveNetModel.");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
        }

        static string resourcesModelAssetPath = "Assets/ML/Models/movenet_singlepose_lightning";

        static GameObject BuildMechaPrefab()
        {
            // 根 GameObject
            var root = new GameObject("Mecha");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 2.2f; cc.radius = 0.6f; cc.center = new Vector3(0f, 1.1f, 0f);

            var rig = root.AddComponent<MechaRig>();
            var ctrl = root.AddComponent<MechaController>();

            // 用 primitive 组装骨骼
            rig.Root = root.transform;
            rig.Hips = CreatePart(root.transform, "Hips", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(0.7f, 0.4f, 0.4f), new Color(0.55f, 0.6f, 0.7f));
            rig.Spine = CreatePart(rig.Hips, "Spine", PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.4f, 0.3f), new Color(0.55f, 0.6f, 0.7f));
            rig.Chest = CreatePart(rig.Spine, "Chest", PrimitiveType.Cube, new Vector3(0f, 0.35f, 0f), new Vector3(0.8f, 0.5f, 0.4f), new Color(0.5f, 0.55f, 0.65f));
            rig.Head = CreatePart(rig.Chest, "Head", PrimitiveType.Capsule, new Vector3(0f, 0.55f, 0f), new Vector3(0.4f, 0.4f, 0.4f), new Color(0.6f, 0.65f, 0.75f));

            // 左臂
            rig.LeftShoulder = CreatePart(rig.Chest, "LeftShoulder", PrimitiveType.Sphere, new Vector3(-0.5f, 0.25f, 0f), new Vector3(0.2f, 0.2f, 0.2f), new Color(0.4f, 0.45f, 0.55f));
            rig.LeftUpperArm = CreatePart(rig.LeftShoulder, "LeftUpperArm", PrimitiveType.Cylinder, new Vector3(-0.15f, 0f, 0f), new Vector3(0.18f, 0.35f, 0.18f), new Color(0.5f, 0.55f, 0.65f), rotEuler: new Vector3(0f, 0f, 90f));
            rig.LeftForearm = CreatePart(rig.LeftUpperArm, "LeftForearm", PrimitiveType.Cylinder, new Vector3(0f, -0.4f, 0.05f), new Vector3(0.16f, 0.3f, 0.16f), new Color(0.5f, 0.55f, 0.65f));
            rig.LeftHand = CreatePart(rig.LeftForearm, "LeftHand", PrimitiveType.Sphere, new Vector3(0f, -0.3f, 0.05f), new Vector3(0.18f, 0.18f, 0.18f), new Color(0.4f, 0.45f, 0.55f));
            rig.LeftWeapon = CreatePart(rig.LeftHand, "LeftWeapon", PrimitiveType.Cube, new Vector3(0f, 0f, 0.4f), new Vector3(0.15f, 0.15f, 0.5f), new Color(0.2f, 0.2f, 0.25f));

            // 右臂 (mirror)
            rig.RightShoulder = CreatePart(rig.Chest, "RightShoulder", PrimitiveType.Sphere, new Vector3(0.5f, 0.25f, 0f), new Vector3(0.2f, 0.2f, 0.2f), new Color(0.4f, 0.45f, 0.55f));
            rig.RightUpperArm = CreatePart(rig.RightShoulder, "RightUpperArm", PrimitiveType.Cylinder, new Vector3(0.15f, 0f, 0f), new Vector3(0.18f, 0.35f, 0.18f), new Color(0.5f, 0.55f, 0.65f), rotEuler: new Vector3(0f, 0f, 90f));
            rig.RightForearm = CreatePart(rig.RightUpperArm, "RightForearm", PrimitiveType.Cylinder, new Vector3(0f, -0.4f, 0.05f), new Vector3(0.16f, 0.3f, 0.16f), new Color(0.5f, 0.55f, 0.65f));
            rig.RightHand = CreatePart(rig.RightForearm, "RightHand", PrimitiveType.Sphere, new Vector3(0f, -0.3f, 0.05f), new Vector3(0.18f, 0.18f, 0.18f), new Color(0.4f, 0.45f, 0.55f));
            rig.RightWeapon = CreatePart(rig.RightHand, "RightWeapon", PrimitiveType.Cube, new Vector3(0f, 0f, 0.4f), new Vector3(0.15f, 0.15f, 0.5f), new Color(0.2f, 0.2f, 0.25f));

            // 腿
            rig.LeftUpperLeg = CreatePart(rig.Hips, "LeftUpperLeg", PrimitiveType.Cylinder, new Vector3(-0.25f, -0.35f, 0f), new Vector3(0.22f, 0.5f, 0.22f), new Color(0.5f, 0.55f, 0.65f));
            rig.LeftLowerLeg = CreatePart(rig.LeftUpperLeg, "LeftLowerLeg", PrimitiveType.Cylinder, new Vector3(0f, -0.55f, 0f), new Vector3(0.18f, 0.5f, 0.18f), new Color(0.5f, 0.55f, 0.65f));
            rig.LeftFoot = CreatePart(rig.LeftLowerLeg, "LeftFoot", PrimitiveType.Cube, new Vector3(0f, -0.55f, 0.1f), new Vector3(0.25f, 0.1f, 0.45f), new Color(0.3f, 0.3f, 0.35f));
            rig.RightUpperLeg = CreatePart(rig.Hips, "RightUpperLeg", PrimitiveType.Cylinder, new Vector3(0.25f, -0.35f, 0f), new Vector3(0.22f, 0.5f, 0.22f), new Color(0.5f, 0.55f, 0.65f));
            rig.RightLowerLeg = CreatePart(rig.RightUpperLeg, "RightLowerLeg", PrimitiveType.Cylinder, new Vector3(0f, -0.55f, 0f), new Vector3(0.18f, 0.5f, 0.18f), new Color(0.5f, 0.55f, 0.65f));
            rig.RightFoot = CreatePart(rig.RightLowerLeg, "RightFoot", PrimitiveType.Cube, new Vector3(0f, -0.55f, 0.1f), new Vector3(0.25f, 0.1f, 0.45f), new Color(0.3f, 0.3f, 0.35f));

            // 把首个 renderer 当 BodyRenderer, 武器 renderer 单独
            rig.BodyRenderer = rig.Chest.GetComponent<Renderer>();
            rig.WeaponRenderer = rig.LeftWeapon.GetComponent<Renderer>();

            // 保存为 prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform CreatePart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Color color, Vector3? rotEuler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (rotEuler.HasValue) go.transform.localEulerAngles = rotEuler.Value;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = color;
                r.sharedMaterial = mat;
            }
            return go.transform;
        }

        static void BuildBattleScene(GameObject mechaPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            groundMat.color = new Color(0.18f, 0.22f, 0.18f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            // 灯光
            var dir = new GameObject("DirectionalLight");
            dir.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var dl = dir.AddComponent<Light>();
            dl.type = LightType.Directional;
            dl.intensity = 1.2f;

            // 机甲 (从 prefab 实例化)
            var mecha = (GameObject)PrefabUtility.InstantiatePrefab(mechaPrefab);
            mecha.transform.position = new Vector3(0f, 0f, 0f);
            var ctrl = mecha.GetComponent<MechaController>();
            var rig = mecha.GetComponent<MechaRig>();
            ctrl.Rig = rig;

            // 摄像机
            var camGo = new GameObject("MainCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(0f, 5f, -9f);
            var rig2 = camGo.AddComponent<CameraRig>();
            rig2.Target = mecha.transform;

            // MotionPipeline
            var pipeGo = new GameObject("MotionPipeline");
            var pipe = pipeGo.AddComponent<MotionPipeline>();
            // 模型关联 (用户需要在 Inspector 里把 .asset 拖进来, 这里只做提示)
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(resourcesModelAssetPath + ".asset");
            if (modelAsset != null) pipe.MoveNetModel = modelAsset;
            ctrl.Pipeline = pipe;

            // Enemy 图层目标: 3 个 dummy
            for (int i = 0; i < 3; i++)
            {
                var enemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                enemy.name = $"Enemy_{i}";
                enemy.transform.position = new Vector3(Random.Range(-3f, 3f), 0.75f, 5f + i * 2f);
                enemy.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
                enemy.layer = LayerMask.NameToLayer("Enemy");
                var emat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                emat.color = new Color(0.65f, 0.25f, 0.2f);
                enemy.GetComponent<Renderer>().sharedMaterial = emat;
                enemy.AddComponent<EnemyDummy>();
            }

            // HUD Canvas
            var canvas = new GameObject("HUD");
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.AddComponent<CanvasScaler>();
            canvas.AddComponent<GraphicRaycaster>();

            var hud = canvas.AddComponent<GameHUD>();
            hud.ScoreText = MakeText(canvas.transform, "ScoreText", new Vector2(20f, -20f), TextAnchor.UpperLeft, 28);
            hud.HpText = MakeText(canvas.transform, "HpText", new Vector2(20f, -60f), TextAnchor.UpperLeft, 22);
            var hpBarGo = new GameObject("HpBar", typeof(Image));
            hpBarGo.transform.SetParent(canvas.transform, false);
            var hpBarRect = hpBarGo.GetComponent<RectTransform>();
            hpBarRect.anchorMin = hpBarRect.anchorMax = new Vector2(0f, 1f);
            hpBarRect.pivot = new Vector2(0f, 1f);
            hpBarRect.anchoredPosition = new Vector2(120f, -60f);
            hpBarRect.sizeDelta = new Vector2(200f, 20f);
            hpBarGo.GetComponent<Image>().color = new Color(0.2f, 0.8f, 0.3f);
            hud.HpBar = hpBarGo.GetComponent<Image>();
            hud.MessageText = MakeText(canvas.transform, "MessageText", new Vector2(0f, -100f), TextAnchor.UpperCenter, 32);

            // EventSystem (UI 必需)
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            // GameManager
            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.Pipeline = pipe;
            gm.Mecha = ctrl;
            gm.CameraRig = rig2;
            gm.Hud = hud;

            // DebugBar
            var dbgGo = new GameObject("DebugBar");
            var dbg = dbgGo.AddComponent<DebugBar>();
            dbg.Pipeline = pipe;
            dbg.Mecha = ctrl;

            EditorSceneManager.SaveScene(scene, BattlePath);
            Debug.Log($"[SceneBootstrap] 已保存 {BattlePath}");
        }

        static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("MainCamera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.12f);

            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            MakeText(canvas.transform, "Title", new Vector2(0f, 50f), TextAnchor.MiddleCenter, 56, "FIGHTBOT 机甲体感对战");
            MakeText(canvas.transform, "Hint", new Vector2(0f, -40f), TextAnchor.MiddleCenter, 24,
                "Build Settings 把 Battle.unity 加入场景列表后, 运行 Battle 即可\n(此 Menu 场景仅作占位)");

            EditorSceneManager.SaveScene(scene, MenuPath);
            Debug.Log($"[SceneBootstrap] 已保存 {MenuPath}");
        }

        static Text MakeText(Transform parent, string name, Vector2 anchoredPos, TextAnchor anchor, int fontSize, string content = "")
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(800f, 80f);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }
    }
}
#endif
