using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Shows the robot's RealSense colour stream on a RawImage.
    /// Subscribes to the JPEG-compressed topic (small enough for Wi-Fi) and decodes only the
    /// newest frame each Unity frame, so a slow network never builds up a backlog.
    ///
    /// On the robot:
    ///   ros2 launch realsense2_camera rs_launch.py enable_depth:=false pointcloud.enable:=false \
    ///     rgb_camera.color_profile:=424x240x15
    /// </summary>
    public class RobotCameraFeed : MonoBehaviour
    {
        public string topic = "/camera/camera/color/image_raw/compressed";
        public RawImage target;
        public AspectRatioFitter fitter;
        [Tooltip("Shown while no frames arrive")]
        public GameObject placeholder;
        public float staleAfterSeconds = 2f;

        public float Fps { get; private set; }
        public int Width => tex != null ? tex.width : 0;
        public int Height => tex != null ? tex.height : 0;
        public bool IsLive => hasFrame && Time.realtimeSinceStartup - lastFrameTime < staleAfterSeconds;

        Texture2D tex;
        byte[] pending;
        bool hasFrame;
        float lastFrameTime, fpsWindowStart;
        int fpsCount;

        void Start()
        {
            tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            ROSConnection.GetOrCreateInstance().Subscribe<CompressedImageMsg>(topic, msg => pending = msg.data);
            if (target) target.texture = tex;
        }

        void Update()
        {
            if (pending != null)
            {
                var data = pending; pending = null;            // keep only the newest frame
                if (tex.LoadImage(data, false))
                {
                    hasFrame = true;
                    float now = Time.realtimeSinceStartup;
                    lastFrameTime = now;
                    if (fpsWindowStart <= 0f) fpsWindowStart = now;
                    fpsCount++;
                    if (now - fpsWindowStart >= 1f)
                    {
                        Fps = fpsCount / (now - fpsWindowStart);
                        fpsCount = 0; fpsWindowStart = now;
                    }
                    if (fitter && tex.height > 0) fitter.aspectRatio = (float)tex.width / tex.height;
                }
            }
            bool live = IsLive;
            if (target) target.enabled = live;
            if (placeholder) placeholder.SetActive(!live);
            if (!live) Fps = 0f;
        }

        void OnDestroy() { if (tex) Destroy(tex); }
    }
}
