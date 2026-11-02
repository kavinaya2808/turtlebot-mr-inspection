using UnityEngine;

namespace RealTurtleBotMR
{
    /// <summary>One place for the dashboard's colours and status wording.</summary>
    public static class MRStyle
    {
        public static readonly Color PanelBg       = new Color(0.04f, 0.06f, 0.09f, 0.86f);
        public static readonly Color TileBg        = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color Track         = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Divider       = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color TextPrimary   = new Color(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Color TextSecondary = new Color(0.68f, 0.72f, 0.78f, 1f);
        public static readonly Color TextMuted     = new Color(0.47f, 0.51f, 0.58f, 1f);

        public static readonly Color Cyan  = new Color(0.25f, 0.84f, 1.00f, 1f);
        public static readonly Color Green = new Color(0.33f, 0.90f, 0.56f, 1f);
        public static readonly Color Amber = new Color(1.00f, 0.73f, 0.27f, 1f);
        public static readonly Color Red   = new Color(1.00f, 0.38f, 0.38f, 1f);
        public static readonly Color Blue  = new Color(0.50f, 0.62f, 1.00f, 1f);
        public static readonly Color Grey  = new Color(0.55f, 0.58f, 0.64f, 1f);

        public static Color StatusColor(TurtleBotTelemetry.RobotStatus s)
        {
            switch (s)
            {
                case TurtleBotTelemetry.RobotStatus.NoLink:     return Red;
                case TurtleBotTelemetry.RobotStatus.NoData:     return Grey;
                case TurtleBotTelemetry.RobotStatus.Charging:   return Blue;
                case TurtleBotTelemetry.RobotStatus.LowBattery: return Amber;
                case TurtleBotTelemetry.RobotStatus.Moving:     return Cyan;
                default:                                        return Green;
            }
        }

        public static string StatusLabel(TurtleBotTelemetry.RobotStatus s)
        {
            switch (s)
            {
                case TurtleBotTelemetry.RobotStatus.NoLink:     return "NO ROS LINK";
                case TurtleBotTelemetry.RobotStatus.NoData:     return "WAITING FOR DATA";
                case TurtleBotTelemetry.RobotStatus.Charging:   return "CHARGING";
                case TurtleBotTelemetry.RobotStatus.LowBattery: return "LOW BATTERY";
                case TurtleBotTelemetry.RobotStatus.Moving:     return "MOVING";
                default:                                        return "IDLE";
            }
        }

        public static Color BatteryColor(float pct)
        {
            if (pct <= 20f) return Red;
            if (pct <= 45f) return Amber;
            return Green;
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Line material that works in URP without any asset.</summary>
        public static Material LineMaterial()
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            m.renderQueue = 3100;
            return m;
        }
    }
}
