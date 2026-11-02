using UnityEngine;

namespace RealTurtleBotMR
{
    /// <summary>
    /// A glowing ring + heading tick on the floor around the real robot.
    /// If it sits neatly around the robot, the alignment is good.
    /// </summary>
    public class RobotFootprint : MonoBehaviour
    {
        public TurtleBotTelemetry telemetry;
        public RobotWorldAnchor anchor;
        [Tooltip("TB3 Burger ≈ 0.11, TB3 Waffle ≈ 0.22, TB4 ≈ 0.18 (metres)")]
        public float radius = 0.12f;
        public int segments = 64;

        LineRenderer ring, arrow;
        const float Lift = -0.004f;   // 4 mm above the floor (local -Z is up)

        void Awake()
        {
            ring = MakeLine("FootprintRing", 0.006f);
            ring.loop = true;
            ring.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, Lift));
            }
            arrow = MakeLine("HeadingTick", 0.008f);
            arrow.positionCount = 2;
            arrow.SetPosition(0, new Vector3(0, radius * 0.55f, Lift));
            arrow.SetPosition(1, new Vector3(0, radius * 1.45f, Lift));
        }

        LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = MRStyle.LineMaterial();
            lr.startWidth = lr.endWidth = width;
            lr.alignment = LineAlignment.TransformZ;            // lie flat on the floor
            // Rotated so local XY = floor plane, local +Y = robot forward, local -Z = up.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void LateUpdate()
        {
            bool visible = anchor == null || anchor.IsAligned;
            ring.enabled = arrow.enabled = visible;
            if (!visible) return;

            Color c = telemetry != null ? MRStyle.StatusColor(telemetry.Status) : MRStyle.Grey;
            float pulse = 0.65f + 0.25f * Mathf.Sin(Time.time * 3f);
            ring.startColor = ring.endColor = new Color(c.r, c.g, c.b, pulse);
            arrow.startColor = new Color(c.r, c.g, c.b, 0.9f);
            arrow.endColor = new Color(c.r, c.g, c.b, 0.2f);
        }
    }
}
