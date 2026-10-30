using UnityEngine;

namespace SkeletonPose
{
    /// <summary>
    /// Walk cycle authored in Unity space, then stored as camera-space coordinates
    /// so the same conversion path used by the C detector is exercised.
    /// </summary>
    public static class DemoPoseSource
    {
        public static void Fill(PoseFrame frame, float time)
        {
            frame.Valid = true;
            frame.Space = PoseSpace.CameraYDown;
            frame.UnitScale = 1f;
            frame.TimestampMs = (uint)Mathf.Max(0f, time * 1000f);

            float w = time * 4.4f;
            float s = Mathf.Sin(w);
            float bob = 0.018f * Mathf.Cos(w * 2f);
            float z = 0.42f * Mathf.Sin(time * 0.55f);
            float hipY = 0.92f + bob;

            // Facing the camera: anatomical forward is -Z, anatomical left is +X.
            Vector3 hipL = new Vector3(0.11f + 0.02f * s, hipY, z);
            Vector3 hipR = new Vector3(-0.11f + 0.02f * s, hipY, z);
            Vector3 shoL = new Vector3(0.20f, 1.38f + bob, z);
            Vector3 shoR = new Vector3(-0.20f, 1.38f + bob, z);

            Vector3 ankleL = new Vector3(hipL.x, 0.07f + 0.06f * Mathf.Abs(s), z - 0.26f * s);
            Vector3 ankleR = new Vector3(hipR.x, 0.07f + 0.06f * Mathf.Abs(s), z + 0.26f * s);
            Vector3 kneeL = Knee(hipL, ankleL, s);
            Vector3 kneeR = Knee(hipR, ankleR, -s);

            float arm = s;
            Vector3 wristL = new Vector3(0.24f, 0.78f, z + 0.30f * arm);
            Vector3 wristR = new Vector3(-0.24f, 0.78f, z - 0.30f * arm);
            Vector3 elbowL = Elbow(shoL, wristL, 0.30f);
            Vector3 elbowR = Elbow(shoR, wristR, -0.30f);

            Vector3 head = new Vector3(0.015f * s, 1.64f + bob, z);
            Put(frame, JointId.Nose, head + new Vector3(0f, -0.02f, -0.09f));
            Put(frame, JointId.LeftEye, head + new Vector3(0.035f, 0.03f, -0.06f));
            Put(frame, JointId.RightEye, head + new Vector3(-0.035f, 0.03f, -0.06f));
            Put(frame, JointId.LeftEar, head + new Vector3(0.09f, 0.01f, 0.02f));
            Put(frame, JointId.RightEar, head + new Vector3(-0.09f, 0.01f, 0.02f));
            Put(frame, JointId.LeftShoulder, shoL);
            Put(frame, JointId.RightShoulder, shoR);
            Put(frame, JointId.LeftElbow, elbowL);
            Put(frame, JointId.RightElbow, elbowR);
            Put(frame, JointId.LeftWrist, wristL);
            Put(frame, JointId.RightWrist, wristR);
            Put(frame, JointId.LeftHip, hipL);
            Put(frame, JointId.RightHip, hipR);
            Put(frame, JointId.LeftKnee, kneeL);
            Put(frame, JointId.RightKnee, kneeR);
            Put(frame, JointId.LeftAnkle, ankleL);
            Put(frame, JointId.RightAnkle, ankleR);
        }

        static Vector3 Knee(Vector3 hip, Vector3 ankle, float phase)
        {
            Vector3 mid = Vector3.Lerp(hip, ankle, 0.52f);
            // Forward is -Z. Knees bend toward the camera, and more when the leg swings.
            mid.z -= 0.05f + 0.07f * Mathf.Abs(phase);
            mid.y = Mathf.Min(mid.y, 0.50f);
            return mid;
        }

        static Vector3 Elbow(Vector3 shoulder, Vector3 wrist, float outward)
        {
            Vector3 mid = Vector3.Lerp(shoulder, wrist, 0.5f);
            mid.x += outward > 0f ? 0.06f : -0.06f;
            // Elbows bend backward, which is +Z when the person faces the camera.
            mid.z += 0.07f;
            return mid;
        }

        static void Put(PoseFrame frame, JointId id, Vector3 unity)
        {
            int i = (int)id;
            frame.Position[i] = new Vector3(unity.x, -unity.y, unity.z);
            frame.Confidence[i] = 1f;
            frame.Tracked[i] = true;
        }
    }
}
