using UnityEngine;
using Unity.Robotics.ROSTCPConnector;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Desk testing in the Unity Editor (no headset, no laser clicks):
    ///  - connects to ROS automatically, because the in-headset IP panel can't be clicked in the Editor
    ///  - SPACE places the robot 1.2 m in front of the camera so the tag and ring become visible.
    /// Does nothing in a Quest build.
    /// </summary>
    public class EditorQuickTest : MonoBehaviour
    {
        public RobotWorldAnchor anchor;
        [Tooltip("The Ubuntu VM running ros_tcp_endpoint")]
        public string rosIP = "127.0.0.1";
        public int rosPort = 10000;
        public bool autoConnectInEditor = true;
        public KeyCode alignKey = KeyCode.Space;

#if UNITY_EDITOR
        void Start()
        {
            if (autoConnectInEditor) Invoke(nameof(ConnectNow), 1.0f);   // after ROSConnectionUI's Start
        }

        void ConnectNow()
        {
            Debug.Log($"[EditorQuickTest] Connecting to ROS at {rosIP}:{rosPort}");
            ROSConnection.GetOrCreateInstance().Connect(rosIP, rosPort);
        }

        void Update()
        {
            if (anchor == null || !Input.GetKeyDown(alignKey)) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            Vector3 pos = cam.transform.position + f * 1.2f;
            pos.y = anchor.FloorY;
            anchor.SetAlignment(pos, Quaternion.LookRotation(f).eulerAngles.y);
            anchor.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f));
            Debug.Log("[EditorQuickTest] Robot placed 1.2 m in front of the camera");
        }
#endif
    }
}
