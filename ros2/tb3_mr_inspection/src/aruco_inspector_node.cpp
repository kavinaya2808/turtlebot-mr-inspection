// Copyright 2026 Kavinaya
// SPDX-License-Identifier: Apache-2.0
//
// aruco_inspector: finds ArUco markers ("inspection points") in the RealSense colour stream,
// estimates each marker's 6-DoF pose from the camera intrinsics and the known marker size,
// moves it into the map frame with TF, and keeps a smoothed, persistent list of inspection
// points. Published as a MarkerArray for RViz (and later the Quest).
//
// Subscribes  <image_topic>        sensor_msgs/CompressedImage (JPEG from the robot)
//             <camera_info_topic>  sensor_msgs/CameraInfo
// Publishes   /inspection/markers      visualization_msgs/MarkerArray (2 Hz)
//             /inspection/points       geometry_msgs/PoseArray (confirmed points, 2 Hz)
//             /inspection/debug_image  sensor_msgs/Image (annotated, only when someone listens)

#include <array>
#include <map>
#include <memory>
#include <string>
#include <vector>

#include <opencv2/aruco.hpp>
#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "geometry_msgs/msg/pose_array.hpp"
#include "geometry_msgs/msg/pose_stamped.hpp"
#include "rclcpp/rclcpp.hpp"
#include "sensor_msgs/msg/camera_info.hpp"
#include "sensor_msgs/msg/compressed_image.hpp"
#include "sensor_msgs/msg/image.hpp"
#include "tf2/LinearMath/Matrix3x3.h"
#include "tf2/LinearMath/Quaternion.h"
#include "tf2_geometry_msgs/tf2_geometry_msgs.hpp"
#include "tf2_ros/buffer.h"
#include "tf2_ros/transform_listener.h"
#include "visualization_msgs/msg/marker_array.hpp"

// OpenCV 4.7 moved ArUco into objdetect behind the ArucoDetector class; Ubuntu 24.04 ships 4.6.
#if (CV_VERSION_MAJOR > 4) || (CV_VERSION_MAJOR == 4 && CV_VERSION_MINOR >= 7)
#define TB3_ARUCO_NEW_API 1
#else
#define TB3_ARUCO_NEW_API 0
#endif

namespace tb3_mr_inspection
{

namespace
{
int dictionary_id(const std::string & name)
{
  static const std::map<std::string, int> kDicts = {
    {"DICT_4X4_50", cv::aruco::DICT_4X4_50},
    {"DICT_4X4_100", cv::aruco::DICT_4X4_100},
    {"DICT_5X5_100", cv::aruco::DICT_5X5_100},
    {"DICT_6X6_250", cv::aruco::DICT_6X6_250},
    {"DICT_ARUCO_ORIGINAL", cv::aruco::DICT_ARUCO_ORIGINAL},
  };
  const auto it = kDicts.find(name);
  if (it == kDicts.end()) {
    throw std::invalid_argument("Unknown ArUco dictionary: " + name);
  }
  return it->second;
}
}  // namespace

class ArucoInspector : public rclcpp::Node
{
public:
  explicit ArucoInspector(const rclcpp::NodeOptions & options = rclcpp::NodeOptions())
  : Node("aruco_inspector", options),
    tf_buffer_(get_clock()),
    tf_listener_(tf_buffer_)
  {
    image_topic_ = declare_parameter<std::string>(
      "image_topic", "/camera/camera/color/image_raw/compressed");
    info_topic_ = declare_parameter<std::string>(
      "camera_info_topic", "/camera/camera/color/camera_info");
    marker_length_ = declare_parameter<double>("marker_length", 0.15);    // m, black square edge
    max_range_ = declare_parameter<double>("max_range", 2.5);             // m, ignore beyond
    min_hits_ = declare_parameter<int>("min_detections", 3);              // before confirming
    alpha_ = declare_parameter<double>("smoothing", 0.3);                 // 0..1, new-sample weight
    map_frame_ = declare_parameter<std::string>("map_frame", "map");
    fallback_frame_ = declare_parameter<std::string>("fallback_frame", "odom");
    const auto dict_name = declare_parameter<std::string>("dictionary", "DICT_4X4_50");

    // Sub-pixel corner refinement: 2-3x smaller pose error on small, distant markers
    // (synthetic 424x240 test: 72 -> 27 mm at 1.2 m for a 10 cm marker).
#if TB3_ARUCO_NEW_API
    cv::aruco::DetectorParameters params;
    params.cornerRefinementMethod = cv::aruco::CORNER_REFINE_SUBPIX;
    detector_ = std::make_unique<cv::aruco::ArucoDetector>(
      cv::aruco::getPredefinedDictionary(dictionary_id(dict_name)), params);
#else
    dictionary_ = cv::aruco::getPredefinedDictionary(dictionary_id(dict_name));
    detector_params_ = cv::aruco::DetectorParameters::create();
    detector_params_->cornerRefinementMethod = cv::aruco::CORNER_REFINE_SUBPIX;
#endif

    // Square marker corners in its own frame (z out of the marker), order required by
    // SOLVEPNP_IPPE_SQUARE: top-left, top-right, bottom-right, bottom-left.
    const float h = static_cast<float>(marker_length_ / 2.0);
    object_points_ = {
      cv::Point3f(-h, h, 0.f), cv::Point3f(h, h, 0.f),
      cv::Point3f(h, -h, 0.f), cv::Point3f(-h, -h, 0.f)};

    const auto sensor_qos = rclcpp::SensorDataQoS();
    info_sub_ = create_subscription<sensor_msgs::msg::CameraInfo>(
      info_topic_, sensor_qos,
      [this](sensor_msgs::msg::CameraInfo::ConstSharedPtr msg) {on_info(*msg);});
    image_sub_ = create_subscription<sensor_msgs::msg::CompressedImage>(
      image_topic_, sensor_qos,
      [this](sensor_msgs::msg::CompressedImage::ConstSharedPtr msg) {on_image(*msg);});

    markers_pub_ = create_publisher<visualization_msgs::msg::MarkerArray>(
      "/inspection/markers", 10);
    points_pub_ = create_publisher<geometry_msgs::msg::PoseArray>("/inspection/points", 10);
    debug_pub_ = create_publisher<sensor_msgs::msg::Image>(
      "/inspection/debug_image", rclcpp::SensorDataQoS());

    timer_ = create_wall_timer(std::chrono::milliseconds(500), [this]() {publish_points();});

    RCLCPP_INFO(get_logger(),
      "Looking for %s markers (%.2f m) in %s", dict_name.c_str(), marker_length_,
      image_topic_.c_str());
  }

private:
  struct InspectionPoint
  {
    std::string frame;
    geometry_msgs::msg::Pose pose;
    int hits{0};
    rclcpp::Time last_seen;
    bool announced{false};
  };

  void on_info(const sensor_msgs::msg::CameraInfo & info)
  {
    if (have_intrinsics_) {
      return;
    }
    camera_matrix_ = cv::Mat(3, 3, CV_64F);
    for (int i = 0; i < 9; ++i) {
      camera_matrix_.at<double>(i / 3, i % 3) = info.k[i];
    }
    dist_coeffs_ = cv::Mat(1, static_cast<int>(info.d.size()), CV_64F);
    for (size_t i = 0; i < info.d.size(); ++i) {
      dist_coeffs_.at<double>(0, static_cast<int>(i)) = info.d[i];
    }
    have_intrinsics_ = camera_matrix_.at<double>(0, 0) > 0.0;
    if (have_intrinsics_) {
      RCLCPP_INFO(get_logger(), "Camera intrinsics received (%ux%u, fx=%.1f)",
        info.width, info.height, info.k[0]);
    }
  }

  void on_image(const sensor_msgs::msg::CompressedImage & msg)
  {
    if (!have_intrinsics_) {
      RCLCPP_WARN_THROTTLE(get_logger(), *get_clock(), 5000,
        "Waiting for %s (record it in the bag too)", info_topic_.c_str());
      return;
    }

    const cv::Mat image = cv::imdecode(msg.data, cv::IMREAD_COLOR);
    if (image.empty()) {
      RCLCPP_WARN_THROTTLE(get_logger(), *get_clock(), 5000, "Could not decode image");
      return;
    }

    std::vector<int> ids;
    std::vector<std::vector<cv::Point2f>> corners;
#if TB3_ARUCO_NEW_API
    detector_->detectMarkers(image, corners, ids);
#else
    cv::aruco::detectMarkers(image, dictionary_, corners, ids, detector_params_);
#endif

    const bool want_debug = debug_pub_->get_subscription_count() > 0;
    cv::Mat debug;
    if (want_debug) {
      debug = image.clone();
      if (!ids.empty()) {
        cv::aruco::drawDetectedMarkers(debug, corners, ids);
      }
    }

    for (size_t i = 0; i < ids.size(); ++i) {
      cv::Vec3d rvec, tvec;
      if (!cv::solvePnP(object_points_, corners[i], camera_matrix_, dist_coeffs_,
        rvec, tvec, false, cv::SOLVEPNP_IPPE_SQUARE))
      {
        continue;
      }
      if (cv::norm(tvec) > max_range_) {
        continue;
      }
      if (want_debug) {
        cv::drawFrameAxes(debug, camera_matrix_, dist_coeffs_, rvec, tvec,
          static_cast<float>(marker_length_ * 0.5));
      }
      update_point(ids[i], to_pose_stamped(rvec, tvec, msg.header));
    }

    if (want_debug) {
      publish_debug(debug, msg.header);
    }
  }

  static geometry_msgs::msg::PoseStamped to_pose_stamped(
    const cv::Vec3d & rvec, const cv::Vec3d & tvec, const std_msgs::msg::Header & header)
  {
    cv::Mat r;
    cv::Rodrigues(rvec, r);
    const tf2::Matrix3x3 m(
      r.at<double>(0, 0), r.at<double>(0, 1), r.at<double>(0, 2),
      r.at<double>(1, 0), r.at<double>(1, 1), r.at<double>(1, 2),
      r.at<double>(2, 0), r.at<double>(2, 1), r.at<double>(2, 2));
    tf2::Quaternion q;
    m.getRotation(q);

    geometry_msgs::msg::PoseStamped p;
    p.header = header;  // camera optical frame, image time stamp
    p.pose.position.x = tvec[0];
    p.pose.position.y = tvec[1];
    p.pose.position.z = tvec[2];
    p.pose.orientation = tf2::toMsg(q);
    return p;
  }

  void update_point(int id, const geometry_msgs::msg::PoseStamped & in_camera)
  {
    geometry_msgs::msg::PoseStamped in_world;
    std::string frame;
    if (!transform_to(map_frame_, in_camera, in_world)) {
      if (!transform_to(fallback_frame_, in_camera, in_world)) {
        RCLCPP_WARN_THROTTLE(get_logger(), *get_clock(), 5000,
          "No TF from %s to %s or %s yet", in_camera.header.frame_id.c_str(),
          map_frame_.c_str(), fallback_frame_.c_str());
        return;
      }
    }
    frame = in_world.header.frame_id;

    auto & pt = points_[id];
    if (pt.hits == 0 || pt.frame != frame) {
      pt.pose = in_world.pose;
      pt.frame = frame;
      pt.hits = 0;
    } else {
      // Exponential smoothing of the position; the orientation follows the latest sample.
      pt.pose.position.x += alpha_ * (in_world.pose.position.x - pt.pose.position.x);
      pt.pose.position.y += alpha_ * (in_world.pose.position.y - pt.pose.position.y);
      pt.pose.position.z += alpha_ * (in_world.pose.position.z - pt.pose.position.z);
      pt.pose.orientation = in_world.pose.orientation;
    }
    pt.hits++;
    pt.last_seen = now();

    if (!pt.announced && pt.hits >= min_hits_) {
      pt.announced = true;
      RCLCPP_INFO(get_logger(), "Inspection point #%d confirmed at (%.2f, %.2f, %.2f) in %s",
        id, pt.pose.position.x, pt.pose.position.y, pt.pose.position.z, frame.c_str());
    }
  }

  bool transform_to(
    const std::string & target, const geometry_msgs::msg::PoseStamped & in,
    geometry_msgs::msg::PoseStamped & out)
  {
    try {
      // Short wait: the TF for the image time stamp may arrive a moment after the image.
      out = tf_buffer_.transform(in, target, tf2::durationFromSec(0.05));
      return true;
    } catch (const tf2::TransformException &) {
      return false;
    }
  }

  void publish_points()
  {
    visualization_msgs::msg::MarkerArray array;
    geometry_msgs::msg::PoseArray poses;
    poses.header.stamp = now();
    poses.header.frame_id = map_frame_;

    for (const auto & [id, pt] : points_) {
      if (pt.hits < min_hits_) {
        continue;
      }
      visualization_msgs::msg::Marker plate;
      plate.header.frame_id = pt.frame;
      plate.header.stamp = rclcpp::Time(0, 0, get_clock()->get_clock_type());
      plate.ns = "inspection_points";
      plate.id = id * 2;
      plate.type = visualization_msgs::msg::Marker::CUBE;
      plate.action = visualization_msgs::msg::Marker::ADD;
      plate.pose = pt.pose;
      plate.scale.x = marker_length_;
      plate.scale.y = marker_length_;
      plate.scale.z = 0.01;
      plate.color.r = 0.25f;
      plate.color.g = 0.84f;
      plate.color.b = 1.0f;
      plate.color.a = 0.9f;
      array.markers.push_back(plate);

      visualization_msgs::msg::Marker label = plate;
      label.id = id * 2 + 1;
      label.type = visualization_msgs::msg::Marker::TEXT_VIEW_FACING;
      label.pose.position.z += 0.15;
      label.pose.orientation = geometry_msgs::msg::Quaternion();
      label.scale.x = label.scale.y = 0.0;
      label.scale.z = 0.08;
      label.color.r = label.color.g = label.color.b = 1.0f;
      label.text = "Point " + std::to_string(id) + " (" + std::to_string(pt.hits) + ")";
      array.markers.push_back(label);

      if (pt.frame == map_frame_) {
        poses.poses.push_back(pt.pose);
      }
    }
    markers_pub_->publish(array);
    points_pub_->publish(poses);
  }

  void publish_debug(const cv::Mat & bgr, const std_msgs::msg::Header & header)
  {
    sensor_msgs::msg::Image img;
    img.header = header;
    img.height = static_cast<uint32_t>(bgr.rows);
    img.width = static_cast<uint32_t>(bgr.cols);
    img.encoding = "bgr8";
    img.is_bigendian = 0;
    img.step = static_cast<uint32_t>(bgr.cols * bgr.elemSize());
    img.data.assign(bgr.datastart, bgr.dataend);
    debug_pub_->publish(img);
  }

  // Parameters
  std::string image_topic_, info_topic_, map_frame_, fallback_frame_;
  double marker_length_{0.15}, max_range_{2.5}, alpha_{0.3};
  int min_hits_{3};

  // Detection
#if TB3_ARUCO_NEW_API
  std::unique_ptr<cv::aruco::ArucoDetector> detector_;
#else
  cv::Ptr<cv::aruco::Dictionary> dictionary_;
  cv::Ptr<cv::aruco::DetectorParameters> detector_params_;
#endif
  std::vector<cv::Point3f> object_points_;
  cv::Mat camera_matrix_, dist_coeffs_;
  bool have_intrinsics_{false};

  // State
  std::map<int, InspectionPoint> points_;

  // ROS
  tf2_ros::Buffer tf_buffer_;
  tf2_ros::TransformListener tf_listener_;
  rclcpp::Subscription<sensor_msgs::msg::CameraInfo>::SharedPtr info_sub_;
  rclcpp::Subscription<sensor_msgs::msg::CompressedImage>::SharedPtr image_sub_;
  rclcpp::Publisher<visualization_msgs::msg::MarkerArray>::SharedPtr markers_pub_;
  rclcpp::Publisher<geometry_msgs::msg::PoseArray>::SharedPtr points_pub_;
  rclcpp::Publisher<sensor_msgs::msg::Image>::SharedPtr debug_pub_;
  rclcpp::TimerBase::SharedPtr timer_;
};

}  // namespace tb3_mr_inspection

int main(int argc, char ** argv)
{
  rclcpp::init(argc, argv);
  rclcpp::spin(std::make_shared<tb3_mr_inspection::ArucoInspector>());
  rclcpp::shutdown();
  return 0;
}
