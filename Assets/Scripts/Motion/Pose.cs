using System;
using UnityEngine;

namespace FightBot.Motion
{
    public enum KeyPointType
    {
        Nose = 0,
        LeftEye = 1,
        RightEye = 2,
        LeftEar = 3,
        RightEar = 4,
        LeftShoulder = 5,
        RightShoulder = 6,
        LeftElbow = 7,
        RightElbow = 8,
        LeftWrist = 9,
        RightWrist = 10,
        LeftHip = 11,
        RightHip = 12,
        LeftKnee = 13,
        RightKnee = 14,
        LeftAnkle = 15,
        RightAnkle = 16
    }

    [Serializable]
    public struct KeyPoint
    {
        public float x;
        public float y;
        public float score;

        public KeyPoint(float x, float y, float score)
        {
            this.x = x;
            this.y = y;
            this.score = score;
        }
    }

    public class Pose
    {
        public const int KEYPOINT_COUNT = 17;
        public const float MinTorsoScore = 0.3f;

        public readonly KeyPoint[] keypoints;

        public Pose(KeyPoint[] keypoints)
        {
            this.keypoints = keypoints ?? throw new ArgumentNullException(nameof(keypoints));
        }

        public KeyPoint Nose => keypoints[(int)KeyPointType.Nose];
        public KeyPoint LeftEye => keypoints[(int)KeyPointType.LeftEye];
        public KeyPoint RightEye => keypoints[(int)KeyPointType.RightEye];
        public KeyPoint LeftEar => keypoints[(int)KeyPointType.LeftEar];
        public KeyPoint RightEar => keypoints[(int)KeyPointType.RightEar];
        public KeyPoint LeftShoulder => keypoints[(int)KeyPointType.LeftShoulder];
        public KeyPoint RightShoulder => keypoints[(int)KeyPointType.RightShoulder];
        public KeyPoint LeftElbow => keypoints[(int)KeyPointType.LeftElbow];
        public KeyPoint RightElbow => keypoints[(int)KeyPointType.RightElbow];
        public KeyPoint LeftWrist => keypoints[(int)KeyPointType.LeftWrist];
        public KeyPoint RightWrist => keypoints[(int)KeyPointType.RightWrist];
        public KeyPoint LeftHip => keypoints[(int)KeyPointType.LeftHip];
        public KeyPoint RightHip => keypoints[(int)KeyPointType.RightHip];
        public KeyPoint LeftKnee => keypoints[(int)KeyPointType.LeftKnee];
        public KeyPoint RightKnee => keypoints[(int)KeyPointType.RightKnee];
        public KeyPoint LeftAnkle => keypoints[(int)KeyPointType.LeftAnkle];
        public KeyPoint RightAnkle => keypoints[(int)KeyPointType.RightAnkle];

        public float HipCenterX => (LeftHip.x + RightHip.x) * 0.5f;
        public float HipCenterY => (LeftHip.y + RightHip.y) * 0.5f;
        public float ShoulderCenterY => (LeftShoulder.y + RightShoulder.y) * 0.5f;
        public float ShoulderCenterX => (LeftShoulder.x + RightShoulder.x) * 0.5f;
        public float AnkleCenterY => (LeftAnkle.y + RightAnkle.y) * 0.5f;

        public float HipScore => (LeftHip.score + RightHip.score) * 0.5f;
        public float ShoulderScore => (LeftShoulder.score + RightShoulder.score) * 0.5f;
        public float AnkleScore => (LeftAnkle.score + RightAnkle.score) * 0.5f;

        public bool IsReliable =>
            HipScore > MinTorsoScore && ShoulderScore > MinTorsoScore;

        public float BodyHeight => Mathf.Max(AnkleCenterY - HipCenterY, 0.1f);
    }
}
