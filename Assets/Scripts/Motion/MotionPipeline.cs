using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FightBot.Motion
{
    using FightBot.Game;

    /// <summary>
    /// 摄像头帧 → 推理 → 状态机 总线 (MonoBehaviour). 对应 peppapig 的 CameraSource + GameEngine.handleCameraFrame.
    ///
    /// 线程模型:
    /// - 主线程 Update() 拉 WebCamTexture 帧, 节流 33ms 后异步 Task.Run 调 PoseDetector.Detect
    /// - 后台线程写 latestPose, 主线程读
    /// - 主线程把 latestPose 喂给 JumpDetector + BodyIntent
    /// </summary>
    public class MotionPipeline : MonoBehaviour
    {
        [Tooltip("拖入 Assets/ML/Models/movenet_singlepose_lightning.tflite 对应的 ModelAsset")]
        public ModelAsset MoveNetModel;
        public BackendType Backend = BackendType.GPUCompute;

        public PoseDetector PoseDetector { get; private set; }
        public JumpDetector JumpDetector { get; private set; }
        public BodyIntent BodyIntent { get; } = new BodyIntent();

        WebCamTexture webcam;
        Color32[] rawPixels;
        Color32[] mirroredPixels;
        int lastFrameProcessed = -1;

        volatile Pose latestPose;
        volatile bool frameInFlight;
        long lastInferMs;
        const long MinInferIntervalMs = 33;

        public WebCamTexture Webcam => webcam;
        public Pose LatestPose => latestPose;
        public int InferenceFps { get; private set; }
        public float LastInferenceMs { get; private set; }
        long[] inferWindow = new long[16];
        int inferWindowHead;
        long lastInferWindowPrune;

        public bool IsRunning { get; private set; }
        public bool CameraAuthorized { get; private set; }

        /// <summary>JumpDetector 触发跳跃时回调 (intensity 0~1). 由外部 (GameManager) 订阅.</summary>
        public Action<float> OnJumpExternal;

        void Awake()
        {
            if (MoveNetModel != null)
            {
                PoseDetector = new PoseDetector(MoveNetModel, Backend);
                JumpDetector = new JumpDetector(intensity => OnJumpExternal?.Invoke(intensity));
                if (!PoseDetector.Available)
                    Debug.LogError("[MotionPipeline] MoveNet 模型加载失败, 推理不可用");
            }
            else
            {
                Debug.LogError("[MotionPipeline] MoveNetModel 未在 Inspector 中指定");
            }
        }

        void OnDestroy()
        {
            StopCamera();
            PoseDetector?.Dispose();
            if (webcam != null) Destroy(webcam);
        }

        public bool StartCamera(int requestWidth = 640, int requestHeight = 480)
        {
            if (IsRunning) return true;

            WebCamDevice[] devices = WebCamTexture.devices;
            int frontIdx = -1;
            for (int i = 0; i < devices.Length; i++)
                if (devices[i].isFrontFacing) { frontIdx = i; break; }
            if (frontIdx < 0 && devices.Length > 0) frontIdx = 0;
            if (frontIdx < 0)
            {
                Debug.LogError("[MotionPipeline] 未发现任何摄像头");
                return false;
            }

            webcam = new WebCamTexture(devices[frontIdx].name, requestWidth, requestHeight, 30);
            webcam.Play();
            IsRunning = true;
            CameraAuthorized = Application.HasUserAuthorization(UserAuthorization.WebCam);
            return true;
        }

        public void StopCamera()
        {
            if (webcam != null && webcam.isPlaying) webcam.Stop();
            IsRunning = false;
        }

        /// <summary>主线程每帧调用 (由 GameManager.Update 调): 抽样 + 异步推理 + 喂状态机.</summary>
        public void Tick(long nowMs)
        {
            if (IsRunning && webcam != null && webcam.didUpdateThisFrame
                && !frameInFlight && nowMs - lastInferMs >= MinInferIntervalMs
                && PoseDetector != null && PoseDetector.Available)
            {
                lastInferMs = nowMs;
                DispatchInference(nowMs);
            }

            Pose pose = latestPose;
            if (JumpDetector != null) JumpDetector.OnPose(pose, nowMs);
            BodyIntent.Update(pose, nowMs);

            UpdateFps(nowMs);
        }

        void DispatchInference(long ts)
        {
            int w = webcam.width;
            int h = webcam.height;
            if (w <= 0 || h <= 0) return;

            if (rawPixels == null || rawPixels.Length < w * h)
            {
                rawPixels = new Color32[w * h];
                mirroredPixels = new Color32[w * h];
            }
            webcam.GetPixels32(rawPixels);

            int rot = webcam.videoRotationAngle;
            int srcW = w, srcH = h;
            bool swap = (rot == 90 || rot == 270);

            MirrorH(rawPixels, srcW, srcH, mirroredPixels);
            Color32[] framed = mirroredPixels;
            if (swap)
            {
                framed = Rotate90CW(framed, srcW, srcH);
                int tmp = srcW; srcW = srcH; srcH = tmp;
            }

            frameInFlight = true;
            Color32[] snapshot = framed;
            int capW = srcW, capH = srcH;

            Task.Run(() =>
            {
                long t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Pose result = PoseDetector.Detect(snapshot, capW, capH);
                long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LastInferenceMs = t1 - t0;
                latestPose = (result != null && result.IsReliable) ? result : null;
                frameInFlight = false;
            });
        }

        static void MirrorH(Color32[] src, int w, int h, Color32[] dst)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    dst[row + (w - 1 - x)] = src[row + x];
            }
        }

        static Color32[] Rotate90CW(Color32[] src, int w, int h)
        {
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dst[(h - 1 - y) + x * h] = src[y * w + x];
            return dst;
        }

        void UpdateFps(long nowMs)
        {
            if (lastInferMs == 0) return;
            if (nowMs - lastInferWindowPrune > 1000)
            {
                int count = 0;
                long cutoff = nowMs - 1000;
                for (int i = 0; i < inferWindow.Length; i++)
                    if (inferWindow[i] > cutoff) count++;
                InferenceFps = count;
                lastInferWindowPrune = nowMs;
            }
            inferWindow[inferWindowHead] = lastInferMs;
            inferWindowHead = (inferWindowHead + 1) % inferWindow.Length;
        }

        public void ResetDetectors()
        {
            JumpDetector?.Reset();
            BodyIntent.Reset();
            latestPose = null;
        }
    }
}
