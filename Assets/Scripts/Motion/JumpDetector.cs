using System;
using FightBot.Motion;
using Pose = FightBot.Motion.Pose;
using UnityEngine;

namespace FightBot.Game
{
    /// <summary>
    /// 跳跃检测状态机 — 方案 A (双脚离地判据). 直接复刻 peppapig_jump/JumpDetector.kt.
    ///
    /// 状态流: IDLE → AIRBORNE → LAND → COOLDOWN → IDLE
    /// 主信号: ankleCenterY 取左右踝 y 较小值 (较高的脚).
    /// baseline: IDLE 状态下 EMA(α=0.05) 持续更新 groundAnkleY/groundHipY.
    /// 强度: (groundHipY - peakHipY) / bodyHeight, 取 [0.3, 1].
    /// </summary>
    public class JumpDetector
    {
        public enum State { Idle, Airborne, Land, Cooldown }

        public State CurrentState { get; private set; } = State.Idle;
        public float GroundAnkleY { get; private set; } = float.NaN;
        public float GroundHipY { get; private set; } = float.NaN;
        public float BodyHeightSmoothed { get; private set; } = float.NaN;
        public float LastAnkleY { get; private set; } = float.NaN;
        public float LastHipY { get; private set; } = float.NaN;
        public float LastJumpIntensity { get; private set; } = 0.5f;

        public readonly float[] AnkleHistory = new float[HistorySize];
        public readonly float[] HipHistory = new float[HistorySize];

        int groundSamples;
        long cooldownUntilMs;
        int historyWritePos;
        float peakHipY = float.NaN;

        readonly Action<float> onJump;

        public JumpDetector(Action<float> onJump)
        {
            this.onJump = onJump;
            for (int i = 0; i < HistorySize; i++)
            {
                AnkleHistory[i] = float.NaN;
                HipHistory[i] = float.NaN;
            }
        }

        public void OnPose(Pose pose, long nowMs)
        {
            if (pose == null || !pose.IsReliable) return;
            if (pose.AnkleScore < 0.3f) return;

            float leftAy = pose.LeftAnkle.y;
            float rightAy = pose.RightAnkle.y;
            float hipY = pose.HipCenterY;
            float height = pose.BodyHeight;
            float ankleY = Math.Min(leftAy, rightAy);

            BodyHeightSmoothed = float.IsNaN(BodyHeightSmoothed)
                ? height
                : BodyHeightSmoothed * 0.9f + height * 0.1f;

            AnkleHistory[historyWritePos] = ankleY;
            HipHistory[historyWritePos] = hipY;
            historyWritePos = (historyWritePos + 1) % HistorySize;

            // baseline 初始化
            if (float.IsNaN(GroundAnkleY) || groundSamples < GroundInitFrames)
            {
                GroundAnkleY = float.IsNaN(GroundAnkleY) ? ankleY
                    : GroundAnkleY + (ankleY - GroundAnkleY) / (groundSamples + 1);
                GroundHipY = float.IsNaN(GroundHipY) ? hipY
                    : GroundHipY + (hipY - GroundHipY) / (groundSamples + 1);
                groundSamples++;
                LastAnkleY = ankleY;
                LastHipY = hipY;
                return;
            }

            // IDLE 下 EMA 更新 baseline (适应玩家轻微移动)
            if (CurrentState == State.Idle)
            {
                GroundAnkleY += (ankleY - GroundAnkleY) * GroundAlpha;
                GroundHipY += (hipY - GroundHipY) * GroundAlpha;
            }

            switch (CurrentState)
            {
                case State.Idle:
                    {
                        float airT = GroundAnkleY - AirThreshold;
                        if (leftAy < airT && rightAy < airT)
                        {
                            CurrentState = State.Airborne;
                            peakHipY = hipY;
                        }
                        break;
                    }
                case State.Airborne:
                    {
                        if (hipY < peakHipY || float.IsNaN(peakHipY)) peakHipY = hipY;
                        float landT = GroundAnkleY - LandThreshold;
                        if (leftAy > landT || rightAy > landT)
                        {
                            CurrentState = State.Land;
                        }
                        break;
                    }
                case State.Land:
                    {
                        float lift = !float.IsNaN(peakHipY) ? (GroundHipY - peakHipY) : 0f;
                        LastJumpIntensity = Mathf.Clamp(lift / BodyHeightSmoothed, 0.3f, 1f);
                        onJump?.Invoke(LastJumpIntensity);
                        cooldownUntilMs = nowMs + CooldownMs;
                        CurrentState = State.Cooldown;
                        break;
                    }
                case State.Cooldown:
                    {
                        if (nowMs >= cooldownUntilMs)
                        {
                            float airT = GroundAnkleY - AirThreshold;
                            if (leftAy >= airT || rightAy >= airT)
                            {
                                CurrentState = State.Idle;
                            }
                        }
                        break;
                    }
            }

            LastAnkleY = ankleY;
            LastHipY = hipY;
        }

        public void Reset()
        {
            CurrentState = State.Idle;
            GroundAnkleY = float.NaN;
            GroundHipY = float.NaN;
            BodyHeightSmoothed = float.NaN;
            LastAnkleY = float.NaN;
            LastHipY = float.NaN;
            peakHipY = float.NaN;
            groundSamples = 0;
            cooldownUntilMs = 0;
            historyWritePos = 0;
            for (int i = 0; i < HistorySize; i++)
            {
                AnkleHistory[i] = float.NaN;
                HipHistory[i] = float.NaN;
            }
        }

        const int GroundInitFrames = 30;
        const float GroundAlpha = 0.05f;
        const float AirThreshold = 0.05f;
        const float LandThreshold = 0.02f;
        const long CooldownMs = 200;
        const int HistorySize = 60;
    }
}
