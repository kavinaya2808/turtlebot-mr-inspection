using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Sensor;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Listens to ONE real TurtleBot over ROS-TCP-Connector and keeps its latest telemetry
    /// in plain fields that the MR dashboard reads. It never publishes anything:
    /// the real robot owns its own /tf, /odom and /clock.
    ///
    /// Topics are configurable so the same script works for TurtleBot 3 and 4
    /// (with or without a namespace, e.g. "/tb4").
    /// </summary>
    [DisallowMultipleComponent]
    public class TurtleBotTelemetry : MonoBehaviour
    {
        public enum RobotStatus { NoLink, NoData, Charging, LowBattery, Moving, Idle }

        [Header("Identity")]
        public string robotId = "TB3-01";

        [Header("Topics")]
        [Tooltip("Optional namespace, e.g. \"/tb4\". Leave empty for no namespace.")]
        public string topicNamespace = "";
        public string odomTopic = "odom";
        public string batteryTopic = "battery_state";
        [Tooltip("SLAM correction map -> odom, published by tb3_mr_inspection's mr_frames_publisher")]
        public string mapToOdomTopic = "/mr/map_to_odom";
        [Tooltip("Use the map frame (drift-free) whenever the SLAM correction is fresh")]
        public bool preferMapFrame = true;
        public float mapCorrectionTimeout = 2.0f;

        [Header("Status rules")]
        [Tooltip("Odometry older than this counts as 'no data'.")]
        public float staleAfterSeconds = 1.5f;
        [Range(0, 100)] public float lowBatteryPercent = 20f;
        public float movingLinearThreshold = 0.02f;    // m/s
        public float movingAngularThreshold = 0.10f;   // rad/s

        [Header("Battery fallback (if the robot only reports voltage)")]
        [Tooltip("TurtleBot 3 uses a 3S LiPo: ~12.6 V full, ~11.0 V empty.")]
        public float batteryFullVoltage = 12.6f;
        public float batteryEmptyVoltage = 11.0f;

        // ---------- Live data (read by the dashboard) ----------
        public bool HasOdom { get; private set; }
        public bool HasBattery { get; private set; }

        /// <summary>Robot position in the odom frame, already converted to Unity axes, y = 0.</summary>
        public Vector3 OdomPositionUnity { get; private set; }
        /// <summary>Robot yaw in the odom frame, Unity convention (degrees, around +Y).</summary>
        public float OdomYawUnity { get; private set; }

        /// <summary>Position in the ROS odom frame (x forward, y left) for display.</summary>
        public Vector2 OdomPositionRos { get; private set; }
        /// <summary>Heading in ROS convention: degrees, counter-clockwise from +x.</summary>
        public float HeadingRosDeg { get; private set; }

        public float LinearSpeed { get; private set; }     // m/s  (twist.linear.x)
        public float AngularSpeed { get; private set; }    // rad/s (twist.angular.z)

        public float BatteryPercent { get; private set; }  // 0..100
        public float BatteryVoltage { get; private set; }
        public bool IsCharging { get; private set; }

        /// <summary>True while slam_toolbox's map -> odom correction arrives.</summary>
        public bool UsingMapFrame => preferMapFrame && hasMapToOdom &&
                                     Time.realtimeSinceStartup - lastMapToOdomTime < mapCorrectionTimeout;

        /// <summary>Robot position in the tracking frame (map when SLAM runs, else odom), Unity axes, y = 0.</summary>
        public Vector3 FramePositionUnity => UsingMapFrame ? mapToOdomRot * OdomPositionUnity + mapToOdomPos : OdomPositionUnity;
        /// <summary>Robot yaw in the tracking frame, Unity convention (degrees).</summary>
        public float FrameYawUnity => UsingMapFrame ? mapToOdomRot.eulerAngles.y + OdomYawUnity : OdomYawUnity;

        public float OdomRateHz { get; private set; }
        public float SecondsSinceOdom => HasOdom ? Time.realtimeSinceStartup - lastOdomTime : float.PositiveInfinity;

        public bool RosLinked => ros != null && ros.HasConnectionThread && !ros.HasConnectionError;
        public RobotStatus Status { get; private set; } = RobotStatus.NoLink;

        public string FullOdomTopic => Join(topicNamespace, odomTopic);
        public string FullBatteryTopic => Join(topicNamespace, batteryTopic);

        ROSConnection ros;
        float lastOdomTime;
        bool hasMapToOdom;
        float lastMapToOdomTime;
        Vector3 mapToOdomPos;
        Quaternion mapToOdomRot = Quaternion.identity;
        float rateWindowStart;
        int rateCount;

        void Start()
        {
            ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<OdometryMsg>(FullOdomTopic, OnOdom);
            ros.Subscribe<BatteryStateMsg>(FullBatteryTopic, OnBattery);
            if (!string.IsNullOrEmpty(mapToOdomTopic))
                ros.Subscribe<PoseStampedMsg>(mapToOdomTopic, OnMapToOdom);
            Debug.Log($"[TurtleBotTelemetry] {robotId}: listening on {FullOdomTopic} and {FullBatteryTopic}");
        }

        void OnOdom(OdometryMsg msg)
        {
            // Messages are handed to us in batches once per frame, so per-message gaps are
            // meaningless. Count messages over ~1 s windows instead.
            float now = Time.realtimeSinceStartup;
            if (rateWindowStart <= 0f) rateWindowStart = now;
            rateCount++;
            if (now - rateWindowStart >= 1f)
            {
                OdomRateHz = rateCount / (now - rateWindowStart);
                rateCount = 0;
                rateWindowStart = now;
            }
            lastOdomTime = now;
            HasOdom = true;

            var p = msg.pose.pose.position;
            var q = msg.pose.pose.orientation;

            // ROS (x fwd, y left, z up)  ->  Unity (x right, y up, z fwd)
            Vector3 u = p.From<FLU>();
            u.y = 0f;
            OdomPositionUnity = u;
            OdomYawUnity = q.From<FLU>().eulerAngles.y;

            OdomPositionRos = new Vector2((float)p.x, (float)p.y);
            float yawRos = Mathf.Atan2(2f * (float)(q.w * q.z + q.x * q.y),
                                       1f - 2f * (float)(q.y * q.y + q.z * q.z));
            HeadingRosDeg = yawRos * Mathf.Rad2Deg;

            LinearSpeed = (float)msg.twist.twist.linear.x;
            AngularSpeed = (float)msg.twist.twist.angular.z;
        }

        void OnMapToOdom(PoseStampedMsg msg)
        {
            Vector3 p = msg.pose.position.From<FLU>();
            p.y = 0f;
            float yaw = msg.pose.orientation.From<FLU>().eulerAngles.y;
            mapToOdomPos = p;
            mapToOdomRot = Quaternion.Euler(0f, yaw, 0f);
            hasMapToOdom = true;
            lastMapToOdomTime = Time.realtimeSinceStartup;
        }

        void OnBattery(BatteryStateMsg msg)
        {
            HasBattery = true;
            BatteryVoltage = msg.voltage;
            IsCharging = msg.power_supply_status == BatteryStateMsg.POWER_SUPPLY_STATUS_CHARGING;

            float pct = msg.percentage;
            if (float.IsNaN(pct) || pct <= 0f)
            {
                // Some robots (TurtleBot 3) only fill in voltage -> estimate.
                pct = Mathf.InverseLerp(batteryEmptyVoltage, batteryFullVoltage, msg.voltage) * 100f;
            }
            else if (pct <= 1.0001f)
            {
                pct *= 100f;                    // ROS standard is 0..1
            }
            BatteryPercent = Mathf.Clamp(pct, 0f, 100f);
        }

        void Update()
        {
            Status = ComputeStatus();
        }

        RobotStatus ComputeStatus()
        {
            if (!RosLinked) return RobotStatus.NoLink;
            if (!HasOdom || SecondsSinceOdom > staleAfterSeconds) return RobotStatus.NoData;
            if (HasBattery && IsCharging) return RobotStatus.Charging;
            if (HasBattery && BatteryPercent <= lowBatteryPercent) return RobotStatus.LowBattery;
            if (Mathf.Abs(LinearSpeed) > movingLinearThreshold ||
                Mathf.Abs(AngularSpeed) > movingAngularThreshold) return RobotStatus.Moving;
            return RobotStatus.Idle;
        }

        static string Join(string ns, string topic)
        {
            topic = (topic ?? "").Trim('/');
            ns = (ns ?? "").Trim('/');
            return string.IsNullOrEmpty(ns) ? "/" + topic : "/" + ns + "/" + topic;
        }
    }
}
