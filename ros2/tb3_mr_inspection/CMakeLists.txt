cmake_minimum_required(VERSION 3.8)
project(tb3_mr_inspection)

if(NOT CMAKE_CXX_STANDARD)
  set(CMAKE_CXX_STANDARD 17)
  set(CMAKE_CXX_STANDARD_REQUIRED ON)
endif()

if(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  add_compile_options(-Wall -Wextra -Wpedantic)
endif()

find_package(ament_cmake REQUIRED)
find_package(rclcpp REQUIRED)
find_package(sensor_msgs REQUIRED)
find_package(std_msgs REQUIRED)
find_package(geometry_msgs REQUIRED)
find_package(visualization_msgs REQUIRED)
find_package(tf2 REQUIRED)
find_package(tf2_ros REQUIRED)
find_package(tf2_geometry_msgs REQUIRED)
# OpenCV with ArUco: a contrib module up to 4.6 (Ubuntu 24.04 / Jazzy), part of objdetect from 4.7.
find_package(OpenCV REQUIRED)
set(TB3_OPENCV_LIBS opencv_core opencv_imgproc opencv_imgcodecs opencv_calib3d)
foreach(mod opencv_aruco opencv_objdetect)
  if(TARGET ${mod})
    list(APPEND TB3_OPENCV_LIBS ${mod})
  endif()
endforeach()

# --- scan_resampler: /scan -> /scan_fixed (fixed beam count for slam_toolbox)
add_executable(scan_resampler src/scan_resampler_node.cpp)
target_include_directories(scan_resampler PUBLIC
  $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)
ament_target_dependencies(scan_resampler rclcpp sensor_msgs)

# --- aruco_inspector: RealSense image -> inspection points in the map frame
add_executable(aruco_inspector src/aruco_inspector_node.cpp)
target_include_directories(aruco_inspector PUBLIC ${OpenCV_INCLUDE_DIRS})
target_link_libraries(aruco_inspector ${TB3_OPENCV_LIBS})
ament_target_dependencies(aruco_inspector
  rclcpp sensor_msgs std_msgs geometry_msgs visualization_msgs tf2 tf2_ros tf2_geometry_msgs)

install(TARGETS scan_resampler aruco_inspector DESTINATION lib/${PROJECT_NAME})
install(DIRECTORY launch config rviz DESTINATION share/${PROJECT_NAME})

if(BUILD_TESTING)
  find_package(ament_cmake_gtest REQUIRED)
  ament_add_gtest(test_scan_resample test/test_scan_resample.cpp)
  target_include_directories(test_scan_resample PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)
endif()

ament_package()
