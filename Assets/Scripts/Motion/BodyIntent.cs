using System;
using UnityEngine;

namespace FightBot.Motion
{
    /// <summary>
    /// 解析单个 Pose → 玩家身体意图(倾斜/抬臂/出拳/蹲伏). 供 MechaController 消费.
    /// 比 peppapig (只有 jump) 多扩展的全套机甲控制信号源.
    /// </summary>
    public class BodyIntent
    {
        // ---- 输出 ----
        /// <summary>左右倾斜 [-1, 1]: 负=左, 正=右, 0=中</summary>
        public float LeanX { get; private set; }
        /// <summary>前倾/后仰 [-1, 1]: 正=前倾 (移动), 负=后仰 (后退)</summary>
        public float LeanZ { get; private set; }
        public bool LeftArmRaised { get; private set; }
        public bool RightArmRaised { get; private set; }
        public bool LeftPunchTriggered { get; private set; }
        public bool RightPunchTriggered { get; private set; }
        public bool Squat { get; private set; }

        // ---- 校准中性位置(由 CalibrationUI 写入) ----
        public float NeutralHipX { get; set; } = 0.5f;
        public float NeutralHipY { get; set; } = 0.5f;
        public float NeutralTorsoLen { get; set; } = 0.3f;  // shoulder.y - hip.y
        public bool Calibrated { get; set; }

        // ---- 内部历史(出拳速度检测) ----
        float prevLeftWristX = float.NaN;
        float prevRightWristX = float.NaN;
        int leftPunchStreak;
        int rightPunchStreak;
        long lastLeftPunchMs;
        long lastRightPunchMs;

        const float LeanThreshold = 0.05f;
        const float LeanMax = 0.20f;
        const float ArmRaiseThr = 0.05f;
        const float PunchVelThr = 0.30f;
        const int PunchFrames = 3;
        const long PunchCooldownMs = 400;
        const float SquatThr = 0.08f;

        public void Reset()
        {
            LeanX = 0;
            LeanZ = 0;
            LeftArmRaised = false;
            RightArmRaised = false;
            LeftPunchTriggered = false;
            RightPunchTriggered = false;
            Squat = false;
            prevLeftWristX = float.NaN;
            prevRightWristX = float.NaN;
            leftPunchStreak = 0;
            rightPunchStreak = 0;
        }

        /// <summary>
        /// 喂一帧 Pose, 更新所有意图信号.
        /// </summary>
        public void Update(Pose pose, long nowMs)
        {
            // 每帧重置触发型信号 (Punch),由本方法内重新决定
            LeftPunchTriggered = false;
            RightPunchTriggered = false;

            if (pose == null || !pose.IsReliable)
            {
                // 信号丢失时平滑衰减
                LeanX *= 0.5f;
                LeanZ *= 0.5f;
                LeftArmRaised = false;
                RightArmRaised = false;
                Squat = false;
                return;
            }

            float hipX = pose.HipCenterX;
            float hipY = pose.HipCenterY;
            float shoulderY = pose.ShoulderCenterY;

            // 左右倾斜: 镜像坐标系 (前置摄像头已 mirror, 玩家右移 → hipX 增大)
            float dx = hipX - NeutralHipX;
            LeanX = Mathf.Clamp(
                Mathf.Abs(dx) < LeanThreshold ? 0f : dx / LeanMax,
                -1f, 1f);

            // 前后倾: 躯干长度比中性短 = 前倾 (肩膀前压, 玩家低头)
            float torsoLen = shoulderY - hipY;
            float torsoDelta = NeutralTorsoLen - torsoLen;
            LeanZ = Mathf.Clamp(
                Mathf.Abs(torsoDelta) < LeanThreshold * 0.5f ? 0f : torsoDelta / LeanMax,
                -1f, 1f);

            // 抬臂
            LeftArmRaised = pose.LeftWrist.y < pose.LeftShoulder.y - ArmRaiseThr
                && pose.LeftWrist.score > 0.3f;
            RightArmRaised = pose.RightWrist.y < pose.RightShoulder.y - ArmRaiseThr
                && pose.RightWrist.score > 0.3f;

            // 蹲伏 (hip 比中性下移)
            Squat = hipY - NeutralHipY > SquatThr;

            // 出拳: wrist X 单帧位移 > PunchVelThr, 连续 PunchFrames 帧, 且冷却结束
            if (!float.IsNaN(prevLeftWristX))
            {
                float lv = pose.LeftWrist.x - prevLeftWristX;
                if (Mathf.Abs(lv) > PunchVelThr && pose.LeftWrist.score > 0.3f)
                {
                    leftPunchStreak++;
                    if (leftPunchStreak >= PunchFrames && nowMs - lastLeftPunchMs > PunchCooldownMs)
                    {
                        LeftPunchTriggered = true;
                        lastLeftPunchMs = nowMs;
                        leftPunchStreak = 0;
                    }
                }
                else
                {
                    leftPunchStreak = 0;
                }
            }
            prevLeftWristX = pose.LeftWrist.x;

            if (!float.IsNaN(prevRightWristX))
            {
                float rv = pose.RightWrist.x - prevRightWristX;
                if (Mathf.Abs(rv) > PunchVelThr && pose.RightWrist.score > 0.3f)
                {
                    rightPunchStreak++;
                    if (rightPunchStreak >= PunchFrames && nowMs - lastRightPunchMs > PunchCooldownMs)
                    {
                        RightPunchTriggered = true;
                        lastRightPunchMs = nowMs;
                        rightPunchStreak = 0;
                    }
                }
                else
                {
                    rightPunchStreak = 0;
                }
            }
            prevRightWristX = pose.RightWrist.x;
        }
    }
}
