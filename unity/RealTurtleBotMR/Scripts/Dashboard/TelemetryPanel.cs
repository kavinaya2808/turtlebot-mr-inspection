using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealTurtleBotMR
{
    /// <summary>
    /// The main MR dashboard for the real TurtleBot: header (identity, status, link),
    /// live RealSense camera, and battery / motion / pose cards.
    /// Appears beside you when the app starts; press B (right controller) to bring it back.
    /// UI references are created and wired by the "RealTurtleBotMR > Set Up Scene" menu.
    /// </summary>
    public class TelemetryPanel : MonoBehaviour
    {
        [Header("Data")]
        public TurtleBotTelemetry telemetry;
        public RobotWorldAnchor anchor;
        public RobotCameraFeed cameraFeed;
        public NavGoalSender nav;

        [Header("Placement (relative to your head)")]
        public float distance = 1.0f;
        public float sideOffset = 0.42f;     // + right
        public float heightOffset = -0.18f;
        public OVRInput.Button recenterButton = OVRInput.Button.Two;     // B
        public OVRInput.Controller recenterController = OVRInput.Controller.RTouch;
        public float refreshHz = 12f;
        [Tooltip("TurtleBot3 Burger top speed, for the speed bar")]
        public float maxLinear = 0.22f;
        public float maxAngularDeg = 160f;

        [Header("UI – header (auto-wired)")]
        public TextMeshProUGUI title;
        public TextMeshProUGUI subtitle;
        public Image statusPill;
        public Image statusDot;
        public TextMeshProUGUI statusText;
        public Image linkDot;
        public TextMeshProUGUI linkText;

        [Header("UI – camera (auto-wired)")]
        public TextMeshProUGUI cameraInfo;
        public Image cameraLiveDot;

        [Header("UI – battery (auto-wired)")]
        public TextMeshProUGUI batteryValue;
        public TextMeshProUGUI batterySub;
        public Image batteryFill;
        public RectTransform batteryFillRect;

        [Header("UI – motion (auto-wired)")]
        public TextMeshProUGUI speedValue;
        public TextMeshProUGUI turnValue;
        public RectTransform speedBarRect;
        public RectTransform turnBarRect;

        [Header("UI – pose (auto-wired)")]
        public TextMeshProUGUI xValue;
        public TextMeshProUGUI yValue;
        public TextMeshProUGUI headingValue;
        public RectTransform compassNeedle;

        [Header("UI – footer (auto-wired)")]
        public TextMeshProUGUI hintText;

        float nextRefresh, startTime;
        bool placed;
        float shownBattery, shownSpeed, shownTurn;

        void Start() { startTime = Time.time; }

        void Update()
        {
            if (!placed && Time.time - startTime > 0.6f) { PlaceInFrontOfUser(); placed = true; }
            if (OVRInput.GetDown(recenterButton, recenterController)) PlaceInFrontOfUser();
            if (telemetry == null) return;

            AnimateBars();
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 1f / refreshHz;
            Refresh();
        }

        public void PlaceInFrontOfUser()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Transform h = cam.transform;
            Vector3 f = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            Vector3 r = Vector3.Cross(Vector3.up, f);
            transform.position = h.position + f * distance + r * sideOffset + Vector3.up * heightOffset;
            Vector3 look = transform.position - h.position;
            look.y = 0f;
            transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        void AnimateBars()
        {
            var t = telemetry;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            shownBattery = Mathf.Lerp(shownBattery, t.HasBattery ? t.BatteryPercent / 100f : 0f, k);
            shownSpeed = Mathf.Lerp(shownSpeed, Mathf.Clamp(t.LinearSpeed / maxLinear, -1f, 1f), k);
            shownTurn = Mathf.Lerp(shownTurn, Mathf.Clamp(t.AngularSpeed * Mathf.Rad2Deg / maxAngularDeg, -1f, 1f), k);

            if (batteryFillRect) SetFill(batteryFillRect, 0f, Mathf.Max(0.02f, shownBattery));
            if (speedBarRect) SetCentered(speedBarRect, shownSpeed);
            if (turnBarRect) SetCentered(turnBarRect, -shownTurn);   // left turn (+z) fills to the left
            if (compassNeedle && t.HasOdom)
            {
                // ROS heading is CCW-positive; UI rotation around +Z is also CCW.
                var target = Quaternion.Euler(0, 0, t.HeadingRosDeg);
                compassNeedle.localRotation = Quaternion.Slerp(compassNeedle.localRotation, target, k);
            }
        }

        static void SetFill(RectTransform r, float min, float max)
        {
            r.anchorMin = new Vector2(min, 0); r.anchorMax = new Vector2(max, 1);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        /// <summary>Bar grows from the centre: right for +v, left for −v.</summary>
        static void SetCentered(RectTransform r, float v)
        {
            float a = 0.5f, b = 0.5f + v * 0.5f;
            SetFill(r, Mathf.Min(a, b), Mathf.Max(a, b) + 0.004f);
        }

        void Refresh()
        {
            var t = telemetry;
            string muted = MRStyle.Hex(MRStyle.TextMuted);
            var s = t.Status;
            Color sc = MRStyle.StatusColor(s);

            if (title) title.text = t.robotId;
            if (subtitle) subtitle.text = $"TurtleBot3 Burger  <color={muted}>·  {t.FullOdomTopic}  ·  {t.FullBatteryTopic}</color>";

            if (statusText) { statusText.text = MRStyle.StatusLabel(s); statusText.color = sc; }
            if (statusDot) statusDot.color = sc;
            if (statusPill) statusPill.color = new Color(sc.r, sc.g, sc.b, 0.14f);

            Color lc; string lt;
            if (!t.RosLinked) { lc = MRStyle.Red; lt = "NO LINK"; }
            else if (s == TurtleBotTelemetry.RobotStatus.NoData) { lc = MRStyle.Amber; lt = "NO DATA"; }
            else { lc = MRStyle.Green; lt = $"LIVE  <color={muted}>{t.OdomRateHz:0} Hz</color>"; }
            if (linkDot) linkDot.color = lc;
            if (linkText) { linkText.text = lt; linkText.color = lc; }

            // Camera
            if (cameraFeed && cameraInfo)
            {
                bool live = cameraFeed.IsLive;
                cameraInfo.text = live
                    ? $"REALSENSE  <color={muted}>{cameraFeed.Width}×{cameraFeed.Height} · {cameraFeed.Fps:0} fps</color>"
                    : $"REALSENSE  <color={muted}>offline</color>";
                if (cameraLiveDot) cameraLiveDot.color = live ? MRStyle.Red : MRStyle.Grey;   // red = recording/live
            }

            // Battery
            if (batteryValue)
            {
                batteryValue.text = t.HasBattery ? $"{t.BatteryPercent:0}<size=50%> %</size>" : "--<size=50%> %</size>";
                batteryValue.color = t.HasBattery ? MRStyle.BatteryColor(t.BatteryPercent) : MRStyle.TextPrimary;
            }
            if (batterySub) batterySub.text = t.HasBattery
                ? $"{t.BatteryVoltage:0.0} V{(t.IsCharging ? "  ·  charging" : "  ·  motor battery")}"
                : "waiting for /battery_state";
            if (batteryFill) batteryFill.color = t.HasBattery ? MRStyle.BatteryColor(t.BatteryPercent) : MRStyle.Grey;

            // Motion
            bool odom = t.HasOdom;
            if (speedValue) speedValue.text = odom ? $"{t.LinearSpeed:+0.00;-0.00;0.00}<size=50%> m/s</size>" : "--";
            if (turnValue) turnValue.text = odom ? $"{t.AngularSpeed * Mathf.Rad2Deg:+0;-0;0}<size=50%> °/s</size>" : "--";

            // Pose
            if (xValue) xValue.text = odom ? $"{t.OdomPositionRos.x:+0.00;-0.00;0.00}<size=60%> m</size>" : "--";
            if (yValue) yValue.text = odom ? $"{t.OdomPositionRos.y:+0.00;-0.00;0.00}<size=60%> m</size>" : "--";
            if (headingValue) headingValue.text = odom ? $"{t.HeadingRosDeg:0}<size=60%>°</size>" : "--";

            if (hintText)
            {
                string hl = MRStyle.Hex(MRStyle.Cyan);
                string ok = MRStyle.Hex(MRStyle.Green);
                string amber = MRStyle.Hex(MRStyle.Amber);
                string navStatus = nav != null ? nav.StatusText : "";
                bool navBusy = navStatus.StartsWith("Goal") || navStatus.StartsWith("Driving") || navStatus.StartsWith("Arrived");

                if (anchor == null || !anchor.IsAligned)
                    hintText.text = $"<color={hl}>Align</color>  rest the right controller on the robot, pointing where it faces, press <color={hl}>A</color>";
                else if (navBusy)
                    hintText.text = $"<color={amber}>{navStatus}</color>   ·   A re-align   ·   B panel";
                else if (anchor.InMapFrame)
                    hintText.text = $"<color={ok}>Aligned on map</color>   aim at floor + <color={hl}>right stick click</color>: send robot   ·   X map   ·   A re-align";
                else
                    hintText.text = $"<color={ok}>Aligned</color>   grip + stick: fine-tune  ·  A: re-align  ·  B: panel  ·  start SLAM for point-and-go";
            }
        }
    }
}
