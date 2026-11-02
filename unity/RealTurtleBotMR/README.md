# Real TurtleBot × Mixed Reality

Live telemetry from a **real TurtleBot** shown in mixed reality on **Meta Quest 3**
(passthrough), streamed over ROS 2 via ROS-TCP-Connector.

This folder lives inside a host Unity project (MR-SLAM, Unity 2022.3.62f2) that provides the
already-configured Meta XR SDK 201 passthrough setup and the ROS-TCP bridge. Everything that
belongs to *this* project lives here:

| Folder | What goes in it |
|---|---|
| `Scenes/` | `RealTurtleBotMR.unity` – the build scene |
| `Scripts/ROS/` | robot telemetry (odom, battery), RealSense camera feed, Editor test helper |
| `Scripts/Calibration/` | aligning the robot's `odom` frame with the Quest's world |
| `Scripts/Dashboard/` | head tag, floor ring, telemetry panel, shared style |
| `Scripts/MR/` | keeps the scanned room mesh hidden |
| `Editor/` | the RealTurtleBotMR → Set Up Scene tool |
| `UI/` | rounded-rectangle sprite for the panels |

## Scene setup

Run **RealTurtleBotMR → Set Up Scene** once (safe to re-run). It strips the simulated robots and
the old multi-robot dashboard from this scene copy and builds:

- **RealTurtleBot** – `TurtleBotTelemetry` (ROS subscriber), `RobotWorldAnchor` (room alignment),
  `RobotFootprint` (floor ring) and a child **HeadTag** (ID · status · battery, floats above the robot).
- **TelemetryPanel** – status + link header, live RealSense camera, battery / motion / pose cards with compass.
- **RoomMeshVisibility** – keeps the scanned room (blue MRUK mesh) hidden; replaces the host's MRSceneConfig/SceneMeshOcclusion in this scene.

Kept from the host project: OVRCameraRig + passthrough, MRUK scene mesh, laser pointers,
`ROSConnection` and the in-headset IP panel (`ROSConnectionUI`).

## Controls (Quest controllers)

| Input | Action |
|---|---|
| **A** | Align: rest the right controller on top of the robot, pointing where it faces, press A |
| **Right grip + right stick** | Slide the alignment (relative to where you look) |
| **Left grip + left stick ←→** | Rotate the alignment |
| **B** | Bring the telemetry panel in front of you |
| **Y** | Show / hide the scanned room mesh (debug) |
| **Trigger** | Click UI (e.g. Connect on the ROS IP panel) |

## Topics (defaults, set on `RealTurtleBot`)

`/odom` (nav_msgs/Odometry) and `/battery_state` (sensor_msgs/BatteryState), optional namespace.
Unity only subscribes – it never publishes TF or /clock, the real robot owns those.

## Camera (RealSense D435i on the robot)

Start the colour stream on the robot (second SSH terminal); Unity shows `/camera/camera/color/image_raw/compressed`:

```bash
ros2 launch realsense2_camera rs_launch.py enable_depth:=false pointcloud.enable:=false rgb_camera.color_profile:=424x240x15
```
