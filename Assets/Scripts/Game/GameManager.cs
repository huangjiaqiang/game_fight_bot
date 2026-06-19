using System.Collections;
using FightBot.Motion;
using UnityEngine;

namespace FightBot.Game
{
    using FightBot.UI;

    /// <summary>
    /// 游戏总控. 持有 MotionPipeline + MechaController + CameraRig + UI,
    /// 负责启动/暂停/重置 + 校准流程 + 计分.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Refs")]
        public MotionPipeline Pipeline;
        public MechaController Mecha;
        public CameraRig CameraRig;
        public GameHUD Hud;
        public CalibrationUI Calibration;

        [Header("Score")]
        public int Score = 0;
        public int KillsTarget = 5;

        [Header("Calibration")]
        public float CalibrationDuration = 5f;

        public bool GameRunning { get; private set; }

        void Start()
        {
            // 默认进入校准阶段
            if (Hud != null) Hud.SetMessage("正在初始化...");
            StartCoroutine(BootRoutine());
        }

        IEnumerator BootRoutine()
        {
            // 1. 申请权限 + 启动摄像头
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }
            bool ok = Pipeline.StartCamera();
            if (!ok)
            {
                if (Hud != null) Hud.SetMessage("摄像头启动失败. 请检查权限.");
                yield break;
            }
            if (Hud != null) Hud.SetMessage("摄像头已就绪");

            // 2. 校准
            if (Calibration != null)
            {
                yield return Calibration.Run(Pipeline, CalibrationDuration);
                Pipeline.BodyIntent.Calibrated = true;
            }
            if (Hud != null) Hud.SetMessage("准备开始!");

            // 3. 开始
            // 注: MechaController 已在 OnEnable 时订阅 Pipeline.OnJumpExternal
            GameRunning = true;
            if (Hud != null) Hud.SetMessage(string.Empty);
        }

        void Update()
        {
            if (!GameRunning) return;
            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Pipeline.Tick(nowMs);

            // 同步状态到 HUD
            if (Hud != null)
            {
                Hud.SetScore(Score);
                Hud.SetHp(Mecha.CurrentHp, Mecha.MaxHp);
                Hud.SetDebug(
                    state: Pipeline.JumpDetector.CurrentState.ToString(),
                    hipY: Pipeline.JumpDetector.LastHipY,
                    ankleY: Pipeline.JumpDetector.LastAnkleY,
                    groundY: Pipeline.JumpDetector.GroundAnkleY,
                    fps: Pipeline.InferenceFps,
                    lastInferMs: Pipeline.LastInferenceMs,
                    pose: Pipeline.LatestPose);
            }
        }

        public void AddScore(int delta)
        {
            Score += delta;
        }

        public void Pause()
        {
            GameRunning = false;
            Pipeline.StopCamera();
        }

        public void Resume()
        {
            GameRunning = true;
            Pipeline.StartCamera();
        }
    }
}
