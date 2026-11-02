using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Point-and-go: aim the right controller at the floor, a target ring follows the aim point;
    /// click the right thumbstick to send the robot there (Nav2 goal on /goal_pose, map frame).
    /// The planned path (/plan) is drawn on the floor. Needs SLAM + Nav2 running and the robot
    /// aligned in the map frame.
    /// </summary>
    public class NavGoalSender : MonoBehaviour
    {
        public RobotWorldAnchor anchor;
        public TurtleBotTelemetry telemetry;

        [Header("ROS")]
        public string goalTopic = "/goal_pose";
        public string planTopic = "/plan";

        [Header("Input")]
        public OVRInput.Button sendButton = OVRInput.Button.PrimaryThumbstick;   // right stick click
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        public float maxAimDistance = 6f;
        public float arrivedDistance = 0.25f;

        public string StatusText { get; private set; } = "";

        OVRCameraRig rig;
        ROSConnection ros;
        LineRenderer aimRing, goalRing, goalPole, pathLine;
        PathMsg pendingPath;
        Transform visuals;        // world-fixed parent (this component sits on the moving robot)
        bool hasGoal;
        Vector3 goalWorld;

        void Start()
        {
            rig = FindObjectOfType<OVRCameraRig>();
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<PoseStampedMsg>(goalTopic);
            ros.Subscribe<PathMsg>(planTopic, m => pendingPath = m);

            visuals = new GameObject("NavGoalVisuals").transform;
            aimRing = Ring("AimRing", 0.12f, 0.006f);
            goalRing = Ring("GoalRing", 0.16f, 0.01f);
            goalPole = Line("GoalPole", 0.006f, 2);
            pathLine = Line("PlannedPath", 0.012f, 0);
            SetGoalVisible(false);
        }

        void Update()
        {
            bool ready = anchor != null && anchor.InMapFrame;

            // --- aim
            Vector3 aimPoint = default;
            bool aiming = ready && TryAim(out aimPoint);
            aimRing.enabled = aiming;
            if (aiming)
            {
                aimRing.transform.position = aimPoint + Vector3.up * 0.01f;
                if (OVRInput.GetDown(sendButton, controller)) SendGoal(aimPoint);
            }

            // --- path from Nav2
            if (pendingPath != null)
            {
                var p = pendingPath; pendingPath = null;
                DrawPath(p);
            }

            // --- goal state
            if (hasGoal && anchor != null)
            {
                Vector3 robot = anchor.transform.position;
                float d = Vector2.Distance(new Vector2(robot.x, robot.z), new Vector2(goalWorld.x, goalWorld.z));
                if (d < arrivedDistance)
                {
                    StatusText = "Arrived";
                    hasGoal = false;
                    SetGoalVisible(false);
                    pathLine.positionCount = 0;
                }
                else StatusText = $"Driving to goal · {d:0.00} m left";
            }
            else if (!ready)
            {
                StatusText = anchor != null && anchor.IsAligned
                    ? "Point-and-go needs SLAM (mapping_nav launch)"
                    : "";
            }
            else if (StatusText.StartsWith("Point") || StatusText == "") StatusText = "Aim at the floor, click right stick to send";

            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
            SetColor(aimRing, new Color(1f, 1f, 1f, 0.5f + 0.3f * pulse));
            SetColor(goalRing, new Color(MRStyle.Amber.r, MRStyle.Amber.g, MRStyle.Amber.b, pulse));
        }

        bool TryAim(out Vector3 point)
        {
            point = default;
            if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
            if (rig == null || rig.rightControllerAnchor == null) return false;
            Transform c = rig.rightControllerAnchor;
            float floor = anchor.FloorY;
            Vector3 dir = c.forward;
            if (dir.y > -0.05f) return false;                       // must point downwards
            float t = (floor - c.position.y) / dir.y;
            if (t <= 0f || t > maxAimDistance) return false;
            point = c.position + dir * t;
            return true;
        }

        void SendGoal(Vector3 world)
        {
            // Face the direction of travel: from the robot towards the goal.
            Vector3 robot = anchor.transform.position;
            Vector3 travel = world - robot; travel.y = 0f;
            float worldYaw = travel.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(travel).eulerAngles.y
                                                         : anchor.transform.eulerAngles.y;

            Vector3 f = anchor.WorldToFrame(world);                               // Unity axes, map frame
            float frameYaw = worldYaw - anchor.FrameOriginInWorld.rotation.eulerAngles.y;
            var pos = f.To<FLU>();                                                // ROS axes
            var rot = Quaternion.Euler(0f, frameYaw, 0f).To<FLU>();

            var msg = new PoseStampedMsg(
                new HeaderMsg(new TimeMsg(), "map"),
                new PoseMsg(new PointMsg(pos.x, pos.y, 0.0),
                            new QuaternionMsg(rot.x, rot.y, rot.z, rot.w)));
            ros.Publish(goalTopic, msg);

            hasGoal = true;
            goalWorld = world;
            goalRing.transform.position = world + Vector3.up * 0.012f;
            goalPole.SetPosition(0, world);
            goalPole.SetPosition(1, world + Vector3.up * 0.45f);
            SetGoalVisible(true);
            StatusText = $"Goal sent: ({pos.x:0.00}, {pos.y:0.00}) m";
            OVRInput.SetControllerVibration(0.5f, 0.5f, controller);
            Invoke(nameof(StopVibration), 0.1f);
            Debug.Log($"[NavGoalSender] Goal in map: x={pos.x:0.00} y={pos.y:0.00} yaw={-frameYaw:0}°");
        }

        void DrawPath(PathMsg p)
        {
            if (p.poses == null || p.poses.Length == 0 || anchor == null || !anchor.InMapFrame)
            {
                pathLine.positionCount = 0;
                return;
            }
            int step = Mathf.Max(1, p.poses.Length / 200);
            int n = (p.poses.Length + step - 1) / step;
            pathLine.positionCount = n;
            for (int i = 0, k = 0; i < p.poses.Length && k < n; i += step, k++)
            {
                Vector3 f = p.poses[i].pose.position.From<FLU>();
                f.y = 0f;
                pathLine.SetPosition(k, anchor.FrameToWorld(f) + Vector3.up * 0.015f);
            }
            SetColor(pathLine, new Color(MRStyle.Amber.r, MRStyle.Amber.g, MRStyle.Amber.b, 0.85f));
        }

        void SetGoalVisible(bool v)
        {
            goalRing.enabled = v;
            goalPole.enabled = v;
        }

        LineRenderer Ring(string name, float radius, float width)
        {
            var lr = Line(name, width, 48);
            lr.useWorldSpace = false;
            lr.loop = true;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            return lr;
        }

        LineRenderer Line(string name, float width, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visuals, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = MRStyle.LineMaterial();
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = points;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        static void SetColor(LineRenderer lr, Color c) { lr.startColor = lr.endColor = c; }
        void StopVibration() => OVRInput.SetControllerVibration(0f, 0f, controller);
    }
}
