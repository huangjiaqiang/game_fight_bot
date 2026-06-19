using FightBot.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace FightBot.UI
{
    /// <summary>
    /// 顶部 HUD: 分数 / 血量 / 状态消息 / 调试信息.
    /// 对应 peppapig GameScreen.kt 顶部状态条 + DebugBar.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("Refs")]
        public Text ScoreText;
        public Text HpText;
        public Image HpBar;
        public Text MessageText;
        public GameObject DebugGroup;
        public Text DebugStateText;
        public Text DebugSparkHip;
        public Text DebugSparkAnkle;
        public Text DebugFpsText;

        bool showDebug = true;

        public void SetScore(int score)
        {
            if (ScoreText != null) ScoreText.text = $"击破: {score}";
        }

        public void SetHp(int cur, int max)
        {
            if (HpText != null) HpText.text = $"{cur} / {max}";
            if (HpBar != null) HpBar.fillAmount = Mathf.Clamp01((float)cur / max);
        }

        public void SetMessage(string msg)
        {
            if (MessageText != null) MessageText.text = msg ?? string.Empty;
        }

        public void SetDebug(string state, float hipY, float ankleY, float groundY, int fps, float lastInferMs, Pose pose)
        {
            if (!showDebug || DebugGroup == null) return;
            if (!DebugGroup.activeSelf) DebugGroup.SetActive(true);

            if (DebugStateText != null)
            {
                string poseInfo = pose != null ? "OK" : "—";
                DebugStateText.text = $"state={state}  pose={poseInfo}  infer={lastInferMs:F0}ms";
            }
            if (DebugSparkHip != null)
            {
                string hipSpark = !float.IsNaN(hipY) ? Sparkline(hipY, 0.2f, 0.8f) : "—";
                string gndSpark = !float.IsNaN(groundY) ? Sparkline(groundY, 0.2f, 0.8f) : "—";
                DebugSparkHip.text = $"hip {hipY:F3} [{hipSpark}]\ngnd {groundY:F3} [{gndSpark}]";
            }
            if (DebugSparkAnkle != null)
            {
                string ankSpark = !float.IsNaN(ankleY) ? Sparkline(ankleY, 0.2f, 1f) : "—";
                DebugSparkAnkle.text = $"ank {ankleY:F3} [{ankSpark}]";
            }
            if (DebugFpsText != null) DebugFpsText.text = $"infer fps = {fps}";
        }

        public void ToggleDebug()
        {
            showDebug = !showDebug;
            if (DebugGroup != null) DebugGroup.SetActive(showDebug);
        }

        // 简易文本火花线: 把 v 在 [lo, hi] 区间映射到 16 个 ASCII 块字符
        static string Sparkline(float v, float lo, float hi)
        {
            const string blocks = " ▁▂▃▄▅▆▇█";
            float t = Mathf.InverseLerp(lo, hi, v);
            int idx = Mathf.Clamp(Mathf.FloorToInt(t * (blocks.Length - 1)), 0, blocks.Length - 1);
            return blocks[idx].ToString();
        }
    }
}
