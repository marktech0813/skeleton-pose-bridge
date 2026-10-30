using UnityEngine;

namespace SkeletonPose
{
    public sealed class PoseSmoother
    {
        readonly Vector3[] _current = new Vector3[PoseProtocol.JointCount];
        readonly bool[] _has = new bool[PoseProtocol.JointCount];
        public int Held;

        public void Reset()
        {
            for (int i = 0; i < PoseProtocol.JointCount; i++)
                _has[i] = false;
            Held = 0;
        }

        public void Smooth(PoseFrame unity, PoseFrame dst, float responsiveness, float dt, float confidence, float jumpTorsos)
        {
            dst.Valid = unity.Valid;
            dst.Space = PoseSpace.UnityYUp;
            dst.UnitScale = 1f;
            dst.TimestampMs = unity.TimestampMs;
            Held = 0;

            float torso = EstimateTorso(unity, confidence);
            float limit = Mathf.Max(1e-4f, torso) * Mathf.Max(0.25f, jumpTorsos);
            float alpha = 1f - Mathf.Exp(-Mathf.Max(0f, responsiveness) * Mathf.Max(0f, dt));

            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                bool good = unity.Confidence[i] >= confidence && PoseMath.IsFinite(unity.Position[i]);
                if (!good)
                {
                    if (_has[i])
                    {
                        dst.Position[i] = _current[i];
                        dst.Confidence[i] = confidence;
                        dst.Tracked[i] = true;
                        Held++;
                    }
                    else
                    {
                        dst.Position[i] = Vector3.zero;
                        dst.Confidence[i] = 0f;
                        dst.Tracked[i] = false;
                    }
                    continue;
                }

                if (_has[i] && Vector3.Distance(_current[i], unity.Position[i]) > limit)
                {
                    dst.Position[i] = _current[i];
                    dst.Confidence[i] = unity.Confidence[i];
                    dst.Tracked[i] = true;
                    Held++;
                    continue;
                }

                if (!_has[i])
                {
                    _current[i] = unity.Position[i];
                    _has[i] = true;
                }
                else
                {
                    _current[i] = Vector3.Lerp(_current[i], unity.Position[i], alpha);
                }

                dst.Position[i] = _current[i];
                dst.Confidence[i] = unity.Confidence[i];
                dst.Tracked[i] = true;
            }
        }

        static float EstimateTorso(PoseFrame frame, float confidence)
        {
            if (!Confident(frame, JointId.LeftHip, confidence) ||
                !Confident(frame, JointId.RightHip, confidence) ||
                !Confident(frame, JointId.LeftShoulder, confidence) ||
                !Confident(frame, JointId.RightShoulder, confidence))
                return 0.5f;

            Vector3 hip = PoseMath.Mid(frame.Position[(int)JointId.LeftHip], frame.Position[(int)JointId.RightHip]);
            Vector3 sho = PoseMath.Mid(frame.Position[(int)JointId.LeftShoulder], frame.Position[(int)JointId.RightShoulder]);
            return Mathf.Max(1e-4f, Vector3.Distance(hip, sho));
        }

        static bool Confident(PoseFrame frame, JointId id, float confidence)
        {
            int i = (int)id;
            return frame.Confidence[i] >= confidence && PoseMath.IsFinite(frame.Position[i]);
        }
    }

    public sealed class PoseSolver
    {
        Vector3 _calibHip;
        bool _hasCalib;

        public void Recalibrate()
        {
            _hasCalib = false;
        }

        public bool TrySolve(PoseFrame unity, SolvedBody dst)
        {
            dst.Valid = false;
            dst.LeftArm = false;
            dst.RightArm = false;
            dst.LeftLeg = false;
            dst.RightLeg = false;
            dst.RejectReason = "";
            for (int i = 0; i < PoseProtocol.JointCount; i++)
                dst.Tracked[i] = unity.Tracked[i];

            if (!unity.Tracked[(int)JointId.LeftHip] || !unity.Tracked[(int)JointId.RightHip] ||
                !unity.Tracked[(int)JointId.LeftShoulder] || !unity.Tracked[(int)JointId.RightShoulder])
            {
                dst.RejectReason = "Need both hips and both shoulders.";
                return false;
            }

            Vector3 hip = PoseMath.Mid(unity.Position[(int)JointId.LeftHip], unity.Position[(int)JointId.RightHip]);
            Vector3 sho = PoseMath.Mid(unity.Position[(int)JointId.LeftShoulder], unity.Position[(int)JointId.RightShoulder]);
            float measured = Vector3.Distance(hip, sho);
            if (measured < 1e-5f || !PoseMath.IsFinite(hip) || !PoseMath.IsFinite(sho))
            {
                dst.RejectReason = "Torso length is degenerate.";
                return false;
            }

            if (!_hasCalib)
            {
                _calibHip = hip;
                _hasCalib = true;
            }

            float inv = 1f / measured;
            Vector3 delta = (hip - _calibHip) * inv;
            if (!PoseMath.IsFinite(delta))
            {
                dst.RejectReason = "Hip motion is not finite.";
                return false;
            }

            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                if (!unity.Tracked[i])
                {
                    dst.World[i] = delta;
                    continue;
                }
                Vector3 mapped = (unity.Position[i] - hip) * inv + delta;
                dst.World[i] = PoseMath.IsFinite(mapped) ? mapped : delta;
            }

            dst.HipsDelta = delta;
            dst.HipCenter = PoseMath.Mid(dst.Joint(JointId.LeftHip), dst.Joint(JointId.RightHip));
            dst.ShoulderCenter = PoseMath.Mid(dst.Joint(JointId.LeftShoulder), dst.Joint(JointId.RightShoulder));

            Vector3 upRaw = dst.ShoulderCenter - dst.HipCenter;
            Vector3 rightRaw = dst.Joint(JointId.RightHip) - dst.Joint(JointId.LeftHip);
            if (upRaw.sqrMagnitude < 1e-8f || rightRaw.sqrMagnitude < 1e-8f)
            {
                dst.RejectReason = "Torso axes are degenerate.";
                return false;
            }

            dst.Up = upRaw.normalized;
            dst.Forward = Vector3.Cross(rightRaw.normalized, dst.Up);
            if (dst.Forward.sqrMagnitude < 1e-8f)
            {
                dst.RejectReason = "Could not build a facing direction.";
                return false;
            }
            dst.Forward.Normalize();
            dst.Right = Vector3.Cross(dst.Up, dst.Forward);

            bool ears = unity.Tracked[(int)JointId.LeftEar] && unity.Tracked[(int)JointId.RightEar];
            bool nose = unity.Tracked[(int)JointId.Nose];
            Vector3 head;
            if (ears)
                head = PoseMath.Mid(dst.Joint(JointId.LeftEar), dst.Joint(JointId.RightEar));
            else if (nose)
                head = dst.Joint(JointId.Nose);
            else
                head = dst.ShoulderCenter + dst.Up * 0.35f;
            if (nose && ears)
                head = Vector3.Lerp(head, dst.Joint(JointId.Nose), 0.25f);

            dst.NeckPoint = dst.ShoulderCenter + (head - dst.ShoulderCenter) * 0.28f;
            if ((head - dst.NeckPoint).sqrMagnitude < 1e-6f)
                head = dst.NeckPoint + dst.Up * 0.2f;
            dst.HeadPoint = head;

            dst.LeftArm = Limb(unity, dst, JointId.LeftShoulder, JointId.LeftElbow, JointId.LeftWrist);
            dst.RightArm = Limb(unity, dst, JointId.RightShoulder, JointId.RightElbow, JointId.RightWrist);
            dst.LeftLeg = Limb(unity, dst, JointId.LeftHip, JointId.LeftKnee, JointId.LeftAnkle);
            dst.RightLeg = Limb(unity, dst, JointId.RightHip, JointId.RightKnee, JointId.RightAnkle);

            if (!PoseMath.IsFinite(dst.Up) || !PoseMath.IsFinite(dst.Forward) || !PoseMath.IsFinite(dst.HipsDelta))
            {
                dst.RejectReason = "Solved pose is not finite.";
                return false;
            }

            dst.Valid = true;
            return true;
        }

        static bool Limb(PoseFrame frame, SolvedBody body, JointId a, JointId b, JointId c)
        {
            if (!frame.Tracked[(int)a] || !frame.Tracked[(int)b] || !frame.Tracked[(int)c])
                return false;
            Vector3 ab = body.Joint(b) - body.Joint(a);
            Vector3 bc = body.Joint(c) - body.Joint(b);
            return ab.sqrMagnitude > 1e-8f && bc.sqrMagnitude > 1e-8f && PoseMath.IsFinite(ab) && PoseMath.IsFinite(bc);
        }
    }
}
