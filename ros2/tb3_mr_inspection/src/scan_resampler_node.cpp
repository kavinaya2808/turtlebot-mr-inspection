// Copyright 2026 Kavinaya
// SPDX-License-Identifier: Apache-2.0
//
// scan_resampler: /scan (LDS-02, varying beam count) -> /scan_fixed (always `beams` beams).
// Keeps the beam count constant so slam_toolbox can build a map from the LDS-02.

#include <memory>
#include <stdexcept>
#include <utility>

#include "rclcpp/rclcpp.hpp"
#include "sensor_msgs/msg/laser_scan.hpp"
#include "tb3_mr_inspection/scan_resample.hpp"

namespace tb3_mr_inspection
{

class ScanResampler : public rclcpp::Node
{
public:
  explicit ScanResampler(const rclcpp::NodeOptions & options = rclcpp::NodeOptions())
  : Node("scan_resampler", options)
  {
    beams_ = static_cast<int>(declare_parameter<int>("beams", 360));
    if (beams_ <= 0) {
      throw std::invalid_argument("beams must be > 0");
    }

    // The LDS-02 publishes best effort; SensorDataQoS also accepts reliable publishers
    // (for example a rosbag replay).
    const auto qos = rclcpp::SensorDataQoS();
    pub_ = create_publisher<sensor_msgs::msg::LaserScan>("scan_fixed", qos);
    sub_ = create_subscription<sensor_msgs::msg::LaserScan>(
      "scan", qos,
      [this](sensor_msgs::msg::LaserScan::ConstSharedPtr msg) {on_scan(*msg);});

    RCLCPP_INFO(get_logger(), "Resampling %s -> %s onto %d beams",
      sub_->get_topic_name(), pub_->get_topic_name(), beams_);
  }

private:
  void on_scan(const sensor_msgs::msg::LaserScan & in)
  {
    ResampledScan r = resample_scan(
      in.ranges, in.intensities, in.angle_min, in.angle_increment, beams_);
    if (r.ranges.empty()) {
      RCLCPP_WARN_THROTTLE(get_logger(), *get_clock(), 5000, "Dropping empty or invalid scan");
      return;
    }

    sensor_msgs::msg::LaserScan out;
    out.header = in.header;
    out.range_min = in.range_min;
    out.range_max = in.range_max;
    out.scan_time = in.scan_time;
    out.angle_min = static_cast<float>(r.angle_min);
    out.angle_max = static_cast<float>(r.angle_max);
    out.angle_increment = static_cast<float>(r.angle_increment);
    out.time_increment = in.scan_time > 0.0f ? in.scan_time / static_cast<float>(beams_) : 0.0f;
    out.ranges = std::move(r.ranges);
    out.intensities = std::move(r.intensities);
    pub_->publish(out);
  }

  int beams_{360};
  rclcpp::Publisher<sensor_msgs::msg::LaserScan>::SharedPtr pub_;
  rclcpp::Subscription<sensor_msgs::msg::LaserScan>::SharedPtr sub_;
};

}  // namespace tb3_mr_inspection

int main(int argc, char ** argv)
{
  rclcpp::init(argc, argv);
  rclcpp::spin(std::make_shared<tb3_mr_inspection::ScanResampler>());
  rclcpp::shutdown();
  return 0;
}
