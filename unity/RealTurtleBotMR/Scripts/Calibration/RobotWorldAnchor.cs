using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Places this GameObject exactly where the real robot is in the room.
    ///
    /// The robot only knows its pose in its own <c>odom</c> frame; the Quest has its own world
    /// frame. We learn the transform between them once ("alignment"):
    ///   1. Rest the right controller flat on top of the robot, pointing where the robot faces.
    ///   2. Press A.
    /// From then on:  worldPose = alignment * odomPose.
    ///
    /// Fine-tune while aligned:
    ///   hold RIGHT grip + right stick  -> slide (relative to where you look)
    ///   hold LEFT grip  + left stick X -> rotate
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotWorldAnchor : MonoBehaviour
    {
        public TurtleBotTelemetry telemetry;

        [Header("Controls")]
        public OVRInput.Button alignButton = OVRInput.Button.One;          // A
        public OVRInput.Controller alignController = OVRInput.Controller.RTouch;
        public float nudgeSpeed = 0.25f;     // m/s
        public float rotateSpeed = 40f;      // deg/s

        [Header("Smoothing")]
        [Tooltip("Higher = snappier. Odometry arrives at ~20-30 Hz, this hides the steps.")]
        public float followSharpness = 18f;

        public bool IsAligned { get; private set; }
        public float FloorY { get; private set; }

        Vector3 originPos;          // where the tracking frame's (0,0) is in the room
        Quaternion originRot = Quaternion.identity;
        OVRCameraRig rig;
        bool lastFrameWasMap;

        /// <summary>True when aligned and the robot is tracked in the SLAM map frame.</summary>
        public bool InMapFrame => IsAligned && telemetry != null && telemetry.UsingMapFrame;

        /// <summary>Tracking-frame point (Unity axes, as from FLU) -> room position.</summary>
        public Vector3 FrameToWorld(Vector3 frameUnity)
        {
            Vector3 w = originPos + originRot * frameUnity;
            w.y = FloorY;
            return w;
        }

        /// <summary>Room position -> tracking-frame point (Unity axes, y = 0).</summary>
        public Vector3 WorldToFrame(Vector3 world)
        {
            Vector3 f = Quaternion.Inverse(originRot) * (world - originPos);
            f.y = 0f;
            return f;
        }

        /// <summary>Pose of the tracking frame's origin in the room (for drawing the map).</summary>
        public Pose FrameOriginInWorld => new Pose(new Vector3(originPos.x, FloorY, originPos.z), originRot);

        void Start()
        {
            rig = FindObjectOfType<OVRCameraRig>();
        }

        void Update()
        {
            FloorY = ResolveFloorY();

            if (OVRInput.GetDown(alignButton, alignController))
                AlignToController();

            if (!IsAligned || telemetry == null) return;

            // SLAM started or stopped: re-anchor in the new frame so the robot stays where it is.
            bool isMap = telemetry.UsingMapFrame;
            if (isMap != lastFrameWasMap)
            {
                lastFrameWasMap = isMap;
                SetAlignment(transform.position, transform.eulerAngles.y);
                Debug.Log($"[RobotWorldAnchor] Tracking in {(isMap ? "map (SLAM)" : "odom")} frame");
            }

            FineTune();

            Vector3 targetPos = originPos + originRot * telemetry.FramePositionUnity;
            targetPos.y = FloorY;
            Quaternion targetRot = originRot * Quaternion.Euler(0f, telemetry.FrameYawUnity, 0f);

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, k),
                Quaternion.Slerp(transform.rotation, targetRot, k));
        }

        /// <summary>Robot is under the right controller, facing where the controller points.</summary>
        public void AlignToController()
        {
            if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
            if (rig == null || rig.rightControllerAnchor == null || telemetry == null) return;

            Transform c = rig.rightControllerAnchor;
            Vector3 fwd = Vector3.ProjectOnPlane(c.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(c.up, Vector3.up);
            float worldYaw = Quaternion.LookRotation(fwd.normalized, Vector3.up).eulerAngles.y;

            Vector3 worldPos = new Vector3(c.position.x, FloorY, c.position.z);
            SetAlignment(worldPos, worldYaw);

            // Snap immediately instead of gliding in from wherever we were.
            transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, worldYaw, 0f));
            OVRInput.SetControllerVibration(0.4f, 0.4f, alignController);
            Invoke(nameof(StopVibration), 0.12f);
            Debug.Log($"[RobotWorldAnchor] Aligned at {worldPos}, yaw {worldYaw:F1}°");
        }

        /// <summary>Declare that the robot currently stands at worldPos with worldYaw.</summary>
        public void SetAlignment(Vector3 worldPos, float worldYaw)
        {
            originRot = Quaternion.Euler(0f, worldYaw - telemetry.FrameYawUnity, 0f);
            originPos = worldPos - originRot * telemetry.FramePositionUnity;
            originPos.y = 0f;
            lastFrameWasMap = telemetry.UsingMapFrame;
            IsAligned = true;
        }

        void FineTune()
        {
            bool rightGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            if (!rightGrip && !leftGrip) return;

            Vector3 robotNow = originPos + originRot * telemetry.FramePositionUnity;
            float robotYaw = (originRot * Quaternion.Euler(0f, telemetry.FrameYawUnity, 0f)).eulerAngles.y;

            if (rightGrip)
            {
                Vector2 s = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
                Transform head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main.transform;
                Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                Vector3 r = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
                robotNow += (r * s.x + f * s.y) * nudgeSpeed * Time.deltaTime;
            }
            if (leftGrip)
            {
                float sx = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch).x;
                robotYaw += sx * rotateSpeed * Time.deltaTime;
            }
            SetAlignment(robotNow, robotYaw);
        }

        float ResolveFloorY()
        {
            var mruk = MRUK.Instance;
            var room = mruk != null ? mruk.GetCurrentRoom() : null;
            var floors = room != null ? room.FloorAnchors : null;
            if (floors != null && floors.Count > 0 && floors[0] != null) return floors[0].transform.position.y;
            return 0f; // OVRCameraRig uses a floor-level tracking origin in this project
        }

        void StopVibration() => OVRInput.SetControllerVibration(0f, 0f, alignController);
    }
}
