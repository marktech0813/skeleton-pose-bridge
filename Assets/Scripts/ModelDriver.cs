using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkeletonPose
{
    /// <summary>
    /// One box or generic bone. Is Root is the hips: it may translate.
    /// Every other bone keeps the local position and scale captured at Play,
    /// and only its rotation is updated. That is what stops a parented box
    /// skeleton, or a skinned mesh, from being torn apart.
    /// </summary>
    [Serializable]
    public class GenericBoneLink
    {
        public Transform bone;
        public JointId from = JointId.LeftShoulder;
        public JointId to = JointId.LeftElbow;
        public bool isRoot;
    }

    /// <summary>
    /// Rotation-only driver for a Humanoid avatar and optional generic bones.
    /// </summary>
    public sealed class ModelDriver
    {
        enum Aim
        {
            Hips,
            Torso,
            HeadLine,
            ClavicleLeft,
            ClavicleRight,
            Limb,
            Foot
        }

        class Driven
        {
            public Transform Bone;
            public Vector3 BindLocalPosition;
            public Vector3 BindLocalScale;
            public Vector3 BindWorldPosition;
            public Quaternion BindLocalRotation;
            public Vector3 AxisLocal;
            public Vector3 TwistLocal;
            public Vector3 LocalUp;
            public Vector3 LocalFwd;
            public bool HasAxis;
            public bool Twist;
            public Aim Aim;
            public JointId From;
            public JointId To;
            public bool NeedLeftArm;
            public bool NeedRightArm;
            public bool NeedLeftLeg;
            public bool NeedRightLeg;
        }

        readonly List<Driven> _bones = new List<Driven>();
        readonly List<Driven> _generic = new List<Driven>();
        Animator _animator;
        int _animatorId = int.MinValue;
        float _modelTorso = 0.42f;
        string _status = "No humanoid assigned. The built-in figure is showing.";

        public bool IsBound { get { return _bones.Count > 0; } }
        public float ModelTorso { get { return _modelTorso; } }
        public string Status { get { return _status; } }
        public int DrivenCount { get { return _bones.Count + _generic.Count; } }

        public void EnsureHumanoid(Animator animator)
        {
            int id = animator == null ? 0 : animator.GetInstanceID();
            if (id == _animatorId)
                return;
            _animatorId = id;
            _bones.Clear();
            _animator = animator;
            if (animator == null)
            {
                _status = _generic.Count > 0
                    ? _status
                    : "No humanoid assigned. The built-in figure is showing.";
                return;
            }
            if (!animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            {
                _status = "Animator is not a valid Humanoid. On the model importer set Rig to Humanoid and click Apply. The model was not modified.";
                Debug.LogError(_status);
                return;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                _status = "Humanoid hips bone is missing. The model was not modified.";
                Debug.LogError(_status);
                return;
            }

            Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Vector3 shoulder = hips.position + Vector3.up;
            if (leftArm != null && rightArm != null)
                shoulder = (leftArm.position + rightArm.position) * 0.5f;
            _modelTorso = Vector3.Distance(shoulder, hips.position);
            if (_modelTorso < 1e-4f)
                _modelTorso = EstimateTorso(animator.gameObject);

            Vector3 modelUp = (shoulder - hips.position).sqrMagnitude > 1e-8f
                ? (shoulder - hips.position).normalized
                : Vector3.up;
            Vector3 hipRight = Vector3.right;
            Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (leftLeg != null && rightLeg != null && (rightLeg.position - leftLeg.position).sqrMagnitude > 1e-8f)
                hipRight = (rightLeg.position - leftLeg.position).normalized;
            Vector3 modelFwd = Vector3.Cross(hipRight, modelUp);
            if (modelFwd.sqrMagnitude < 1e-8f)
                modelFwd = Vector3.forward;
            modelFwd.Normalize();
            Vector3 elbowPole = -modelFwd;

            Quaternion hipInv = Quaternion.Inverse(hips.rotation);
            Driven hip = Begin(hips);
            hip.Aim = Aim.Hips;
            hip.LocalUp = hipInv * modelUp;
            hip.LocalFwd = hipInv * modelFwd;
            _bones.Add(hip);

            AddTorso(animator, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck);
            AddTorso(animator, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head);
            AddTorso(animator, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LastBone);
            AddHeadLine(animator, HumanBodyBones.Neck, HumanBodyBones.Head, modelUp);
            AddHeadLine(animator, HumanBodyBones.Head, HumanBodyBones.Neck, modelUp);

            AddLimb(animator, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, JointId.LeftShoulder, JointId.LeftShoulder, Aim.ClavicleLeft, modelUp, true, false, false, false);
            AddLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, JointId.LeftShoulder, JointId.LeftElbow, Aim.Limb, elbowPole, true, false, false, false);
            AddLimb(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, JointId.LeftElbow, JointId.LeftWrist, Aim.Limb, elbowPole, true, false, false, false);
            AddContinue(animator, HumanBodyBones.LeftHand, JointId.LeftElbow, JointId.LeftWrist, elbowPole, true, false, false, false);

            AddLimb(animator, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, JointId.RightShoulder, JointId.RightShoulder, Aim.ClavicleRight, modelUp, false, true, false, false);
            AddLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, JointId.RightShoulder, JointId.RightElbow, Aim.Limb, elbowPole, false, true, false, false);
            AddLimb(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, JointId.RightElbow, JointId.RightWrist, Aim.Limb, elbowPole, false, true, false, false);
            AddContinue(animator, HumanBodyBones.RightHand, JointId.RightElbow, JointId.RightWrist, elbowPole, false, true, false, false);

            AddLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, JointId.LeftHip, JointId.LeftKnee, Aim.Limb, modelFwd, false, false, true, false);
            AddLimb(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, JointId.LeftKnee, JointId.LeftAnkle, Aim.Limb, modelFwd, false, false, true, false);
            AddFoot(animator, HumanBodyBones.LeftFoot, modelFwd, true);
            AddFoot(animator, HumanBodyBones.LeftToes, modelFwd, true);

            AddLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, JointId.RightHip, JointId.RightKnee, Aim.Limb, modelFwd, false, false, false, true);
            AddLimb(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, JointId.RightKnee, JointId.RightAnkle, Aim.Limb, modelFwd, false, false, false, true);
            AddFoot(animator, HumanBodyBones.RightFoot, modelFwd, false);
            AddFoot(animator, HumanBodyBones.RightToes, modelFwd, false);

            animator.enabled = false;
            CalmPhysics(animator.gameObject);
            SkinnedMeshRenderer[] meshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < meshes.Length; i++)
                meshes[i].updateWhenOffscreen = true;

            _status = "Humanoid rotation-only, " + _bones.Count + " bones. Scale and child positions are locked to the bind pose.";
            Debug.Log(_status + " Animator playback is disabled so it cannot fight the skeleton stream.");
        }

        public void BindGeneric(GenericBoneLink[] links)
        {
            _generic.Clear();
            if (links == null || links.Length == 0)
            {
                if (_bones.Count == 0)
                    _status = "No humanoid assigned. The built-in figure is showing.";
                return;
            }

            var transforms = new List<Transform>();
            Transform calmRoot = null;
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i] != null && links[i].bone != null)
                    transforms.Add(links[i].bone);
            }

            for (int i = 0; i < links.Length; i++)
            {
                GenericBoneLink link = links[i];
                if (link == null || link.bone == null)
                    continue;
                if (link.isRoot || calmRoot == null)
                    calmRoot = link.bone;

                Driven d = Begin(link.bone);
                d.Aim = link.isRoot ? Aim.Hips : Aim.Limb;
                d.From = link.from;
                d.To = link.to;
                d.Twist = false;

                Transform tip = null;
                for (int c = 0; c < transforms.Count; c++)
                {
                    if (transforms[c] != link.bone && transforms[c].parent == link.bone)
                    {
                        tip = transforms[c];
                        break;
                    }
                }

                Vector3 axisWorld = tip != null ? tip.position - link.bone.position : link.bone.TransformDirection(Vector3.up);
                if (axisWorld.sqrMagnitude < 1e-8f)
                    axisWorld = Vector3.up;
                d.HasAxis = true;
                d.AxisLocal = Quaternion.Inverse(link.bone.rotation) * axisWorld.normalized;
                d.TwistLocal = Vector3.right;

                if (link.isRoot)
                {
                    Vector3 up = axisWorld.normalized;
                    Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
                    if (fwd.sqrMagnitude < 1e-6f)
                        fwd = Vector3.ProjectOnPlane(Vector3.right, up);
                    Quaternion inv = Quaternion.Inverse(link.bone.rotation);
                    d.LocalUp = inv * up;
                    d.LocalFwd = fwd.sqrMagnitude > 1e-6f ? inv * fwd.normalized : inv * Vector3.forward;
                }

                _generic.Add(d);

                Animator parentAnimator = link.bone.GetComponentInParent<Animator>();
                if (parentAnimator != null && parentAnimator.enabled && parentAnimator != _animator)
                {
                    Debug.LogWarning("An Animator above '" + link.bone.name + "' is still enabled and will overwrite these bones. Assign it as the Humanoid, or disable the Animator.");
                }
            }

            _generic.Sort(delegate (Driven a, Driven b) { return Depth(a.Bone).CompareTo(Depth(b.Bone)); });
            if (_bones.Count == 0 && _generic.Count > 0)
            {
                _status = "Generic rotation-only, " + _generic.Count + " bones. Child positions and scales are locked.";
                if (calmRoot != null)
                    _modelTorso = EstimateTorso(calmRoot.gameObject);
            }
            if (calmRoot != null)
                CalmPhysics(calmRoot.gameObject);
        }

        public void Apply(SolvedBody body, float referenceTorso, float dt, bool snap, float maxDegreesPerSecond)
        {
            if (body == null || !body.Valid)
                return;
            float step = Mathf.Max(1f, maxDegreesPerSecond) * Mathf.Clamp(dt, 0f, 0.05f);
            float torso = _modelTorso > 1e-4f ? _modelTorso : referenceTorso;
            for (int i = 0; i < _bones.Count; i++)
                ApplyOne(_bones[i], body, torso, step, snap);
            for (int i = 0; i < _generic.Count; i++)
                ApplyOne(_generic[i], body, torso, step, snap);
        }

        static void ApplyOne(Driven d, SolvedBody body, float torso, float step, bool snap)
        {
            if (d.Bone == null)
                return;

            // Lock the bind pose shape before writing the new rotation.
            d.Bone.localScale = d.BindLocalScale;
            if (d.Aim != Aim.Hips)
                d.Bone.localPosition = d.BindLocalPosition;

            if (d.Aim == Aim.Hips)
            {
                Quaternion facing;
                if (PoseMath.TryMatchAxes(d.LocalUp, d.LocalFwd, body.Up, body.Forward, out facing))
                    SetRotation(d.Bone, facing, snap, step);
                d.Bone.position = d.BindWorldPosition + body.HipsDelta * torso;
                return;
            }

            if (d.Aim == Aim.Foot)
            {
                if (d.NeedLeftLeg && !body.LeftLeg) return;
                if (d.NeedRightLeg && !body.RightLeg) return;
                Vector3 flat = Vector3.ProjectOnPlane(body.Forward, Vector3.up);
                if (flat.sqrMagnitude < 1e-6f)
                    flat = body.Forward;
                Quaternion foot;
                if (PoseMath.TryMatchAxes(d.LocalUp, d.LocalFwd, Vector3.up, flat, out foot))
                    SetRotation(d.Bone, foot, snap, step);
                return;
            }

            if (!Allowed(d, body))
                return;

            Vector3 desired = Desired(d, body);
            if (desired.sqrMagnitude < 1e-8f || !PoseMath.IsFinite(desired))
                return;

            Quaternion parentWorld = d.Bone.parent != null ? d.Bone.parent.rotation : Quaternion.identity;
            Quaternion baseWorld = parentWorld * d.BindLocalRotation;
            Vector3 bindDir = baseWorld * d.AxisLocal;
            if (bindDir.sqrMagnitude < 1e-8f)
                return;

            Quaternion swung = Quaternion.FromToRotation(bindDir, desired.normalized) * baseWorld;
            if (d.Twist)
            {
                Vector3 side = swung * d.TwistLocal;
                swung = PoseMath.TwistToward(swung, desired, side, Pole(d, body));
            }
            if (PoseMath.IsFinite(swung))
                SetRotation(d.Bone, swung, snap, step);
        }

        static bool Allowed(Driven d, SolvedBody body)
        {
            if (d.NeedLeftArm && !body.LeftArm) return false;
            if (d.NeedRightArm && !body.RightArm) return false;
            if (d.NeedLeftLeg && !body.LeftLeg) return false;
            if (d.NeedRightLeg && !body.RightLeg) return false;
            if (d.Aim == Aim.Limb && (!body.Tracked[(int)d.From] || !body.Tracked[(int)d.To]))
                return false;
            return true;
        }

        static Vector3 Desired(Driven d, SolvedBody body)
        {
            if (d.Aim == Aim.Torso)
                return body.Up;
            if (d.Aim == Aim.HeadLine)
                return body.HeadPoint - body.NeckPoint;
            if (d.Aim == Aim.ClavicleLeft)
                return body.Joint(JointId.LeftShoulder) - body.ShoulderCenter;
            if (d.Aim == Aim.ClavicleRight)
                return body.Joint(JointId.RightShoulder) - body.ShoulderCenter;
            return body.Joint(d.To) - body.Joint(d.From);
        }

        static Vector3 Pole(Driven d, SolvedBody body)
        {
            if (d.From == JointId.LeftShoulder && d.To == JointId.LeftElbow || d.From == JointId.LeftElbow && d.To == JointId.LeftWrist)
                return PoseMath.BendOr(body.Joint(JointId.LeftShoulder), body.Joint(JointId.LeftElbow), body.Joint(JointId.LeftWrist), -body.Forward);
            if (d.From == JointId.RightShoulder && d.To == JointId.RightElbow || d.From == JointId.RightElbow && d.To == JointId.RightWrist)
                return PoseMath.BendOr(body.Joint(JointId.RightShoulder), body.Joint(JointId.RightElbow), body.Joint(JointId.RightWrist), -body.Forward);
            if (d.From == JointId.LeftHip && d.To == JointId.LeftKnee || d.From == JointId.LeftKnee && d.To == JointId.LeftAnkle)
                return PoseMath.BendOr(body.Joint(JointId.LeftHip), body.Joint(JointId.LeftKnee), body.Joint(JointId.LeftAnkle), body.Forward);
            if (d.From == JointId.RightHip && d.To == JointId.RightKnee || d.From == JointId.RightKnee && d.To == JointId.RightAnkle)
                return PoseMath.BendOr(body.Joint(JointId.RightHip), body.Joint(JointId.RightKnee), body.Joint(JointId.RightAnkle), body.Forward);
            return body.Forward;
        }

        static void SetRotation(Transform bone, Quaternion target, bool snap, float step)
        {
            bone.rotation = snap ? target : Quaternion.RotateTowards(bone.rotation, target, step);
        }

        Driven Begin(Transform bone)
        {
            if (bone.localScale.sqrMagnitude < 1e-10f)
                bone.localScale = Vector3.one;
            return new Driven
            {
                Bone = bone,
                BindLocalPosition = bone.localPosition,
                BindLocalScale = bone.localScale,
                BindWorldPosition = bone.position,
                BindLocalRotation = bone.localRotation
            };
        }

        void AddTorso(Animator animator, HumanBodyBones bone, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c)
        {
            Transform t = animator.GetBoneTransform(bone);
            Transform child = First(animator, a, b, c);
            if (t == null || child == null)
                return;
            AddAxis(t, child.position - t.position, Aim.Torso, JointId.LeftHip, JointId.LeftShoulder, Vector3.zero, false, false, false, false, false);
        }

        void AddHeadLine(Animator animator, HumanBodyBones bone, HumanBodyBones other, Vector3 fallback)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t == null)
                return;
            Transform o = animator.GetBoneTransform(other);
            Vector3 axis = o != null ? (bone == HumanBodyBones.Head ? t.position - o.position : o.position - t.position) : fallback;
            AddAxis(t, axis, Aim.HeadLine, JointId.Nose, JointId.Nose, Vector3.zero, false, false, false, false, false);
        }

        void AddLimb(Animator animator, HumanBodyBones bone, HumanBodyBones child, JointId from, JointId to, Aim aim, Vector3 bindPole, bool leftArm, bool rightArm, bool leftLeg, bool rightLeg)
        {
            Transform t = animator.GetBoneTransform(bone);
            Transform c = child == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(child);
            if (t == null || c == null)
                return;
            bool twist = aim == Aim.Limb;
            AddAxis(t, c.position - t.position, aim, from, to, bindPole, twist, leftArm, rightArm, leftLeg, rightLeg);
        }

        void AddContinue(Animator animator, HumanBodyBones bone, JointId from, JointId to, Vector3 bindPole, bool leftArm, bool rightArm, bool leftLeg, bool rightLeg)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t == null || t.parent == null)
                return;
            AddAxis(t, t.position - t.parent.position, Aim.Limb, from, to, bindPole, true, leftArm, rightArm, leftLeg, rightLeg);
        }

        void AddFoot(Animator animator, HumanBodyBones bone, Vector3 modelForward, bool left)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t == null)
                return;
            Driven d = Begin(t);
            d.Aim = Aim.Foot;
            d.NeedLeftLeg = left;
            d.NeedRightLeg = !left;
            Quaternion inv = Quaternion.Inverse(t.rotation);
            d.LocalUp = inv * Vector3.up;
            d.LocalFwd = inv * modelForward;
            _bones.Add(d);
        }

        void AddAxis(Transform bone, Vector3 worldAxis, Aim aim, JointId from, JointId to, Vector3 bindPole, bool twist, bool leftArm, bool rightArm, bool leftLeg, bool rightLeg)
        {
            if (worldAxis.sqrMagnitude < 1e-8f)
                worldAxis = bone.TransformDirection(Vector3.up);
            Vector3 axis = worldAxis.normalized;
            Driven d = Begin(bone);
            d.Aim = aim;
            d.From = from;
            d.To = to;
            d.HasAxis = true;
            d.Twist = twist;
            d.NeedLeftArm = leftArm;
            d.NeedRightArm = rightArm;
            d.NeedLeftLeg = leftLeg;
            d.NeedRightLeg = rightLeg;
            d.AxisLocal = Quaternion.Inverse(bone.rotation) * axis;
            Vector3 pole = Vector3.ProjectOnPlane(bindPole, axis);
            if (pole.sqrMagnitude < 1e-6f)
                pole = Vector3.Cross(axis, Vector3.up);
            if (pole.sqrMagnitude < 1e-6f)
                pole = Vector3.Cross(axis, Vector3.right);
            d.TwistLocal = Quaternion.Inverse(bone.rotation) * pole.normalized;
            _bones.Add(d);
        }

        static Transform First(Animator animator, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c)
        {
            Transform t = BoneOrNull(animator, a);
            if (t != null) return t;
            t = BoneOrNull(animator, b);
            if (t != null) return t;
            return BoneOrNull(animator, c);
        }

        static Transform BoneOrNull(Animator animator, HumanBodyBones bone)
        {
            if (bone == HumanBodyBones.LastBone)
                return null;
            return animator.GetBoneTransform(bone);
        }

        static int Depth(Transform t)
        {
            int depth = 0;
            while (t != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        static float EstimateTorso(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0.42f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float torso = bounds.size.y * 0.24f;
            return torso > 1e-3f && torso < 100000f ? torso : 0.42f;
        }

        static void CalmPhysics(GameObject root)
        {
            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            int changed = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (!bodies[i].isKinematic || bodies[i].useGravity)
                {
                    bodies[i].isKinematic = true;
                    bodies[i].useGravity = false;
#if UNITY_6000_0_OR_NEWER
                    bodies[i].linearVelocity = Vector3.zero;
                    bodies[i].angularVelocity = Vector3.zero;
#else
                    bodies[i].velocity = Vector3.zero;
                    bodies[i].angularVelocity = Vector3.zero;
#endif
                    changed++;
                }
            }
            CharacterController[] controllers = root.GetComponentsInChildren<CharacterController>(true);
            for (int i = 0; i < controllers.Length; i++)
                controllers[i].enabled = false;
            if (changed > 0)
                Debug.Log("Set " + changed + " rigidbodies to kinematic so physics cannot fling the skeleton apart.");
        }
    }

    public sealed class JointMarkers
    {
        Transform _root;
        Transform[] _dots;
        Material _material;

        public void SetVisible(bool visible, Transform parent)
        {
            if (!visible)
            {
                if (_root != null)
                    _root.gameObject.SetActive(false);
                return;
            }
            if (_root == null)
                Build(parent);
            _root.gameObject.SetActive(true);
        }

        public void Apply(SolvedBody body, Vector3 anchor, float torsoLength)
        {
            if (_root == null || !_root.gameObject.activeSelf || body == null || !body.Valid)
                return;
            _root.localScale = Vector3.one;
            _root.rotation = Quaternion.identity;
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                _dots[i].position = anchor + body.World[i] * torsoLength;
                _dots[i].localScale = Vector3.one * 0.045f;
            }
        }

        public void Dispose()
        {
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
            _material = null;
            _root = null;
            _dots = null;
        }

        void Build(Transform parent)
        {
            var go = new GameObject("JointMarkers");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _dots = new Transform[PoseProtocol.JointCount];
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _material = new Material(shader);
            Color color = new Color(1f, 0.82f, 0.15f);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
            _material.color = color;
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = PoseProtocol.Names[i];
                Collider col = dot.GetComponent<Collider>();
                if (col != null)
                    UnityEngine.Object.DestroyImmediate(col);
                dot.transform.SetParent(_root, false);
                dot.GetComponent<MeshRenderer>().sharedMaterial = _material;
                _dots[i] = dot.transform;
            }
        }
    }
}
