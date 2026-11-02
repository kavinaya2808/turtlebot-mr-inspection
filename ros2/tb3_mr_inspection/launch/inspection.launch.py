# Copyright 2026 Kavinaya
# SPDX-License-Identifier: Apache-2.0
"""SLAM + ArUco inspection pipeline for the TurtleBot3 (Burger, LDS-02, RealSense D435i).

Replay a recorded session (default):
    ros2 launch tb3_mr_inspection inspection.launch.py
    ros2 bag play <bag> --clock            # second terminal

Live, on a machine on the robot's network:
    ros2 launch tb3_mr_inspection inspection.launch.py use_sim_time:=false
"""

import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    pkg = get_package_share_directory('tb3_mr_inspection')
    use_sim_time = LaunchConfiguration('use_sim_time')

    args = [
        DeclareLaunchArgument('use_sim_time', default_value='true',
                              description='true for rosbag replay (play with --clock)'),
        DeclareLaunchArgument('rviz', default_value='true'),
        DeclareLaunchArgument('slam', default_value='true'),
        DeclareLaunchArgument('inspector', default_value='true'),
        # Camera mount on the robot (base_footprint -> camera_link). Measure on the robot.
        DeclareLaunchArgument('camera_x', default_value='0.03'),
        DeclareLaunchArgument('camera_y', default_value='0.0'),
        DeclareLaunchArgument('camera_z', default_value='0.15'),
        DeclareLaunchArgument('camera_yaw', default_value='0.0'),
    ]

    camera_tf = Node(
        package='tf2_ros', executable='static_transform_publisher', name='camera_mount_tf',
        arguments=['--x', LaunchConfiguration('camera_x'),
                   '--y', LaunchConfiguration('camera_y'),
                   '--z', LaunchConfiguration('camera_z'),
                   '--yaw', LaunchConfiguration('camera_yaw'),
                   '--frame-id', 'base_footprint', '--child-frame-id', 'camera_link'],
        parameters=[{'use_sim_time': use_sim_time}],
    )

    scan_resampler = Node(
        package='tb3_mr_inspection', executable='scan_resampler', name='scan_resampler',
        parameters=[{'beams': 360, 'use_sim_time': use_sim_time}],
        output='screen',
    )

    slam = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(os.path.join(
            get_package_share_directory('slam_toolbox'), 'launch', 'online_async_launch.py')),
        launch_arguments={
            'use_sim_time': use_sim_time,
            'slam_params_file': os.path.join(pkg, 'config', 'slam_toolbox.yaml'),
        }.items(),
        condition=IfCondition(LaunchConfiguration('slam')),
    )

    inspector = Node(
        package='tb3_mr_inspection', executable='aruco_inspector', name='aruco_inspector',
        parameters=[os.path.join(pkg, 'config', 'aruco_inspector.yaml'),
                    {'use_sim_time': use_sim_time}],
        output='screen',
        condition=IfCondition(LaunchConfiguration('inspector')),
    )

    rviz = Node(
        package='rviz2', executable='rviz2', name='rviz2',
        arguments=['-d', os.path.join(pkg, 'rviz', 'inspection.rviz')],
        parameters=[{'use_sim_time': use_sim_time}],
        condition=IfCondition(LaunchConfiguration('rviz')),
    )

    return LaunchDescription(args + [camera_tf, scan_resampler, slam, inspector, rviz])
