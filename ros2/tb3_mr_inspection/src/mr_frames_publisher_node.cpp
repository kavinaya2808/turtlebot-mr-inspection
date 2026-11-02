// Copyright 2026 Kavinaya
// SPDX-License-Identifier: Apache-2.0
//
// mr_frames_publisher: publishes the SLAM correction map -> odom as a PoseStamped (10 Hz).
//
// The headset follows the robot with /odom (20 Hz, smooth, but drifts). slam_toolbox corrects
// that drift by moving odom inside map. Sending only this one transform lets the headset place
// the robot drift-free (map pose = map_to_odom * odom pose) without streaming the whole /tf tree
// over Wi-Fi.

#include <chrono>
#include <memory>
#include <string>

#include "geometry_msgs/msg/pose_stamped.hpp"
#include "rclcpp/rclcpp.hpp"
#include "tf2/exceptions.h"
#include "tf2_ros/buffer.h"
#include "tf2_ros/transform_listener.h"

namespace tb3_mr_inspection
{

class MrFramesPublisher : public rclcpp::Node
{
public:
  explicit MrFramesPublisher(const rclcpp::NodeOptions & options = rclcpp::NodeOptions())
  : Node("mr_frames_publisher", options),
    tf_buffer_(get_clock()),
    tf_listener_(tf_buffer_)
  {
    map_frame_ = declare_parameter<std::string>("map_frame", "map");
    odom_frame_ = declare_parameter<std::string>("odom_frame", "odom");
    const double rate = declare_parameter<double>("rate", 10.0);

    pub_ = create_publisher<geometry_msgs::msg::PoseStamped>("/mr/map_to_odom", 10);
    timer_ = create_wall_timer(
      std::chrono::duration<double>(1.0 / rate), [this]() {tick();});
  }

private:
  void tick()
  {
    geometry_msgs::msg::TransformStamped t;
    try {
      t = tf_buffer_.lookupTransform(map_frame_, odom_frame_, tf2::TimePointZero);
    } catch (const tf2::TransformException &) {
      RCLCPP_INFO_THROTTLE(get_logger(), *get_clock(), 10000,
        "Waiting for %s -> %s (is slam_toolbox running?)", map_frame_.c_str(), odom_frame_.c_str());
      return;
    }
    geometry_msgs::msg::PoseStamped p;
    p.header = t.header;  // frame_id = map
    p.pose.position.x = t.transform.translation.x;
    p.pose.position.y = t.transform.translation.y;
    p.pose.position.z = t.transform.translation.z;
    p.pose.orientation = t.transform.rotation;
    pub_->publish(p);
  }

  std::string map_frame_, odom_frame_;
  tf2_ros::Buffer tf_buffer_;
  tf2_ros::TransformListener tf_listener_;
  rclcpp::Publisher<geometry_msgs::msg::PoseStamped>::SharedPtr pub_;
  rclcpp::TimerBase::SharedPtr timer_;
};

}  // namespace tb3_mr_inspection

int main(int argc, char ** argv)
{
  rclcpp::init(argc, argv);
  rclcpp::spin(std::make_shared<tb3_mr_inspection::MrFramesPublisher>());
  rclcpp::shutdown();
  return 0;
}
