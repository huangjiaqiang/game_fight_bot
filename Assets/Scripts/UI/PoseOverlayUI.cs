using FightBot.Motion;
using Pose = FightBot.Motion.Pose;
using UnityEngine;

namespace FightBot.UI
{
    /// <summary>
    /// 右上角画中画: 显示 pipeline 推理用的那张帧 (ProcessedFrame, 已镜像+旋转) + 17 关键点骨骼线.
    /// 对应 game_jumping_in_muddle_paddle 的 GameEngine.drawCameraPip + drawSingleSkeleton.
    /// 原则 (与参考项目一致): 显示帧 == 推理帧 => 骨骼与人体坐标系一致, 天然对齐.
    /// 不再用 GUI 矩阵去模拟 pipeline 的变换, 直接显示 pipeline 已经处理好的 ProcessedFrame.
    /// </summary>
    public class PoseOverlayUI : MonoBehaviour
    {
        public MotionPipeline Pipeline;

        [Range(0.12f, 0.4f)] public float PipWidthRatio = 0.22f;
        public float PipAspect = 0.75f;     // 4:3
        public float MarginRatio = 0.03f;

        // MoveNet 17 关键点标准骨架连接
        static readonly int[][] Bones =
        {
            new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 2, 4 },                       // 头
            new[] { 0, 5 }, new[] { 0, 6 },                                                       // 颈
            new[] { 5, 7 }, new[] { 7, 9 }, new[] { 6, 8 }, new[] { 8, 10 },                      // 手臂
            new[] { 5, 6 }, new[] { 5, 11 }, new[] { 6, 12 }, new[] { 11, 12 },                  // 躯干
            new[] { 11, 13 }, new[] { 13, 15 }, new[] { 12, 14 }, new[] { 14, 16 },              // 腿
        };

        static readonly Color LineColor = new Color(1f, 0.5f, 0.2f, 0.95f);
        static readonly Color HipColor = new Color(1f, 0.85f, 0f, 1f);
        static readonly Color KpGood = new Color(0.3f, 1f, 0.5f, 1f);
        static readonly Color KpLow = new Color(1f, 0.7f, 0.3f, 1f);

        static Texture2D _whiteTex;
        static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }
                return _whiteTex;
            }
        }

        void OnGUI()
        {
            if (Pipeline == null || Pipeline.ProcessedFrame == null) return;
            var tex = Pipeline.ProcessedFrame;
            if (tex.width <= 0 || tex.height <= 0) return;

            float sw = Screen.width, sh = Screen.height;
            float pipW = sw * PipWidthRatio;
            float pipH = pipW * PipAspect;
            float margin = sh * MarginRatio;
            Rect pip = new Rect(sw - pipW - margin, margin, pipW, pipH); // 右上角

            // 半透明黑底
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(pip, WhiteTex);
            GUI.color = Color.white;

            // contain 推理帧进 pip (留边). 骨骼映射到同一 disp => 对齐.
            float s = Mathf.Min(pip.width / tex.width, pip.height / tex.height);
            float dispW = tex.width * s, dispH = tex.height * s;
            Rect disp = new Rect(pip.x + (pip.width - dispW) * 0.5f, pip.y + (pip.height - dispH) * 0.5f, dispW, dispH);
            GUI.DrawTexture(disp, tex);   // 已镜像+旋转好的推理帧, 直接显示

            // 骨骼 (pose 与 tex 同源, 映射到 disp)
            Pose pose = Pipeline.LatestPose;
            if (pose != null) DrawSkeleton(pose, disp);

            // 白色描边 (围绕 pip)
            float fw = Mathf.Max(1.5f, pipW * 0.006f);
            DrawLine(new Vector2(pip.x, pip.y), new Vector2(pip.xMax, pip.y), Color.white, fw);
            DrawLine(new Vector2(pip.xMax, pip.y), new Vector2(pip.xMax, pip.yMax), Color.white, fw);
            DrawLine(new Vector2(pip.xMax, pip.yMax), new Vector2(pip.x, pip.yMax), Color.white, fw);
            DrawLine(new Vector2(pip.x, pip.yMax), new Vector2(pip.x, pip.y), Color.white, fw);
        }

        void DrawSkeleton(Pose pose, Rect disp)
        {
            var kp = pose.keypoints;
            // pose.y 与 Texture2D 行序一致; 显示帧经 GUI.DrawTexture 直立显示, 故纵轴需翻转 (图像 y 向下).
            Vector2 P(int i) => new Vector2(disp.x + kp[i].x * disp.width, disp.yMax - kp[i].y * disp.height);
            bool Vis(int i) => kp[i].score > 0.3f;

            float lineW = Mathf.Max(1.5f, disp.width * 0.013f);
            foreach (var b in Bones)
                if (Vis(b[0]) && Vis(b[1]))
                    DrawLine(P(b[0]), P(b[1]), LineColor, lineW);

            for (int i = 0; i < kp.Length; i++)
            {
                if (kp[i].score <= 0.3f) continue;
                Color c = (i == 11 || i == 12) ? HipColor : (kp[i].score > 0.6f ? KpGood : KpLow);
                float r = (i == 11 || i == 12) ? disp.width * 0.022f : disp.width * 0.014f;
                DrawDot(P(i), r, c);
            }
        }

        static void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float len = Vector2.Distance(a, b);
            Matrix4x4 saved = GUI.matrix;
            Color savedCol = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), WhiteTex);
            GUI.matrix = saved;
            GUI.color = savedCol;
        }

        static void DrawDot(Vector2 p, float r, Color c)
        {
            Color saved = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2, r * 2), WhiteTex);
            GUI.color = saved;
        }
    }
}
