using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkeletonPose
{
    /// <summary>
    /// Human figure built from capsules. Bone pivots stay at scale 1 and keep their
    /// bind local positions. Only rotations change, plus the root translation.
    /// Visual meshes are leaves, so their size cannot compound down the hierarchy.
    /// </summary>
    public sealed class MannequinRig
    {
        const float Thigh = 0.42f;
        const float Shin = 0.40f;
        const float FootLen = 0.18f;
        const float SpineLen = 0.26f;
        const float ChestLen = 0.22f;
        const float NeckLen = 0.10f;
        const float HeadR = 0.12f;
        const float UpperArm = 0.28f;
        const float Forearm = 0.25f;
        const float HandLen = 0.12f;
        const float ShoulderHalf = 0.20f;
        const float ShoulderLift = 0.16f;
        const float HipHalf = 0.11f;

        public float TorsoLength { get { return SpineLen + ShoulderLift; } }
        public float HipHeight { get { return Thigh + Shin; } }
        public bool Visible
        {
            get { return _root != null && _root.gameObject.activeSelf; }
            set { if (_root != null) _root.gameObject.SetActive(value); }
        }

        Transform _root;
        Transform _spine, _chest, _neck, _head;
        Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
        Transform _lThigh, _lShin, _lFoot, _rThigh, _rShin, _rFoot;
        readonly List<Material> _materials = new List<Material>();
        readonly List<LockedPivot> _locked = new List<LockedPivot>();

        struct LockedPivot
        {
            public Transform Pivot;
            public Vector3 LocalPosition;
        }

        public void Build(Transform parent)
        {
            Dispose();
            _locked.Clear();

            Color shirt = new Color(0.23f, 0.45f, 0.72f);
            Color pants = new Color(0.16f, 0.18f, 0.24f);
            Color skin = new Color(0.93f, 0.76f, 0.64f);
            Color shoe = new Color(0.12f, 0.12f, 0.13f);

            _root = Pivot("Mannequin", parent, Vector3.up * HipHeight, Quaternion.LookRotation(Vector3.back, Vector3.up));
            Sphere(_root, "Pelvis", 0.12f, Vector3.zero, pants);

            _spine = Pivot("Spine", _root, Vector3.zero, Quaternion.identity);
            Capsule(_spine, "SpineMesh", SpineLen, 0.09f, shirt);
            _chest = Pivot("Chest", _spine, new Vector3(0f, SpineLen, 0f), Quaternion.identity);
            Capsule(_chest, "ChestMesh", ChestLen, 0.11f, shirt);
            _neck = Pivot("Neck", _chest, new Vector3(0f, ChestLen, 0f), Quaternion.identity);
            Capsule(_neck, "NeckMesh", NeckLen, 0.045f, skin);
            _head = Pivot("Head", _neck, new Vector3(0f, NeckLen, 0f), Quaternion.identity);
            Sphere(_head, "HeadMesh", HeadR, new Vector3(0f, HeadR, 0f), skin);

            _lUpper = Pivot("LeftUpperArm", _chest, new Vector3(-ShoulderHalf, ShoulderLift, 0f), Quaternion.Euler(0f, 0f, 90f));
            Capsule(_lUpper, "LeftUpperArmMesh", UpperArm, 0.045f, skin);
            _lLower = Pivot("LeftForearm", _lUpper, new Vector3(0f, UpperArm, 0f), Quaternion.identity);
            Capsule(_lLower, "LeftForearmMesh", Forearm, 0.038f, skin);
            _lHand = Pivot("LeftHand", _lLower, new Vector3(0f, Forearm, 0f), Quaternion.identity);
            Capsule(_lHand, "LeftHandMesh", HandLen, 0.028f, skin);

            _rUpper = Pivot("RightUpperArm", _chest, new Vector3(ShoulderHalf, ShoulderLift, 0f), Quaternion.Euler(0f, 0f, -90f));
            Capsule(_rUpper, "RightUpperArmMesh", UpperArm, 0.045f, skin);
            _rLower = Pivot("RightForearm", _rUpper, new Vector3(0f, UpperArm, 0f), Quaternion.identity);
            Capsule(_rLower, "RightForearmMesh", Forearm, 0.038f, skin);
            _rHand = Pivot("RightHand", _rLower, new Vector3(0f, Forearm, 0f), Quaternion.identity);
            Capsule(_rHand, "RightHandMesh", HandLen, 0.028f, skin);

            Quaternion down = Quaternion.Euler(180f, 0f, 0f);
            _lThigh = Pivot("LeftThigh", _root, new Vector3(-HipHalf, 0f, 0f), down);
            Capsule(_lThigh, "LeftThighMesh", Thigh, 0.065f, pants);
            _lShin = Pivot("LeftShin", _lThigh, new Vector3(0f, Thigh, 0f), Quaternion.identity);
            Capsule(_lShin, "LeftShinMesh", Shin, 0.05f, pants);
            _lFoot = Pivot("LeftFoot", _lShin, new Vector3(0f, Shin, 0f), Quaternion.Euler(-90f, 0f, 0f));
            Capsule(_lFoot, "LeftFootMesh", FootLen, 0.04f, shoe);

            _rThigh = Pivot("RightThigh", _root, new Vector3(HipHalf, 0f, 0f), down);
            Capsule(_rThigh, "RightThighMesh", Thigh, 0.065f, pants);
            _rShin = Pivot("RightShin", _rThigh, new Vector3(0f, Thigh, 0f), Quaternion.identity);
            Capsule(_rShin, "RightShinMesh", Shin, 0.05f, pants);
            _rFoot = Pivot("RightFoot", _rShin, new Vector3(0f, Shin, 0f), Quaternion.Euler(-90f, 0f, 0f));
            Capsule(_rFoot, "RightFootMesh", FootLen, 0.04f, shoe);

            Lock(_spine); Lock(_chest); Lock(_neck); Lock(_head);
            Lock(_lUpper); Lock(_lLower); Lock(_lHand);
            Lock(_rUpper); Lock(_rLower); Lock(_rHand);
            Lock(_lThigh); Lock(_lShin); Lock(_lFoot);
            Lock(_rThigh); Lock(_rShin); Lock(_rFoot);
        }

        public void Apply(SolvedBody body, Vector3 stagePosition, float dt, bool snap, float maxDegreesPerSecond)
        {
            if (_root == null || body == null || !body.Valid || _locked.Count < 16)
                return;

            float step = Mathf.Max(1f, maxDegreesPerSecond) * Mathf.Clamp(dt, 0f, 0.05f);
            _root.localScale = Vector3.one;
            _root.position = stagePosition + Vector3.up * HipHeight + body.HipsDelta * TorsoLength;

            Vector3 face = body.Forward;
            Drive(_root, null, body.Up, face, snap, step, false);
            Drive(_spine, _locked[0], body.Up, face, snap, step, true);
            Drive(_chest, _locked[1], body.Up, face, snap, step, true);

            Vector3 neckDir = body.HeadPoint - body.NeckPoint;
            Drive(_neck, _locked[2], neckDir, face, snap, step, true);
            Drive(_head, _locked[3], neckDir, face, snap, step, true);

            if (body.LeftArm)
            {
                Vector3 pole = PoseMath.BendOr(body.Joint(JointId.LeftShoulder), body.Joint(JointId.LeftElbow), body.Joint(JointId.LeftWrist), -face);
                Vector3 upper = body.Joint(JointId.LeftElbow) - body.Joint(JointId.LeftShoulder);
                Vector3 lower = body.Joint(JointId.LeftWrist) - body.Joint(JointId.LeftElbow);
                Drive(_lUpper, _locked[4], upper, pole, snap, step, true);
                Drive(_lLower, _locked[5], lower, pole, snap, step, true);
                Drive(_lHand, _locked[6], lower, pole, snap, step, true);
            }
            else
            {
                Hold(_locked[4]); Hold(_locked[5]); Hold(_locked[6]);
            }

            if (body.RightArm)
            {
                Vector3 pole = PoseMath.BendOr(body.Joint(JointId.RightShoulder), body.Joint(JointId.RightElbow), body.Joint(JointId.RightWrist), -face);
                Vector3 upper = body.Joint(JointId.RightElbow) - body.Joint(JointId.RightShoulder);
                Vector3 lower = body.Joint(JointId.RightWrist) - body.Joint(JointId.RightElbow);
                Drive(_rUpper, _locked[7], upper, pole, snap, step, true);
                Drive(_rLower, _locked[8], lower, pole, snap, step, true);
                Drive(_rHand, _locked[9], lower, pole, snap, step, true);
            }
            else
            {
                Hold(_locked[7]); Hold(_locked[8]); Hold(_locked[9]);
            }

            Vector3 footDir = Vector3.ProjectOnPlane(face, Vector3.up);
            if (footDir.sqrMagnitude < 1e-6f)
                footDir = face;
            footDir = (footDir.normalized + Vector3.down * 0.08f);

            if (body.LeftLeg)
            {
                Vector3 pole = PoseMath.BendOr(body.Joint(JointId.LeftHip), body.Joint(JointId.LeftKnee), body.Joint(JointId.LeftAnkle), face);
                Drive(_lThigh, _locked[10], body.Joint(JointId.LeftKnee) - body.Joint(JointId.LeftHip), pole, snap, step, true);
                Drive(_lShin, _locked[11], body.Joint(JointId.LeftAnkle) - body.Joint(JointId.LeftKnee), pole, snap, step, true);
                Drive(_lFoot, _locked[12], footDir, Vector3.up, snap, step, true);
            }
            else
            {
                Hold(_locked[10]); Hold(_locked[11]); Hold(_locked[12]);
            }

            if (body.RightLeg)
            {
                Vector3 pole = PoseMath.BendOr(body.Joint(JointId.RightHip), body.Joint(JointId.RightKnee), body.Joint(JointId.RightAnkle), face);
                Drive(_rThigh, _locked[13], body.Joint(JointId.RightKnee) - body.Joint(JointId.RightHip), pole, snap, step, true);
                Drive(_rShin, _locked[14], body.Joint(JointId.RightAnkle) - body.Joint(JointId.RightKnee), pole, snap, step, true);
                Drive(_rFoot, _locked[15], footDir, Vector3.up, snap, step, true);
            }
            else
            {
                Hold(_locked[13]); Hold(_locked[14]); Hold(_locked[15]);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] != null)
                    Object.Destroy(_materials[i]);
            }
            _materials.Clear();
            if (_root != null)
                Object.Destroy(_root.gameObject);
            _root = null;
            _locked.Clear();
        }

        void Lock(Transform pivot)
        {
            _locked.Add(new LockedPivot { Pivot = pivot, LocalPosition = pivot.localPosition });
        }

        static void Hold(LockedPivot locked)
        {
            if (locked.Pivot == null)
                return;
            locked.Pivot.localScale = Vector3.one;
            locked.Pivot.localPosition = locked.LocalPosition;
        }

        static void Drive(Transform pivot, LockedPivot? maybe, Vector3 direction, Vector3 pole, bool snap, float step, bool restorePosition)
        {
            if (pivot == null)
                return;
            pivot.localScale = Vector3.one;
            if (restorePosition && maybe.HasValue)
                pivot.localPosition = maybe.Value.LocalPosition;
            Quaternion target;
            if (!PoseMath.TryAlignY(direction, pole, out target))
                return;
            pivot.rotation = snap ? target : Quaternion.RotateTowards(pivot.rotation, target, step);
        }

        Transform Pivot(string name, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one;
            Strip(go);
            return go.transform;
        }

        void Capsule(Transform pivot, string name, float length, float radius, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Strip(go);
            go.transform.SetParent(pivot, false);
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            go.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
            Paint(go, color);
            if (go.transform.childCount != 0)
                Debug.LogError("Mannequin visual '" + name + "' has children. Scaling it would destroy the rig.");
        }

        void Sphere(Transform pivot, string name, float radius, Vector3 localPos, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(pivot, false);
            float s = radius * 2f;
            go.transform.localScale = new Vector3(s, s, s);
            go.transform.localPosition = localPos;
            Paint(go, color);
        }

        void Paint(GameObject go, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.12f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            _materials.Add(mat);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        static void Strip(GameObject go)
        {
            Collider[] cols = go.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                Object.DestroyImmediate(cols[i]);
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null)
                Object.DestroyImmediate(body);
        }
    }
}
