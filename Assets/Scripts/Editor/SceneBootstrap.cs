#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FightBot.Game;
using FightBot.Motion;
using FightBot.UI;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        const string FbxModelPath = "Assets/Models/QuaterniusRobot/Robot.fbx";

        [MenuItem("FightBot/1. Build All (Scenes + Prefab + Settings)")]
        public static void BuildAll()
        {
            EnsureFolders();
            EnsureLayers();
            ConfigureAndroid();
            EnsureRenderPipeline();

            EnsureFbxImported();
            var mechaPrefab = BuildMechaPrefabFbx() ?? BuildMechaPrefabPrimitive();
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

        [MenuItem("FightBot/3. Build Android APK (auto scenes + build)")]
        public static void BuildApk()
        {
            // 1. 生成场景 / Prefab / Android PlayerSettings (复用 BuildAll)
            BuildAll();

            // 2. BuildSettings 场景列表: Battle 放第 0 个, App 直接进游戏 (Menu 仅占位, 不自动加载)
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BattlePath, true),
                new EditorBuildSettingsScene(MenuPath, true),
            };

            // 3. 确保是 Android 平台 (CLI 已用 -buildTarget Android 打开, 这里兜底)
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            // 4. 输出路径 (Build/ 与 Assets 同级, 被 .gitignore 忽略). 用 Application.dataPath 锚定项目根.
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outDir = Path.Combine(projectRoot, "Build");
            string apkPath = Path.Combine(outDir, "fightbot.apk");
            Directory.CreateDirectory(outDir);

            // 5. 构建
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { BattlePath, MenuPath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development, // 首次用 dev build: 快 + 可调试 (DebugBar OnGUI 生效)
            };
            Debug.Log($"[SceneBootstrap] 开始构建 APK -> {opts.locationPathName}");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[SceneBootstrap] Build 结果={s.result} 用时={s.totalTime} 大小={s.totalSize} bytes -> {opts.locationPathName}");
            if (s.result != BuildResult.Succeeded)
                Debug.LogError($"[SceneBootstrap] APK 构建未成功: {s.result}");
        }

        static void EnsureRenderPipeline()
        {
            // 程序化创建的 URP 资产在安卓运行时 SetupPerFrameShaderConstants 会 NRE (内部状态不全).
            // 改用内置渲染管线 + Standard 着色器 (SceneBootstrap 里材质都用 Standard), 稳且能正常渲染.
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                GraphicsSettings.defaultRenderPipeline = null;
                Debug.Log("[SceneBootstrap] 关闭 URP -> 使用内置渲染管线 + Standard 着色器");
            }
        }

        static void EnsureFbxImported()
        {
            // Robot.fbx 以 Generic rig 导入: 保留骨骼绑定, 不引入动画片段覆盖骨骼
            // (程序化驱动骨骼不需要 Avatar/Animator). FBX 找不到时静默回退 primitive.
            if (!System.IO.File.Exists(FbxModelPath))
            {
                Debug.LogWarning($"[FBX] 文件不存在: {FbxModelPath}, 将使用 primitive 机甲.");
                return;
            }
            AssetDatabase.ImportAsset(FbxModelPath); // 确保已导入 (生成 .meta / 材质)
            var importer = AssetImporter.GetAtPath(FbxModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[FBX] 无法获取 importer: {FbxModelPath}, 将使用 primitive 机甲.");
                return;
            }
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (importer.importAnimation)
            {
                importer.importAnimation = false;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log("[FBX] Robot.fbx 已配置为 Generic rig (关闭动画导入).");
            }
        }

        [MenuItem("FightBot/2. Configure Android Player Settings")]
        public static void ConfigureAndroid()
        {
            PlayerSettings.applicationIdentifier = "com.fightbot.mecha";
            PlayerSettings.companyName = "FightBot";
            PlayerSettings.productName = "FightBot Mecha";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(BuildTargetGroup.Android, 2); // ARM64
            // 启用 Custom Main Gradle Template (无公开 API, 用 SerializedObject 改 ProjectSettings 的序列化字段)
            // 以便 mainTemplate.gradle 生效, 给 fightbotpose.aar 解析 CameraX/TFLite 依赖
            {
                var psAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (psAsset != null && psAsset.Length > 0)
                {
                    var so = new SerializedObject(psAsset[0]);
                    var p = so.FindProperty("useCustomMainGradleTemplate");
                    if (p != null && !p.boolValue) { p.boolValue = true; so.ApplyModifiedProperties(); }
                }
            }
            // Sentis GPU 需要 Android API 24+
            EnsureAndroidToolchainPaths();
            Debug.Log("[SceneBootstrap] Android PlayerSettings 已配置 (minSdk 24, targetSdk 34, landscape, IL2CPP ARM64).");
        }

        // batchmode 下 EditorPrefs 可能未配 Android SDK/NDK/JDK 路径.
        // 注意: Unity 在编辑器启动时缓存 SDK 路径, 运行时改 EditorPrefs 不被 BuildPlayer 重读.
        // 因此 SDK 路径需在 Unity 启动前预设 (注册表 reg add 或编辑器 External Tools). 这里仅在缺失时补.
        static void EnsureAndroidToolchainPaths()
        {
            string ap = System.IO.Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            bool validSdk(string p) => !string.IsNullOrEmpty(p) && System.IO.Directory.Exists(System.IO.Path.Combine(p, "cmdline-tools"));

            // SDK: 已指向含 cmdline-tools 的有效 SDK 则不覆盖; 否则环境变量; 最后自带 (自带缺 cmdline-tools)
            if (!validSdk(EditorPrefs.GetString("AndroidSdkRoot")))
            {
                string env = System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                          ?? System.Environment.GetEnvironmentVariable("ANDROID_HOME");
                string cand = validSdk(env) ? env : System.IO.Path.Combine(ap, "SDK");
                if (System.IO.Directory.Exists(cand)) EditorPrefs.SetString("AndroidSdkRoot", cand);
            }

            void SetIfDir(string key, string sub)
            {
                if (string.IsNullOrEmpty(EditorPrefs.GetString(key)))
                {
                    string p = System.IO.Path.Combine(ap, sub);
                    if (System.IO.Directory.Exists(p)) EditorPrefs.SetString(key, p);
                }
            }
            SetIfDir("AndroidNdkRoot", "NDK");
            // JDK: cmdline-tools/latest 需 Java 17 (Unity 自带 OpenJDK 11 跑不动 sdkmanager).
            // 环境变量 JAVA_HOME 可外部指定 Java17 (覆盖自带 11).
            string jdkEnv = System.Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(jdkEnv) && System.IO.File.Exists(System.IO.Path.Combine(jdkEnv, "bin", "java.exe")))
                EditorPrefs.SetString("JdkPath", jdkEnv);
            else SetIfDir("JdkPath", "OpenJDK");
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
            var tflite = AssetDatabase.LoadAssetAtPath<TextAsset>(resourcesModelAssetPath + ".tflite");
            // 让用户知道怎么处理 tflite
            var existing = AssetDatabase.LoadAssetAtPath<ModelAsset>(resourcesModelAssetPath + ".onnx")
                ?? AssetDatabase.LoadAssetAtPath<ModelAsset>(resourcesModelAssetPath + ".asset");
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

        // 机甲材质必须存成 .mat 资产: 运行时 new Material() 在 SaveAsPrefabAsset 时引用会丢失
        // (prefab 里变成 fileID:0 -> 部件品红). 按颜色缓存复用, 每种颜色一个 .mat.
        static readonly Dictionary<Color, Material> s_MatCache = new Dictionary<Color, Material>();
        static Material GetOrCreateMat(Color c)
        {
            if (s_MatCache.TryGetValue(c, out var m)) return m;
            m = new Material(Shader.Find("Standard"));
            m.color = c;
            AssetDatabase.CreateAsset(m, $"Assets/Prefabs/Materials/mecha_mat_{s_MatCache.Count}.mat");
            s_MatCache[c] = m;
            return m;
        }

        static GameObject BuildMechaPrefabPrimitive()
        {
            // 材质资产目录: 每次重新生成 (清掉旧 .mat)
            const string matDir = "Assets/Prefabs/Materials";
            if (AssetDatabase.IsValidFolder(matDir)) AssetDatabase.DeleteAsset(matDir);
            AssetDatabase.CreateFolder("Assets/Prefabs", "Materials");
            s_MatCache.Clear();

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

        [MenuItem("FightBot/1b. Rebuild Mecha Prefab (FBX Model)")]
        public static void RebuildMechaFbx()
        {
            EnsureFolders();
            EnsureLayers();
            EnsureFbxImported();
            var prefab = BuildMechaPrefabFbx() ?? BuildMechaPrefabPrimitive();
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("FightBot Mecha (FBX)",
                "已用 Robot.fbx 重建 Mecha.prefab.\n" +
                "- 查看 Console 是否有 '骨骼未映射' warning\n" +
                "- 打开 Battle 场景按 Play 验证\n" +
                "- 出拳/举手姿态不对时, 调 Mecha.prefab 上 MechaController 的 Rig 调参字段", "OK");
        }

        // 用 Quaternius Robot.fbx (带骨骼+蒙皮) 替换 primitive 方块外观.
        // 原理: FBX 骨骼就是 Transform 层级, MechaController.ApplyRig 程序化写骨骼的
        // localRotation/localPosition, SkinnedMeshRenderer 自动跟随蒙皮. 无需 Avatar/Animator.
        // 骨骼命名映射依据 Robot.fbx 实际层级 (Blender 风格 部位.L / 部位.R):
        //   Bone>Body>Hips>Abdomen>Torso>{Neck>Head, Shoulder.L>UpperArm.L>LowerArm.L>Palm1.L, ...R}
        //   Hips>UpperLeg.L>LowerLeg.L ; Body>Foot.L
        static GameObject BuildMechaPrefabFbx()
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxModelPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[FBX] 加载失败: {FbxModelPath}. 回退 primitive.");
                return null;
            }

            // 材质目录: 删重建 (避免与 primitive 残留的 mecha_mat_N 撞 path 导致 CreateAsset 失败)
            const string matDir = "Assets/Prefabs/Materials";
            if (AssetDatabase.IsValidFolder(matDir)) AssetDatabase.DeleteAsset(matDir);
            AssetDatabase.CreateFolder("Assets/Prefabs", "Materials");
            s_MatCache.Clear();

            // 根 GameObject (同 primitive 版)
            var root = new GameObject("Mecha");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 2.2f; cc.radius = 0.6f; cc.center = new Vector3(0f, 1.1f, 0f);
            var rig = root.AddComponent<MechaRig>();
            root.AddComponent<MechaController>();

            // 实例化 FBX 作为视觉子物体
            var fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            fbxInstance.name = "RobotModel";
            fbxInstance.transform.SetParent(root.transform, false);

            // 缩放到 ~2.2m 高, 再把脚提到 local y=0 (对齐 CharacterController 胶囊底)
            float origH = CombinedBounds(fbxInstance).size.y;
            float s = (origH > 0.01f) ? (2.2f / origH) : 1f;
            fbxInstance.transform.localScale = Vector3.one * s;
            Bounds b = CombinedBounds(fbxInstance);
            fbxInstance.transform.localPosition = new Vector3(0f, -b.min.y, 0f);

            // 骨骼映射
            rig.Root = root.transform;
            rig.Hips = FindBone(fbxInstance.transform, "Hips");
            rig.Spine = FindBone(fbxInstance.transform, "Abdomen");   // 脊椎首段
            rig.Chest = FindBone(fbxInstance.transform, "Torso");      // 胸
            rig.Head = FindBone(fbxInstance.transform, "Head");
            rig.LeftShoulder = FindBone(fbxInstance.transform, "Shoulder.L");
            rig.LeftUpperArm = FindBone(fbxInstance.transform, "UpperArm.L");
            rig.LeftForearm = FindBone(fbxInstance.transform, "LowerArm.L");
            rig.LeftHand = FindBone(fbxInstance.transform, "Palm1.L");  // 手掌骨
            rig.LeftUpperLeg = FindBone(fbxInstance.transform, "UpperLeg.L");
            rig.LeftLowerLeg = FindBone(fbxInstance.transform, "LowerLeg.L");
            rig.LeftFoot = FindBone(fbxInstance.transform, "Foot.L");
            rig.RightShoulder = FindBone(fbxInstance.transform, "Shoulder.R");
            rig.RightUpperArm = FindBone(fbxInstance.transform, "UpperArm.R");
            rig.RightForearm = FindBone(fbxInstance.transform, "LowerArm.R");
            rig.RightHand = FindBone(fbxInstance.transform, "Palm1.R");
            rig.RightUpperLeg = FindBone(fbxInstance.transform, "UpperLeg.R");
            rig.RightLowerLeg = FindBone(fbxInstance.transform, "LowerLeg.R");
            rig.RightFoot = FindBone(fbxInstance.transform, "Foot.R");

            // 武器锚点: FBX 无武器骨骼, 在手掌下挂锚点保留出拳前伸逻辑
            rig.LeftWeapon = CreateWeaponAnchor(rig.LeftHand, "LeftWeapon");
            rig.RightWeapon = CreateWeaponAnchor(rig.RightHand, "RightWeapon");

            // Renderer (受击变色走 _Color)
            rig.BodyRenderer = fbxInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            rig.WeaponRenderer = rig.LeftWeapon != null ? rig.LeftWeapon.GetComponent<Renderer>() : null;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[FBX] Mecha.prefab 已用 Robot.fbx 重建.");
            return prefab;
        }

        // 递归按名查找骨骼; 同名时优先返回无 Renderer 的纯骨骼 (避免命中 Mesh 对象)
        static Transform FindBone(Transform root, string name)
        {
            var matches = new List<Transform>();
            CollectNamed(root, name, matches);
            if (matches.Count == 0)
            {
                Debug.LogWarning($"[FBX] 骨骼未映射: {name}");
                return null;
            }
            foreach (var t in matches)
                if (t.GetComponent<Renderer>() == null) return t;
            return matches[0];
        }

        static void CollectNamed(Transform t, string name, List<Transform> outList)
        {
            if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                outList.Add(t);
            for (int i = 0; i < t.childCount; i++)
                CollectNamed(t.GetChild(i), name, outList);
        }

        static Transform CreateWeaponAnchor(Transform hand, string name)
        {
            if (hand == null) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(hand, false);
            go.transform.localScale = new Vector3(0.15f, 0.15f, 0.5f);
            go.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = GetOrCreateMat(new Color(0.2f, 0.2f, 0.25f));
            return go.transform;
        }

        static Bounds CombinedBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds();
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
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
                r.sharedMaterial = GetOrCreateMat(color);
            return go.transform;
        }

        static void BuildBattleScene(GameObject mechaPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var groundMat = new Material(Shader.Find("Standard"));
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

            // MotionPipeline (原生 pose: fightbotpose.aar 经 AndroidJavaObject 轮询; 不再用 Sentis)
            var pipeGo = new GameObject("MotionPipeline");
            var pipe = pipeGo.AddComponent<MotionPipeline>();
            ctrl.Pipeline = pipe;

            // Enemy 图层目标: 3 个 dummy
            for (int i = 0; i < 3; i++)
            {
                var enemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                enemy.name = $"Enemy_{i}";
                enemy.transform.position = new Vector3(UnityEngine.Random.Range(-3f, 3f), 0.75f, 5f + i * 2f);
                enemy.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
                enemy.layer = LayerMask.NameToLayer("Enemy");
                var emat = new Material(Shader.Find("Standard"));
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

            // 右上角骨骼小窗由原生 SkeletonOverlayView 绘制 (阶段2), Unity 侧不再画

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
