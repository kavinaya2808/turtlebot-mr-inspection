#!/usr/bin/env python3
"""Make the MR-SLAM host project build for Meta Quest (Android) with this project's scene.

    python3 tools/patch_unity_project_for_quest.py ~/MR-SLAM

Open the project in Unity once before running this (so Library/PackageCache exists), and close
Unity while it runs. Safe to run again: every step checks before it changes anything.

What it does
1. Embeds local copies of the Unity Robotics packages (ROS-TCP-Connector, Visualizations,
   URDF-Importer) under Packages/, because their runtime assemblies exclude Android and the
   Quest build fails with hundreds of "type or namespace not found" errors.
2. Adds Android to those runtime assemblies.
3. Switches the URDF-Importer's desktop-only native plugins (assimp, vhacd) off for Android,
   otherwise the build fails with "Cannot include plugin ... assimp.dll".
4. Turns on Passthrough (required), Scene (required) and Anchors in the Meta XR project config.
"""

import glob
import json
import os
import re
import shutil
import stat
import sys

PACKAGES = ["ros-tcp-connector", "visualizations", "urdf-importer"]
NATIVE = re.compile(r"\.(dll|so|dylib|bundle)\.meta$", re.I)


def log(msg):
    print("  " + msg)


def embed_packages(project):
    print("1. Embed Unity Robotics packages")
    for name in PACKAGES:
        dst = os.path.join(project, "Packages", f"com.unity.robotics.{name}")
        if os.path.isdir(dst):
            log(f"{name}: already embedded")
            continue
        src = sorted(glob.glob(os.path.join(project, "Library", "PackageCache",
                                            f"com.unity.robotics.{name}@*")))
        if not src:
            sys.exit(f"  {name}: not in Library/PackageCache. Open the project in Unity once first.")
        shutil.copytree(src[-1], dst)
        for root, dirs, files in os.walk(dst):
            for p in dirs + files:
                path = os.path.join(root, p)
                os.chmod(path, os.stat(path).st_mode | stat.S_IWUSR)
        log(f"{name}: copied from {os.path.basename(src[-1])}")


def enable_android(project):
    print("2. Add Android to runtime assemblies")
    for name in PACKAGES:
        for asmdef in glob.glob(os.path.join(project, "Packages", f"com.unity.robotics.{name}",
                                             "Runtime", "**", "*.asmdef"), recursive=True):
            with open(asmdef, encoding="utf-8-sig") as f:
                data = json.load(f)
            plats = data.get("includePlatforms", [])
            if not plats or plats == ["Editor"] or "Android" in plats:
                continue
            plats.append("Android")
            with open(asmdef, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=4)
                f.write("\n")
            log(f"{os.path.basename(asmdef)}: + Android")


def disable_native_plugins_for_android(project):
    print("3. Desktop native plugins off for Android")
    root_dir = os.path.join(project, "Packages", "com.unity.robotics.urdf-importer")
    for root, _, files in os.walk(root_dir):
        for fn in files:
            if not NATIVE.search(fn) or fn == "AssimpNet.dll.meta":  # managed .NET dll is fine
                continue
            path = os.path.join(root, fn)
            with open(path, encoding="utf-8") as f:
                s = f.read()
            if "PluginImporter" not in s:
                continue
            before = s
            # "Any Platform" off, so the explicit per-platform entries decide.
            s = re.sub(r"(  - first:\n      Any: ?\n    second:\n      enabled: )1", r"\g<1>0", s)
            s = re.sub(r"(        Exclude Android: )0", r"\g<1>1", s)
            m = re.search(r"(  - first:\n      Android: Android\n    second:\n      enabled: )(\d)", s)
            if m:
                s = s[:m.start(2)] + "0" + s[m.end(2):]
            else:
                s = s.replace("  platformData:\n",
                              "  platformData:\n  - first:\n      Android: Android\n    second:\n"
                              "      enabled: 0\n      settings:\n        CPU: ARMv7\n", 1)
            if s != before:
                with open(path, "w", encoding="utf-8") as f:
                    f.write(s)
                log(f"{fn}: Android off")


def enable_quest_features(project):
    print("4. Meta XR features: passthrough, scene, anchors")
    path = os.path.join(project, "Assets", "Oculus", "OculusProjectConfig.asset")
    if not os.path.isfile(path):
        log("OculusProjectConfig.asset not found, set them in OVRManager > Quest Features")
        return
    with open(path, encoding="utf-8") as f:
        s = f.read()
    before = s
    s = re.sub(r"^(  _insightPassthroughSupport:) \d$", r"\1 2", s, flags=re.M)  # Required
    s = re.sub(r"^(  sceneSupport:) \d$", r"\1 2", s, flags=re.M)                # Required
    s = re.sub(r"^(  anchorSupport:) \d$", r"\1 1", s, flags=re.M)               # Enabled
    if s != before:
        with open(path, "w", encoding="utf-8") as f:
            f.write(s)
        log("updated")
    else:
        log("already set")


def warn_duplicates(project):
    dups = glob.glob(os.path.join(project, "Assets", "* [0-9]"))
    for d in dups:
        if os.path.isdir(d):
            print(f"!! Duplicate folder {os.path.basename(d)!r} in Assets: an accidental copy "
                  "breaks the Android build (\"plugin with the same name\"). Move it out of Assets.")


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    project = os.path.abspath(os.path.expanduser(sys.argv[1]))
    if not os.path.isdir(os.path.join(project, "Assets")):
        sys.exit(f"Not a Unity project: {project}")
    print(f"Patching {project}")
    embed_packages(project)
    enable_android(project)
    disable_native_plugins_for_android(project)
    enable_quest_features(project)
    warn_duplicates(project)
    print("Done. Open Unity, wait for it to import, then build.")


if __name__ == "__main__":
    main()
