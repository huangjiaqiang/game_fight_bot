using FightBot.Motion;
using FightBot.Game;
using UnityEngine;

namespace FightBot.UI
{
    /// <summary>
    /// 调试条驱动 (文本 + 简易 spark line). 读 MotionPipeline + MechaController 状态.
    /// 对应 peppapig GameEngine.debugFlow + DebugBar.
    /// 挂在场景里的空 GameObject 上, Inspector 里指定 Pipeline/Mecha.
    /// </summary>
    public class DebugBar : MonoBehaviour
    {
        public MotionPipeline Pipeline;
        public MechaController Mecha;

        static readonly string[] Blocks = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        void OnGUI()
        {
            if (Pipeline == null) return;
            var p = Pipeline;

            string s = $"<b>infer</b> {p.InferenceFps}fps / {p.LastInferenceMs:F0}ms\n";
            if (p.JumpDetector != null)
            {
                s += $"<b>state</b>={p.JumpDetector.CurrentState}\n";
                s += $"hip  {p.JumpDetector.LastHipY:F3}  gnd={p.JumpDetector.GroundHipY:F3}  [{Spark(p.JumpDetector.HipHistory)}]\n";
                s += $"ank  {p.JumpDetector.LastAnkleY:F3}  gnd={p.JumpDetector.GroundAnkleY:F3}  [{Spark(p.JumpDetector.AnkleHistory)}]\n";
            }
            var bi = p.BodyIntent;
            s += $"<b>lean</b>({bi.LeanX:F2},{bi.LeanZ:F2}) squat={bi.Squat}\n";
            s += $"L arm={bi.LeftArmRaised} punch={bi.LeftPunchTriggered}  |  R arm={bi.RightArmRaised} punch={bi.RightPunchTriggered}";
            if (Mecha != null) s += $"\n<b>hp</b>={Mecha.CurrentHp}/{Mecha.MaxHp}";

            var style = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 14,
                normal = { textColor = Color.white }
            };
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(8f, 8f, 520f, 140f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14f, 12f, 520f, 140f), s, style);
        }

        string Spark(float[] hist)
        {
            const int outLen = 24;
            var sb = new System.Text.StringBuilder(outLen);
            int step = Mathf.Max(1, hist.Length / outLen);
            float lo = float.NaN, hi = float.NaN;
            for (int i = 0; i < hist.Length; i++)
            {
                float v = hist[i];
                if (float.IsNaN(v)) continue;
                if (float.IsNaN(lo) || v < lo) lo = v;
                if (float.IsNaN(hi) || v > hi) hi = v;
            }
            if (float.IsNaN(lo)) return new string(' ', outLen);
            float range = Mathf.Max(1e-4f, hi - lo);
            for (int i = 0; i < outLen; i++)
            {
                int srcIdx = i * step;
                if (srcIdx >= hist.Length) srcIdx = hist.Length - 1;
                float v = hist[srcIdx];
                if (float.IsNaN(v)) { sb.Append(' '); continue; }
                int bi2 = Mathf.Clamp(
                    Mathf.FloorToInt((v - lo) / range * (Blocks.Length - 1)),
                    0, Blocks.Length - 1);
                sb.Append(Blocks[bi2]);
            }
            return sb.ToString();
        }
    }
}
