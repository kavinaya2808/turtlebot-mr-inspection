using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace RealTurtleBotMR
{
    /// <summary>
    /// Keeps the scanned room (MRUK EffectMesh: the glowing blue walls/furniture) invisible so you
    /// only see the real room through passthrough. Colliders stay active.
    /// Y (left controller) toggles it on/off for debugging.
    /// Replaces the host project's MRSceneConfig + SceneMeshOcclusion in this scene.
    /// </summary>
    public class RoomMeshVisibility : MonoBehaviour
    {
        public bool showRoomMesh = false;
        public OVRInput.Button toggleButton = OVRInput.Button.Two;           // Y on the left controller
        public OVRInput.Controller toggleController = OVRInput.Controller.LTouch;

        EffectMesh[] meshes = new EffectMesh[0];
        float nextScan;

        void Update()
        {
            if (OVRInput.GetDown(toggleButton, toggleController))
            {
                showRoomMesh = !showRoomMesh;
                Apply();
            }

            // The room loads a few seconds after start (and again if it changes): keep enforcing.
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 1f;
                meshes = FindObjectsOfType<EffectMesh>(true);
                Apply();
            }
        }

        void Apply()
        {
            foreach (var m in meshes)
            {
                if (m == null) continue;
                if (m.HideMesh == showRoomMesh) m.HideMesh = !showRoomMesh;
            }
        }
    }
}
