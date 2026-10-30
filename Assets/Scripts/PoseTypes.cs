using System;
using UnityEngine;

namespace SkeletonPose
{
    /// <summary>
    /// COCO 17 keypoint order. "Left" is the person's left, not the left side of the image.
    /// </summary>
    public enum JointId
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

    public enum PoseSpace
    {
        UnityYUp = 0,
        CameraYDown = 1
    }

    /// <summary>How to interpret an incoming packet when the inspector overrides it.</summary>
    public enum SpaceMode
    {
        UsePacket = 0,
        CameraYDown = 1,
        UnityYUp = 2
    }

    public static class PoseProtocol
    {
        public const byte Magic0 = (byte)'P';
        public const byte Magic1 = (byte)'3';
        public const byte Magic2 = (byte)'D';
        public const byte Magic3 = (byte)'1';
        public const ushort Version = 1;
        public const int JointCount = 17;
        public const int Body25Count = 25;
        public const int HeaderSize = 16;
        public const ushort FlagCameraYDown = 1;
        public const ushort FlagMillimeters = 2;

        public static readonly string[] Names =
        {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };
    }

    public sealed class PoseFrame
    {
        public readonly Vector3[] Position = new Vector3[PoseProtocol.JointCount];
        public readonly float[] Confidence = new float[PoseProtocol.JointCount];
        public readonly bool[] Tracked = new bool[PoseProtocol.JointCount];
        public uint TimestampMs;
        public PoseSpace Space = PoseSpace.CameraYDown;
        public float UnitScale = 1f;
        public bool Valid;

        public void CopyFrom(PoseFrame other)
        {
            Array.Copy(other.Position, Position, PoseProtocol.JointCount);
            Array.Copy(other.Confidence, Confidence, PoseProtocol.JointCount);
            Array.Copy(other.Tracked, Tracked, PoseProtocol.JointCount);
            TimestampMs = other.TimestampMs;
            Space = other.Space;
            UnitScale = other.UnitScale;
            Valid = other.Valid;
        }
    }

    /// <summary>
    /// One solved pose. Joint positions are in torso units: the hip center is the
    /// origin on the calibration frame, and the hip-to-shoulder distance is 1.
    /// Directions are therefore independent of pixels, millimeters, or meters.
    /// </summary>
    public sealed class SolvedBody
    {
        public readonly Vector3[] World = new Vector3[PoseProtocol.JointCount];
        public readonly bool[] Tracked = new bool[PoseProtocol.JointCount];
        public Vector3 HipCenter;
        public Vector3 ShoulderCenter;
        public Vector3 NeckPoint;
        public Vector3 HeadPoint;
        public Vector3 Up = Vector3.up;
        public Vector3 Forward = Vector3.forward;
        public Vector3 Right = Vector3.right;
        public Vector3 HipsDelta;
        public bool Valid;
        public bool LeftArm;
        public bool RightArm;
        public bool LeftLeg;
        public bool RightLeg;
        public string RejectReason = "";

        public Vector3 Joint(JointId id)
        {
            return World[(int)id];
        }

        public void CopyYawTo(SolvedBody dst, float yawDegrees)
        {
            dst.Valid = Valid;
            dst.LeftArm = LeftArm;
            dst.RightArm = RightArm;
            dst.LeftLeg = LeftLeg;
            dst.RightLeg = RightLeg;
            dst.RejectReason = RejectReason;
            Array.Copy(Tracked, dst.Tracked, PoseProtocol.JointCount);
            if (!Valid)
                return;

            if (Mathf.Abs(yawDegrees) < 0.001f)
            {
                Array.Copy(World, dst.World, PoseProtocol.JointCount);
                dst.HipCenter = HipCenter;
                dst.ShoulderCenter = ShoulderCenter;
                dst.NeckPoint = NeckPoint;
                dst.HeadPoint = HeadPoint;
                dst.Up = Up;
                dst.Forward = Forward;
                dst.Right = Right;
                dst.HipsDelta = HipsDelta;
                return;
            }

            Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            for (int i = 0; i < PoseProtocol.JointCount; i++)
                dst.World[i] = yaw * World[i];
            dst.HipCenter = yaw * HipCenter;
            dst.ShoulderCenter = yaw * ShoulderCenter;
            dst.NeckPoint = yaw * NeckPoint;
            dst.HeadPoint = yaw * HeadPoint;
            dst.Up = yaw * Up;
            dst.Forward = yaw * Forward;
            dst.Right = yaw * Right;
            dst.HipsDelta = yaw * HipsDelta;
        }
    }
}
