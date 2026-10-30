using UnityEngine;

namespace SkeletonPose
{
    public static class PoseMath
    {
        public static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        public static bool IsFinite(Quaternion q)
        {
            if (!IsFinite(q.x) || !IsFinite(q.y) || !IsFinite(q.z) || !IsFinite(q.w))
                return false;
            float m = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return m > 1e-8f && IsFinite(m);
        }

        public static Vector3 Mid(Vector3 a, Vector3 b)
        {
            return (a + b) * 0.5f;
        }

        /// <summary>
        /// Rotation whose +Y axis lies along <paramref name="yAxis"/> and whose +Z
        /// axis points toward <paramref name="zHint"/> as closely as possible.
        /// </summary>
        public static bool TryAlignY(Vector3 yAxis, Vector3 zHint, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!IsFinite(yAxis) || yAxis.sqrMagnitude < 1e-10f)
                return false;

            Vector3 y = yAxis.normalized;
            Vector3 z = Vector3.zero;
            if (IsFinite(zHint))
                z = Vector3.ProjectOnPlane(zHint, y);
            if (z.sqrMagnitude < 1e-8f)
                z = Vector3.ProjectOnPlane(Vector3.forward, y);
            if (z.sqrMagnitude < 1e-8f)
                z = Vector3.ProjectOnPlane(Vector3.right, y);
            if (z.sqrMagnitude < 1e-8f)
                return false;

            rotation = Quaternion.LookRotation(z.normalized, y);
            return IsFinite(rotation);
        }

        /// <summary>
        /// Builds a rotation that maps the bone-local up/forward captured at bind
        /// onto the requested world axes. Works for any rig axis convention.
        /// </summary>
        public static bool TryMatchAxes(
            Vector3 localUp,
            Vector3 localForward,
            Vector3 worldUp,
            Vector3 worldForward,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!IsFinite(localUp) || !IsFinite(localForward) || !IsFinite(worldUp) || !IsFinite(worldForward))
                return false;
            if (localUp.sqrMagnitude < 1e-10f || worldUp.sqrMagnitude < 1e-10f)
                return false;

            Vector3 lu = localUp.normalized;
            Vector3 lf = Vector3.ProjectOnPlane(localForward, lu);
            Vector3 wu = worldUp.normalized;
            Vector3 wf = Vector3.ProjectOnPlane(worldForward, wu);
            if (lf.sqrMagnitude < 1e-8f || wf.sqrMagnitude < 1e-8f)
                return false;

            Quaternion local = Quaternion.LookRotation(lf.normalized, lu);
            Quaternion world = Quaternion.LookRotation(wf.normalized, wu);
            rotation = world * Quaternion.Inverse(local);
            return IsFinite(rotation);
        }

        public static Quaternion TwistToward(Quaternion rotation, Vector3 axis, Vector3 currentSide, Vector3 desiredSide)
        {
            if (!IsFinite(axis) || axis.sqrMagnitude < 1e-10f)
                return rotation;
            Vector3 a = Vector3.ProjectOnPlane(currentSide, axis);
            Vector3 b = Vector3.ProjectOnPlane(desiredSide, axis);
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
                return rotation;
            Quaternion twist = Quaternion.FromToRotation(a.normalized, b.normalized);
            Quaternion result = twist * rotation;
            return IsFinite(result) ? result : rotation;
        }

        /// <summary>Direction the middle joint sticks out of the line from a to c.</summary>
        public static Vector3 BendOr(Vector3 a, Vector3 b, Vector3 c, Vector3 fallback)
        {
            Vector3 ac = c - a;
            float denom = Vector3.Dot(ac, ac);
            if (denom < 1e-8f || !IsFinite(fallback))
                return IsFinite(fallback) ? fallback : Vector3.forward;
            float t = Vector3.Dot(b - a, ac) / denom;
            Vector3 bend = b - (a + ac * t);
            if (bend.sqrMagnitude < 1e-8f || !IsFinite(bend))
                return fallback;
            return bend.normalized;
        }

        public static void SwapLeftRight(PoseFrame frame)
        {
            Swap(frame, JointId.LeftEye, JointId.RightEye);
            Swap(frame, JointId.LeftEar, JointId.RightEar);
            Swap(frame, JointId.LeftShoulder, JointId.RightShoulder);
            Swap(frame, JointId.LeftElbow, JointId.RightElbow);
            Swap(frame, JointId.LeftWrist, JointId.RightWrist);
            Swap(frame, JointId.LeftHip, JointId.RightHip);
            Swap(frame, JointId.LeftKnee, JointId.RightKnee);
            Swap(frame, JointId.LeftAnkle, JointId.RightAnkle);
        }

        static void Swap(PoseFrame frame, JointId a, JointId b)
        {
            int i = (int)a;
            int j = (int)b;
            Vector3 p = frame.Position[i];
            frame.Position[i] = frame.Position[j];
            frame.Position[j] = p;
            float c = frame.Confidence[i];
            frame.Confidence[i] = frame.Confidence[j];
            frame.Confidence[j] = c;
            bool t = frame.Tracked[i];
            frame.Tracked[i] = frame.Tracked[j];
            frame.Tracked[j] = t;
        }

        public static void ConvertToUnity(PoseFrame src, PoseFrame dst, SpaceMode mode, bool swapLeftRight)
        {
            dst.TimestampMs = src.TimestampMs;
            dst.Space = PoseSpace.UnityYUp;
            dst.UnitScale = 1f;
            dst.Valid = src.Valid;

            PoseSpace space = src.Space;
            if (mode == SpaceMode.CameraYDown)
                space = PoseSpace.CameraYDown;
            else if (mode == SpaceMode.UnityYUp)
                space = PoseSpace.UnityYUp;

            float unit = src.UnitScale > 0f && IsFinite(src.UnitScale) ? src.UnitScale : 1f;
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                Vector3 p = src.Position[i] * unit;
                if (!IsFinite(p))
                    p = Vector3.zero;
                if (space == PoseSpace.CameraYDown)
                    p = new Vector3(p.x, -p.y, p.z);
                dst.Position[i] = p;
                dst.Confidence[i] = IsFinite(src.Confidence[i]) ? src.Confidence[i] : 0f;
                dst.Tracked[i] = false;
            }

            if (swapLeftRight)
                SwapLeftRight(dst);
        }
    }
}
