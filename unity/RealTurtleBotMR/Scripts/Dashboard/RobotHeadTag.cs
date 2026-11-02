using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealTurtleBotMR
{
    /// <summary>
    /// The always-visible tag that floats above the real robot: ID, status and battery.
    /// Billboards toward you (yaw only), grows a little with distance so it stays readable,
    /// and draws a thin stem down to the robot so it is obvious which robot it belongs to.
    /// UI references are created and wired by the "RealTurtleBotMR > Set Up Scene" menu.
    /// </summary>
    public class RobotHeadTag : MonoBehaviour
    {
        [Header("Data")]
        public TurtleBotTelemetry telemetry;
        public RobotWorldAnchor anchor;
        [Tooltip("The robot root that RobotWorldAnchor moves.")]
        public Transform follow;

        [Header("Placement")]
        public float heightAboveFloor = 0.55f;
        [Tooltip("Top of the real robot – where the stem ends. TB3 Burger ≈ 0.19 m, TB4 ≈ 0.35 m.")]
        public float robotTopHeight = 0.20f;
        public float baseScale = 0.0005f;
        public float maxDistanceScale = 2.2f;

        [Header("UI (auto-wired)")]
        public CanvasGroup group;
        public Image statusDot;
        public TextMeshProUGUI idText;
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI batteryText;
        public Image batteryFill;
        public RectTransform batteryFillRect;

        LineRenderer stem;
        RectTransform rt;

        void Awake()
        {
            rt = GetComponent<RectTransform>();
            var go = new GameObject("Stem");
            go.transform.SetParent(transform.parent, false);
            stem = go.AddComponent<LineRenderer>();
            stem.material = MRStyle.LineMaterial();
            stem.positionCount = 2;
            stem.useWorldSpace = true;
            stem.startWidth = 0.003f;
            stem.endWidth = 0.0015f;
            stem.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stem.receiveShadows = false;
        }

        void LateUpdate()
        {
            bool visible = anchor == null || anchor.IsAligned;
            if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.deltaTime * 4f);
            stem.enabled = visible;
            if (!visible || follow == null) return;

            var cam = Camera.main;
            float floor = anchor != null ? anchor.FloorY : 0f;
            Vector3 basePos = follow.position;
            Vector3 tagPos = new Vector3(basePos.x, floor + heightAboveFloor, basePos.z);

            float scale = baseScale;
            if (cam != null)
            {
                Vector3 look = tagPos - cam.transform.position;
                float dist = look.magnitude;
                scale *= Mathf.Clamp(dist / 1.4f, 1f, maxDistanceScale);
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }

            // Lift by half the (scaled) tag height so its bottom edge sits at heightAboveFloor.
            float halfH = (rt != null ? rt.sizeDelta.y : 150f) * 0.5f * scale;
            transform.position = tagPos + Vector3.up * halfH;
            transform.localScale = Vector3.one * scale;

            Color sc = telemetry != null ? MRStyle.StatusColor(telemetry.Status) : MRStyle.Grey;
            stem.startColor = new Color(sc.r, sc.g, sc.b, 0.85f);
            stem.endColor = new Color(sc.r, sc.g, sc.b, 0.15f);
            stem.SetPosition(0, tagPos);
            stem.SetPosition(1, new Vector3(basePos.x, floor + robotTopHeight, basePos.z));

            Refresh(sc);
        }

        void Refresh(Color sc)
        {
            if (telemetry == null) return;
            var s = telemetry.Status;

            if (idText) idText.text = telemetry.robotId;
            if (statusDot) statusDot.color = sc;
            if (statusText)
            {
                string label = MRStyle.StatusLabel(s);
                if (s == TurtleBotTelemetry.RobotStatus.Moving)
                    label += $"  <color={MRStyle.Hex(MRStyle.TextSecondary)}>{Mathf.Abs(telemetry.LinearSpeed):0.00} m/s</color>";
                statusText.text = label;
                statusText.color = sc;
            }

            float pct = telemetry.HasBattery ? telemetry.BatteryPercent : 0f;
            if (batteryText) batteryText.text = telemetry.HasBattery ? $"{pct:0}<size=70%>%</size>" : "--<size=70%>%</size>";
            if (batteryFillRect)
            {
                var max = batteryFillRect.anchorMax;
                max.x = Mathf.Lerp(max.x, Mathf.Max(0.02f, pct / 100f), 0.2f);
                batteryFillRect.anchorMax = max;
            }
            if (batteryFill) batteryFill.color = telemetry.HasBattery ? MRStyle.BatteryColor(pct) : MRStyle.Grey;
        }
    }
}
