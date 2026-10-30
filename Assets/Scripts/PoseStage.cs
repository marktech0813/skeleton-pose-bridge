using UnityEngine;
using UnityEngine.Rendering;

namespace SkeletonPose
{
    /// <summary>
    /// Plays a 3D skeleton on a human figure.
    ///
    /// The figure is not driven by copying joint positions onto bone boxes.
    /// That tears a rig apart: child bones inherit a parent's scale, a skinned
    /// mesh leaves its bind pose, and a zero-length bone writes a NaN rotation
    /// that makes the renderer drop the object. This stage rotates bones only.
    /// Child local positions and scales stay at the values captured when Play
    /// starts. Visual meshes are leaves, so resizing one cannot scale the next.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class PoseStage : MonoBehaviour
    {
        [Header("Stream")]
        [Tooltip("UDP port. The C detector sends P3D1 packets here.")]
        public int listenPort = 5055;
        public SpaceMode spaceMode = SpaceMode.UsePacket;
        [Tooltip("Turn on if the detector's left/right is the image side instead of the person's left/right.")]
        public bool swapLeftRight;
        [Tooltip("Yaw added to the solved pose. Use 180 if the character faces away from you.")]
        public float extraYaw;
        [Tooltip("Ignore the network and play the built-in walk.")]
        public bool forceDemo;

        [Header("Stabilization")]
        public float confidenceThreshold = 0.25f;
        public float responsiveness = 16f;
        public float maxJointJumpInTorsos = 2.5f;
        public float maxDegreesPerSecond = 720f;

        [Header("Model")]
        [Tooltip("Humanoid character. Rig type on the importer must be Humanoid. Leave empty to use the built-in figure.")]
        public Animator humanoidAnimator;
        public bool hideMannequinWhenHumanoid = true;
        public bool showMannequin = true;
        public bool showMarkers;
        [Tooltip("Optional box rig. Mark the hips as Is Root. Do not scale a transform that has children.")]
        public GenericBoneLink[] genericBones;

        readonly UdpPoseReceiver _receiver = new UdpPoseReceiver();
        readonly PoseSmoother _smoother = new PoseSmoother();
        readonly PoseSolver _solver = new PoseSolver();
        readonly MannequinRig _mannequin = new MannequinRig();
        readonly ModelDriver _driver = new ModelDriver();
        readonly JointMarkers _markers = new JointMarkers();
        readonly PoseFrame _packet = new PoseFrame();
        readonly PoseFrame _unity = new PoseFrame();
        readonly PoseFrame _smooth = new PoseFrame();
        readonly SolvedBody _solved = new SolvedBody();
        readonly SolvedBody _yawed = new SolvedBody();

        Material _floorMaterial;
        bool _snap = true;
        bool _wasLive;
        bool _hidForHumanoid;
        bool _showGui = true;
        bool _loggedLive;
        int _genericHash = int.MinValue;
        int _packets;
        float _age;
        float _lastYaw;
        SpaceMode _lastSpace;
        bool _lastSwap;
        string _reject = "";

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
            EnsureEnvironment();
            _mannequin.Build(transform);
            _lastSpace = spaceMode;
            _lastSwap = swapLeftRight;
            _lastYaw = extraYaw + transform.eulerAngles.y;
        }

        void OnEnable()
        {
            _receiver.Start(listenPort);
        }

        void OnDisable()
        {
            _receiver.Stop();
        }

        void Start()
        {
            _driver.EnsureHumanoid(humanoidAnimator);
            _genericHash = HashLinks(genericBones);
            _driver.BindGeneric(genericBones);
            if (hideMannequinWhenHumanoid && _driver.IsBound)
            {
                showMannequin = false;
                _hidForHumanoid = true;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G)) _showGui = !_showGui;
            if (Input.GetKeyDown(KeyCode.C))
            {
                _solver.Recalibrate();
                _snap = true;
            }
            if (Input.GetKeyDown(KeyCode.D)) forceDemo = !forceDemo;
            if (Input.GetKeyDown(KeyCode.L)) swapLeftRight = !swapLeftRight;
            if (Input.GetKeyDown(KeyCode.Y)) extraYaw = Mathf.Repeat(extraYaw + 180f, 360f);
            if (Input.GetKeyDown(KeyCode.M)) showMarkers = !showMarkers;
            if (Input.GetKeyDown(KeyCode.V)) showMannequin = !showMannequin;
        }

        void LateUpdate()
        {
            _driver.EnsureHumanoid(humanoidAnimator);
            int hash = HashLinks(genericBones);
            if (hash != _genericHash)
            {
                _genericHash = hash;
                _driver.BindGeneric(genericBones);
                _snap = true;
            }
            if (hideMannequinWhenHumanoid && _driver.IsBound && !_hidForHumanoid)
            {
                showMannequin = false;
                _hidForHumanoid = true;
            }
            _mannequin.Visible = showMannequin;

            if (spaceMode != _lastSpace || swapLeftRight != _lastSwap)
            {
                _lastSpace = spaceMode;
                _lastSwap = swapLeftRight;
                _smoother.Reset();
                _solver.Recalibrate();
                _snap = true;
            }

            float yaw = extraYaw + transform.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) > 0.5f)
                _snap = true;
            _lastYaw = yaw;

            int packets;
            float age;
            bool live = !forceDemo && _receiver.TryCopy(_packet, out packets, out age) && packets > 0;
            _packets = packets;
            _age = age;
            if (live != _wasLive)
            {
                _smoother.Reset();
                _solver.Recalibrate();
                _snap = true;
                _wasLive = live;
            }
            if (live && !_loggedLive)
            {
                _loggedLive = true;
                Debug.Log("Skeleton stream connected. " + _receiver.Description);
            }

            if (live)
            {
                // _packet already filled
            }
            else
            {
                DemoPoseSource.Fill(_packet, Time.time);
            }

            PoseMath.ConvertToUnity(_packet, _unity, spaceMode, swapLeftRight);
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _smoother.Smooth(_unity, _smooth, responsiveness, dt, confidenceThreshold, maxJointJumpInTorsos);
            if (!_solver.TrySolve(_smooth, _solved))
            {
                _reject = _solved.RejectReason;
                return;
            }

            _reject = "";
            _solved.CopyYawTo(_yawed, yaw);
            _mannequin.Apply(_yawed, transform.position, dt, _snap, maxDegreesPerSecond);
            _driver.Apply(_yawed, _mannequin.TorsoLength, dt, _snap, maxDegreesPerSecond);
            Vector3 anchor = transform.position + Vector3.up * _mannequin.HipHeight;
            _markers.SetVisible(showMarkers, transform);
            _markers.Apply(_yawed, anchor, _mannequin.TorsoLength);
            _snap = false;
        }

        void OnDestroy()
        {
            _receiver.Stop();
            _mannequin.Dispose();
            _markers.Dispose();
            if (_floorMaterial != null)
                Destroy(_floorMaterial);
        }

        void OnGUI()
        {
            if (!_showGui)
                return;

            const float width = 520f;
            float height = string.IsNullOrEmpty(_receiver.Error) ? 214f : 250f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);
            GUILayout.Label("Skeleton Pose Bridge");
            if (forceDemo)
                GUILayout.Label("Source: built-in demo walk (network ignored)");
            else if (_packets > 0)
                GUILayout.Label("Source: " + _receiver.Description + "    age " + _age.ToString("0.00") + "s    packets " + _packets);
            else
                GUILayout.Label("Source: built-in demo walk, waiting for UDP " + listenPort);

            if (_packets > 0 && _age > 0.5f && !forceDemo)
                GUILayout.Label("Stream paused. Holding the last pose.");
            if (!string.IsNullOrEmpty(_reject))
                GUILayout.Label("Holding pose: " + _reject);
            else
                GUILayout.Label("Held joints: " + _smoother.Held + "    " + _driver.Status);
            if (!string.IsNullOrEmpty(_receiver.Error))
                GUILayout.Label(_receiver.Error);

            GUILayout.Label("Scale and child bone positions are locked. Joints drive rotation only.");
            GUILayout.Label("C calibrate   D demo   L swap sides   Y turn 180   M markers   V figure   G hide");
            GUILayout.EndArea();
        }

        void EnsureEnvironment()
        {
            if (Camera.main == null)
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                var camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
                camera.fieldOfView = 42f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 80f;
                cameraGo.AddComponent<AudioListener>();
                cameraGo.transform.position = new Vector3(0f, 1.15f, -3.4f);
                cameraGo.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            }

            if (AllLights().Length == 0)
            {
                var key = new GameObject("Key Light");
                var light = key.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                key.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            if (GameObject.Find("PoseFillLight") == null)
            {
                var fillGo = new GameObject("PoseFillLight");
                fillGo.transform.SetParent(transform, false);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.35f;
                fill.color = new Color(0.75f, 0.84f, 1f);
                fill.shadows = LightShadows.None;
                fillGo.transform.rotation = Quaternion.Euler(15f, 165f, 0f);
            }

            if (GameObject.Find("StageFloor") == null)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "StageFloor";
                floor.transform.position = Vector3.zero;
                floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
                floor.isStatic = true;
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _floorMaterial = new Material(shader);
                Color color = new Color(0.18f, 0.20f, 0.22f);
                if (_floorMaterial.HasProperty("_Color")) _floorMaterial.SetColor("_Color", color);
                if (_floorMaterial.HasProperty("_BaseColor")) _floorMaterial.SetColor("_BaseColor", color);
                _floorMaterial.color = color;
                floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
            }
        }

        static Light[] AllLights()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<Light>();
#endif
        }

        static int HashLinks(GenericBoneLink[] links)
        {
            if (links == null)
                return 0;
            unchecked
            {
                int hash = links.Length;
                for (int i = 0; i < links.Length; i++)
                {
                    GenericBoneLink link = links[i];
                    if (link == null)
                    {
                        hash = hash * 31 + 1;
                        continue;
                    }
                    int id = link.bone == null ? 0 : link.bone.GetInstanceID();
                    hash = hash * 31 + id;
                    hash = hash * 31 + (int)link.from;
                    hash = hash * 31 + (int)link.to;
                    hash = hash * 31 + (link.isRoot ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
