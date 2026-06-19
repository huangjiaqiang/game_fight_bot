using System;
using FightBot.Game;
using UnityEngine;

namespace FightBot.Motion
{
    /// <summary>
    /// 原生 pose 源 (替代 Sentis 推理). 通过 AndroidJavaObject 调用 fightbotpose.aar 里的
    /// com.fightbot.pose.PosePlugin (CameraX + TFLite, 后台线程推理), 每帧轮询 getLatestPose().
    ///
    /// 保持类名/公开成员与原 Sentis 版一致 (LatestPose/BodyIntent/JumpDetector/OnJumpExternal/
    /// Tick/StartCamera/StopCamera/InferenceFps...), 下游 MechaController/GameManager/DebugBar 零改动.
    /// Editor 无原生, Tick 不产出 pose (仅真机).
    /// </summary>
    public class MotionPipeline : MonoBehaviour
    {
        public BodyIntent BodyIntent { get; } = new BodyIntent();
        public JumpDetector JumpDetector { get; private set; }
        public Action<float> OnJumpExternal;

        public Pose LatestPose { get; private set; }
        public bool IsRunning { get; private set; }
        public int InferenceFps { get; private set; }
        public float LastInferenceMs { get; private set; }

        // 原生摄像头/骨骼都在原生侧, Unity 这里没有 (CalibrationUI/PoseOverlayUI 守卫 null 用)
        public WebCamTexture Webcam => null;
        public Texture2D ProcessedFrame => null;

        // 原生插件单例 (Kotlin object PosePlugin 的 INSTANCE)
        AndroidJavaObject pluginInstance;

        void Awake()
        {
            JumpDetector = new JumpDetector(intensity => OnJumpExternal?.Invoke(intensity));
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var jc = new AndroidJavaClass("com.fightbot.pose.PosePlugin");
                pluginInstance = jc.GetStatic<AndroidJavaObject>("INSTANCE");
            }
            catch (Exception e) { Debug.LogError("[MotionPipeline] 获取原生插件失败: " + e); }
#endif
        }

        void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { pluginInstance?.Call("stop"); } catch { }
#endif
        }

        public bool StartCamera(int requestWidth = 640, int requestHeight = 480)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (pluginInstance == null) { Debug.LogError("[MotionPipeline] 插件未就绪"); return false; }
            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                pluginInstance.Call("start", activity);
                IsRunning = true;
                return true;
            }
            catch (Exception e) { Debug.LogError("[MotionPipeline] start 失败: " + e); return false; }
#else
            return false; // Editor 无原生
#endif
        }

        public void StopCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { pluginInstance?.Call("stop"); } catch { }
#endif
            IsRunning = false;
        }

        public void Tick(long nowMs)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (pluginInstance != null)
            {
                try
                {
                    var arr = pluginInstance.Call<float[]>("getLatestPose");
                    if (arr != null && arr.Length == Pose.KEYPOINT_COUNT * 3)
                    {
                        var kps = new KeyPoint[Pose.KEYPOINT_COUNT];
                        for (int i = 0; i < Pose.KEYPOINT_COUNT; i++)
                            kps[i] = new KeyPoint(arr[i * 3], arr[i * 3 + 1], arr[i * 3 + 2]);
                        LatestPose = new Pose(kps);
                    }
                    InferenceFps = Mathf.RoundToInt(pluginInstance.Call<float>("getInferenceFps"));
                    LastInferenceMs = 0f; // 原生后台异步推理, 主线程不阻塞
                }
                catch (Exception e) { Debug.LogError("[MotionPipeline] poll 失败: " + e); }
            }
#endif
            Pose p = LatestPose;
            if (JumpDetector != null) JumpDetector.OnPose(p, nowMs);
            BodyIntent.Update(p, nowMs);
        }

        public void ResetDetectors()
        {
            JumpDetector?.Reset();
            BodyIntent.Reset();
            LatestPose = null;
        }
    }
}
