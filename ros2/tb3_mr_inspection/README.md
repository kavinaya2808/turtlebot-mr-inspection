# tb3_mr_inspection

SLAM and visual inspection points for a real TurtleBot3, the robotics back end of a mixed-reality inspection supervisor on Meta Quest 3.

The robot maps the room with its lidar, finds ArUco markers ("inspection points") with its RealSense camera, and places each one on the map in 3D. The Quest app (Unity, separate repo) shows the robot, its telemetry and the inspection points in the real room.

## What's inside

| Node | Language | Does |
| --- | --- | --- |
| `scan_resampler` | C++ | Resamples every LDS-02 scan (218 to 244 beams) onto a fixed 360 × 1° grid, so slam_toolbox accepts it |
| `aruco_inspector` | C++, OpenCV | Detects ArUco markers in the compressed colour stream, estimates their pose, moves them into the `map` frame and keeps a smoothed, confirmed list |
| slam_toolbox | — | Online 2D mapping and localisation on `/scan_fixed` |
| RViz | — | Map, lidar, odometry, TF, inspection points, annotated camera image |

**Pose estimation.** Marker corners are refined to sub-pixel accuracy, then the pose comes from a square-target PnP (`SOLVEPNP_IPPE_SQUARE`) using the camera intrinsics from `camera_info` and the printed marker size. TF moves it from `camera_color_optical_frame` to `map` at the image time stamp. A point is confirmed after 3 sightings and its position is smoothed exponentially.

## Hardware and software

TurtleBot3 Burger (OpenCR, LDS-02 lidar, Raspberry Pi 4), Intel RealSense D435i, ROS 2 Jazzy on Ubuntu 24.04, slam_toolbox, OpenCV 4.6.

## Topics

| Topic | Type | Direction |
| --- | --- | --- |
| `/scan` | sensor_msgs/LaserScan | in |
| `/camera/camera/color/image_raw/compressed` | sensor_msgs/CompressedImage | in |
| `/camera/camera/color/camera_info` | sensor_msgs/CameraInfo | in |
| `/scan_fixed` | sensor_msgs/LaserScan | out |
| `/inspection/markers` | visualization_msgs/MarkerArray | out, 2 Hz |
| `/inspection/points` | geometry_msgs/PoseArray | out, 2 Hz, map frame |
| `/inspection/debug_image` | sensor_msgs/Image | out, only when viewed |

## Results

- Unit tests: 7/7 for the scan resampler (beam count, wrap-around, offset start angle, coverage gaps, intensities, invalid input).
- Synthetic 424 × 240 test frames, 10 cm marker, position error of the estimated pose:

| Distance | Without refinement | Sub-pixel refinement |
| --- | --- | --- |
| 0.6 m | 10 mm | 7 mm |
| 1.2 m | 71 mm | 27 mm |
| 2.0 m | 179 mm | 91 mm |

- End-to-end ROS test (fake robot, 15 cm marker at 1.0 m): point confirmed in `map` at 3.5 mm from ground truth.

## Structure

```
tb3_mr_inspection/
├── include/tb3_mr_inspection/scan_resample.hpp   resampling logic (header-only, unit-tested)
├── src/scan_resampler_node.cpp
├── src/aruco_inspector_node.cpp
├── test/test_scan_resample.cpp                   GoogleTest
├── launch/inspection.launch.py                   resampler + slam_toolbox + inspector + RViz
├── config/slam_toolbox.yaml
├── config/aruco_inspector.yaml
├── rviz/inspection.rviz
└── markers/aruco_4x4_50_ids0-3_15cm.pdf          print at 100 %
```

## Reproduce

**Build** (ROS 2 Jazzy):

```bash
sudo apt install ros-jazzy-slam-toolbox libopencv-dev
cd ~/ws/src && git clone <this repo>
cd ~/ws && colcon build --packages-select tb3_mr_inspection && source install/setup.bash
colcon test --packages-select tb3_mr_inspection && colcon test-result --verbose
```

**Record a session on the robot** (robot running `turtlebot3_bringup` and the RealSense colour stream):

```bash
ros2 bag record -o ~/bags/session_01 /scan /odom /tf /tf_static /imu /joint_states /battery_state \
  /camera/camera/color/image_raw/compressed /camera/camera/color/camera_info
```

Tape printed markers around the room, drive slowly past them and finish where you started.

**Replay:**

```bash
ros2 launch tb3_mr_inspection inspection.launch.py          # terminal 1
ros2 bag play ~/bags/session_01 --clock                       # terminal 2
```

**Live** (on a machine on the robot's network): `ros2 launch tb3_mr_inspection inspection.launch.py use_sim_time:=false`

Set `marker_length` in `config/aruco_inspector.yaml` to the measured edge of the printed black square, and the camera mount with `camera_x`, `camera_z` (launch arguments, metres from `base_footprint`).
