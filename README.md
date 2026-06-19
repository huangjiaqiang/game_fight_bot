# FightBot — 体感控制 3D 机甲

参照 [peppapig_jump](../peppapig_jump) 的体感控制流水线(前置摄像头 → MoveNet 17 关键点 → 状态机 → 游戏反馈),用 Unity 2022.3 + Unity Sentis 2.1 复刻并扩展为完整 3D 机甲战斗游戏:

- **左右倾斜** → 机甲左右移动
- **前倾/后仰** → 机甲前进/后退
- **抬左/右臂** → 左/右武器瞄准
- **出拳动作** → 攻击命中
- **双脚离地** → 跳跃
- **蹲下** → 减伤姿态

技术栈:Unity 2022.3 LTS (URP 14) + Unity Sentis 2.1.3 + MoveNet SinglePose Lightning (TFLite) + Android

> ⚠️ 版本说明:本机为中国区环境。中国区 Unity Hub / 团结引擎 Hub 能装到的最高版本是 **Unity 2022.3.62**(国际版 Unity 6 在中国区渠道拿不到)。
> 本工程已据此从 Unity 6 **降级到 Unity 2022.3 LTS**:**Sentis 2.1.3 保留**(其包元数据声明 `unity: 2022.3`,本就兼容 2022.3,无需降级),仅 **URP 17→14.0.12**、**ugui 2.0→1.0** 做了降级。`PoseDetector.cs` 维持原 Sentis 2.x 代码不变。仅个人自用,不发布国际市场。

## 一次性环境准备

1. 打开 `C:\Program Files\Unity Hub`(中国区版本,已安装)
2. Hub → 登录 Unity ID(没有就在 https://unity.cn/id 免费注册)
3. Hub → Installs → Install Editor → 选 **Unity 2022.3.62**(中国区 `c1` 版本,如 `2022.3.62f3c1`),右侧模块勾选:
   - **Android Build Support**(含 OpenJDK + Android SDK + NDK);若只在 Editor 里试跑可暂不勾
4. Hub → Projects → Add → Add project from disk → 选 `D:\workstation\game_fight_bot`
5. 首次打开会触发依赖下载(`com.unity.sentis 1.3.0` 等),等 Package Manager 跑完
   - 若 Hub 提示「项目要求 2022.3.62f1,与已装编辑器不符」,点选已装的 `2022.3.62f3c1` 打开即可(同属 2022.3.62,只需重建 Library)

## 项目内一次性配置(打开项目后做)

1. **导入 MoveNet 模型为 Sentis 资源**:
   - Project 视图定位到 `Assets/ML/Models/movenet_singlepose_lightning.tflite`
   - Inspector 里点 **Import Settings → Generate ModelAsset**(或直接右键 tflite → Sentis → Generate ModelAsset)
   - 会生成同名 `.asset` 文件,这是 `MotionPipeline.MoveNetModel` 要引用的对象

2. **一键生成场景/Prefab/Android 配置**:
   - 顶部菜单 → **FightBot → 1. Build All (Scenes + Prefab + Settings)**
   - 自动产出:
     - `Assets/Prefabs/Mecha.prefab`(程序化机甲)
     - `Assets/Scenes/Battle.unity`(主游戏)
     - `Assets/Scenes/Menu.unity`(占位主菜单)
     - Player Settings(`com.fightbot.mecha`,minSdk 24,targetSdk 34,landscape,IL2CPP ARM64)
     - Enemy 图层

3. **Build Settings**:
   - File → Build Settings → 添加 `Menu.unity` 和 `Battle.unity`(Menu 在前)
   - 平台切到 Android

4. **打开 `Battle.unity`**,把刚生成的 ModelAsset `.asset` 拖到场景里 `MotionPipeline` GameObject 的 `MoveNet Model` 字段(Bootstrap 脚本会自动尝试,如果没有再手动拖)。

## 试运行

- Editor 点 Play,前置摄像头会被请求授权
- 5 秒校准期保持自然站立
- 之后机甲会跟随你的身体动作

## 出 Android APK

1. File → Build Settings → Build(选择输出路径)
2. `adb install -r fightbot.apk`
3. `adb shell pm grant com.fightbot.mecha android.permission.CAMERA`
4. `adb shell am start -n com.fightbot.mecha/com.unity3d.player.UnityPlayerActivity`
5. 横屏握住设备,面对前置摄像头开始操作

## 调试

- 场景里的 `DebugBar` GameObject 会用 OnGUI 在屏幕左上角实时显示:
  - 推理 fps + 单帧耗时
  - JumpDetector 状态 + hipY/ankleY + baseline(火花线)
  - BodyIntent 各信号(lean/squat/arm/punch)
  - 机甲 HP
- 崩溃:`adb logcat -b crash -d | tail -50`
- 性能基线:推理 ≤ 33ms/帧(30fps),游戏帧 ≥ 60fps(中端机),摄像头延迟 ≤ 100ms

## 关键参数

| 想改什么 | 改哪里 |
|---|---|
| 推理后端(GPU/CPU) | `MotionPipeline.Backend`(Inspector) |
| 推理节流 | `MotionPipeline.MinInferIntervalMs`(代码常量,默认 33ms) |
| 跳跃灵敏度 | `JumpDetector.cs` 的 `AirThreshold` / `LandThreshold` / `CooldownMs` |
| 倾斜灵敏度 | `BodyIntent.cs` 的 `LeanThreshold` / `LeanMax` |
| 抬臂阈值 | `BodyIntent.cs` 的 `ArmRaiseThr` |
| 出拳速度阈值 | `BodyIntent.cs` 的 `PunchVelThr` |
| 蹲伏阈值 | `BodyIntent.cs` 的 `SquatThr` |
| 机甲移动速度 | `MechaController.MoveSpeed` / `ForwardSpeed` |
| 跳跃高度 | `MechaController.JumpHeight` |
| 攻击伤害/范围 | `MechaController.AttackDamage` / `AttackRange` |

## 项目结构

```
Assets/
├── ML/Models/movenet_singlepose_lightning.tflite   # 直接拷贝自 peppapig
├── Scripts/
│   ├── Motion/    Pose, PoseDetector, JumpDetector, BodyIntent, MotionPipeline
│   ├── Game/      MechaRig, MechaController, CameraRig, EnemyDummy, GameManager
│   ├── UI/        CalibrationUI, GameHUD, DebugBar
│   └── Editor/    SceneBootstrap (菜单项一键生成场景)
├── Prefabs/Mecha.prefab            # 由 SceneBootstrap 生成
├── Scenes/{Battle,Menu}.unity       # 由 SceneBootstrap 生成
├── Plugins/Android/AndroidManifest.xml  # CAMERA 权限 + landscape
└── csc.rsp                          # C# 编译选项
Packages/manifest.json              # 含 com.unity.sentis
```

## 设计要点(与 peppapig 的对照)

| Unity 实现 | peppapig 原型 | 复用要点 |
|---|---|---|
| `Pose.cs` | `motion/Pose.kt` | 17 关键点索引、hipCenterY、ankleCenterY、bodyHeight、isReliable |
| `PoseDetector.cs` | `motion/PoseDetector.kt` | 同一份 tflite 模型,输入 uint8 192×192×3,输出 [1,1,17,3] |
| `JumpDetector.cs` | `motion/JumpDetector.kt` | 4 状态机 + 所有阈值常量原样保留 |
| `MotionPipeline.cs` | `motion/CameraSource.kt` + `game/GameEngine.kt::handleCameraFrame` | 摄像头帧 + 33ms 节流 + 跨线程 Pose 同步 |
| `BodyIntent.cs` | (新增) | 在 Pose 之上扩展倾斜/抬臂/出拳/蹲伏 |
| `MechaController.cs` | `game/entity/PigSprite.kt` | 动画状态机 + 跳跃抛物线 380ms 节奏 |
| `DebugBar.cs` | `game/GameEngine.kt::debugFlow` | StateFlow 等价 → OnGUI 文本+spark |
| `CalibrationUI.cs` | (peppapig 隐式 baseline) | 把 IDLE 状态下的 EMA baseline 显式做成 5s 校准流程 |
