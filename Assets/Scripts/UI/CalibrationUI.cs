using System.Collections;
using FightBot.Motion;
using Pose = FightBot.Motion.Pose;
using UnityEngine;
using UnityEngine.UI;

namespace FightBot.UI
{
    /// <summary>
    /// 中性位置校准. 在 CalibrationDuration 秒内采集玩家站立自然姿态, 计算中性 hipX/hipY/躯干长,
    /// 写入 BodyIntent.NeutralHipX/Y/NeutralTorsoLen.
    /// 期间屏幕显示倒计时 + 骨架预览.
    /// </summary>
    public class CalibrationUI : MonoBehaviour
    {
        [Header("Refs")]
        public CanvasGroup Group;
        public Text CountdownText;
        public Text HintText;
        public RawImage CameraPreview;     // 用 WebCamTexture 直接显示摄像头预览
        public RectTransform SkeletonLayer; // 在上面用 LineRenderer / GL 绘制骨架

        /// <summary>由 GameManager 协程调用. 完成后 BodyIntent.Calibrated = true.</summary>
        public IEnumerator Run(MotionPipeline pipeline, float duration)
        {
            if (Group != null) Group.alpha = 1f;
            if (Group != null) Group.blocksRaycasts = true;

            // 把摄像头预览接到 UI
            if (CameraPreview != null && pipeline.Webcam != null)
            {
                CameraPreview.texture = pipeline.Webcam;
                CameraPreview.rectTransform.localScale = new Vector3(-1f, 1f, 1f); // 镜像
            }

            // 累加平均
            float sumHipX = 0f, sumHipY = 0f, sumTorso = 0f;
            int samples = 0;
            float endTime = Time.time + duration;
            while (Time.time < endTime)
            {
                float remain = Mathf.CeilToInt(endTime - Time.time);
                if (CountdownText != null) CountdownText.text = remain.ToString();
                Pose p = pipeline.LatestPose;
                if (p != null && p.IsReliable)
                {
                    sumHipX += p.HipCenterX;
                    sumHipY += p.HipCenterY;
                    sumTorso += (p.ShoulderCenterY - p.HipCenterY);
                    samples++;
                }
                if (HintText != null)
                {
                    HintText.text = samples > 0
                        ? "请保持自然站立\n面向摄像头, 双手垂于身侧"
                        : "未检测到完整姿态, 请站到摄像头正前方";
                }
                yield return null;
            }

            if (samples > 0)
            {
                pipeline.BodyIntent.NeutralHipX = sumHipX / samples;
                pipeline.BodyIntent.NeutralHipY = sumHipY / samples;
                pipeline.BodyIntent.NeutralTorsoLen = sumTorso / samples;
                pipeline.BodyIntent.Calibrated = true;
                Debug.Log($"[Calibration] neutral hipX={pipeline.BodyIntent.NeutralHipX:F3} " +
                          $"hipY={pipeline.BodyIntent.NeutralHipY:F3} " +
                          $"torso={pipeline.BodyIntent.NeutralTorsoLen:F3}");
            }
            else
            {
                Debug.LogWarning("[Calibration] 校准期间未采集到任何有效姿态, 使用默认中性值");
                pipeline.BodyIntent.Calibrated = true;
            }

            if (Group != null)
            {
                Group.alpha = 0f;
                Group.blocksRaycasts = false;
            }
        }
    }
}
