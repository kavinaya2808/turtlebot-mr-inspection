using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Nav;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Draws slam_toolbox's live occupancy grid (/map) flat on the real floor: walls and obstacles
    /// glow, free space is a faint tint, unknown space stays clear. It is placed with the same
    /// alignment as the robot, so it only shows while the robot is tracked in the map frame.
    /// X (left controller) toggles it.
    /// </summary>
    public class MapOverlay : MonoBehaviour
    {
        public RobotWorldAnchor anchor;
        public string mapTopic = "/map";
        public bool visible = true;
        public OVRInput.Button toggleButton = OVRInput.Button.One;              // X
        public OVRInput.Controller toggleController = OVRInput.Controller.LTouch;
        public float heightAboveFloor = 0.006f;

        [Header("Colours")]
        public Color occupied = new Color(0.25f, 0.84f, 1.00f, 0.85f);
        public Color free = new Color(0.25f, 0.84f, 1.00f, 0.08f);

        public bool HasMap { get; private set; }
        public int CellsKnown { get; private set; }

        Transform frame;          // pose of the map frame's origin in the room
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Texture2D tex;
        Color32[] pixels;
        OccupancyGridMsg pending;

        void Start()
        {
            var go = new GameObject("SlamMap");
            frame = go.transform;
            var quad = new GameObject("Grid");
            quad.transform.SetParent(frame, false);
            meshFilter = quad.AddComponent<MeshFilter>();
            meshRenderer = quad.AddComponent<MeshRenderer>();
            meshRenderer.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3000 };
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;

            ROSConnection.GetOrCreateInstance().Subscribe<OccupancyGridMsg>(mapTopic, m => pending = m);
        }

        void Update()
        {
            if (OVRInput.GetDown(toggleButton, toggleController)) visible = !visible;

            if (pending != null)
            {
                var m = pending; pending = null;
                Rebuild(m);
            }

            bool show = visible && HasMap && anchor != null && anchor.InMapFrame;
            meshRenderer.enabled = show;
            if (!show) return;

            Pose o = anchor.FrameOriginInWorld;
            frame.SetPositionAndRotation(o.position + Vector3.up * heightAboveFloor, o.rotation);
        }

        void Rebuild(OccupancyGridMsg m)
        {
            int w = (int)m.info.width, h = (int)m.info.height;
            if (w <= 0 || h <= 0 || m.data == null || m.data.Length < w * h) return;
            float res = m.info.resolution;

            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                pixels = new Color32[w * h];
                meshRenderer.material.mainTexture = tex;
            }

            Color32 occ = occupied, fre = free, unknown = new Color32(0, 0, 0, 0);
            int known = 0;
            for (int i = 0; i < w * h; i++)
            {
                sbyte v = m.data[i];                  // row-major, row 0 = map y = 0
                if (v < 0) pixels[i] = unknown;
                else { pixels[i] = v >= 50 ? occ : fre; known++; }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            CellsKnown = known;

            // Grid corners in the map frame (ROS x/y), rotated by the grid origin, then ROS -> Unity.
            var origin = m.info.origin;
            Vector3 o = origin.position.From<FLU>();
            Quaternion r = Quaternion.Euler(0f, origin.orientation.From<FLU>().eulerAngles.y, 0f);
            Vector3 ex = r * new Vector3(0f, 0f, w * res);    // along map +x (Unity +z)
            Vector3 ey = r * new Vector3(-h * res, 0f, 0f);   // along map +y (Unity -x)
            o.y = 0f;

            var mesh = meshFilter.mesh;
            mesh.Clear();
            mesh.vertices = new[] { o, o + ex, o + ex + ey, o + ey };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            HasMap = true;
        }

        void OnDestroy() { if (tex) Destroy(tex); }
    }
}
