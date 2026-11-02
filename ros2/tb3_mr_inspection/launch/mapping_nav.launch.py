# Copyright 2026 Kavinaya
# SPDX-License-Identifier: Apache-2.0
"""Live mapping + navigation for the TurtleBot3 Burger (no premade map).

Runs on a ROS 2 Jazzy machine on the robot's network, while the robot runs bringup:
    ros2 launch tb3_mr_inspection mapping_nav.launch.py

  scan_resampler      /scan -> /scan_fixed (fixed beam count)
  slam_toolbox        builds /map, publishes map -> odom
  Nav2 (after 10 s)   planner, controller, behaviours, collision monitor; goals on /goal_pose
  mr_frames_publisher /mr/map_to_odom for the headset
  aruco_inspector     optional: inspector:=true (needs the RealSense stream)
  RViz                Nav2 view; use "Nav2 Goal" to send the robot somewhere
"""

import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription, TimerAction
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    pkg = get_package_share_directory('tb3_mr_inspection')
    nav2 = get_package_share_directory('nav2_bringup')
    use_sim_time = LaunchConfiguration('use_sim_time')

    args = [
        DeclareLaunchArgument('use_sim_time', default_value='false'),
        DeclareLaunchArgument('rviz', default_value='true'),
        DeclareLaunchArgument('nav', default_value='true'),
        DeclareLaunchArgument('inspector', default_value='false'),
        DeclareLaunchArgument('nav_delay', default_value='10.0',
                              description='seconds to wait for slam_toolbox before Nav2'),
        DeclareLaunchArgument('camera_x', default_value='0.03'),
        DeclareLaunchArgument('camera_z', default_value='0.15'),
    ]

    scan_resampler = Node(
        package='tb3_mr_inspection', executable='scan_resampler', name='scan_resampler',
        parameters=[{'beams': 360, 'use_sim_time': use_sim_time}], output='screen')

    slam = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(os.path.join(
            get_package_share_directory('slam_toolbox'), 'launch', 'online_async_launch.py')),
        launch_arguments={
            'use_sim_time': use_sim_time,
            'slam_params_file': os.path.join(pkg, 'config', 'slam_toolbox.yaml'),
        }.items())

    navigation = TimerAction(
        period=LaunchConfiguration('nav_delay'),
        actions=[IncludeLaunchDescription(
            PythonLaunchDescriptionSource(os.path.join(nav2, 'launch', 'navigation_launch.py')),
            launch_arguments={
                'use_sim_time': use_sim_time,
                'autostart': 'true',
                'params_file': os.path.join(pkg, 'config', 'nav2_burger.yaml'),
            }.items())],
        condition=IfCondition(LaunchConfiguration('nav')))

    frames = Node(
        package='tb3_mr_inspection', executable='mr_frames_publisher',
        name='mr_frames_publisher', parameters=[{'use_sim_time': use_sim_time}], output='screen')

    camera_tf = Node(
        package='tf2_ros', executable='static_transform_publisher', name='camera_mount_tf',
        arguments=['--x', LaunchConfiguration('camera_x'), '--z', LaunchConfiguration('camera_z'),
                   '--frame-id', 'base_footprint', '--child-frame-id', 'camera_link'],
        parameters=[{'use_sim_time': use_sim_time}],
        condition=IfCondition(LaunchConfiguration('inspector')))

    inspector = Node(
        package='tb3_mr_inspection', executable='aruco_inspector', name='aruco_inspector',
        parameters=[os.path.join(pkg, 'config', 'aruco_inspector.yaml'),
                    {'use_sim_time': use_sim_time}],
        output='screen', condition=IfCondition(LaunchConfiguration('inspector')))

    rviz = Node(
        package='rviz2', executable='rviz2', name='rviz2',
        arguments=['-d', os.path.join(nav2, 'rviz', 'nav2_default_view.rviz')],
        parameters=[{'use_sim_time': use_sim_time}],
        condition=IfCondition(LaunchConfiguration('rviz')))

    return LaunchDescription(
        args + [scan_resampler, slam, navigation, frames, camera_tf, inspector, rviz])
