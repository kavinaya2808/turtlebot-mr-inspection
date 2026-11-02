using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RealTurtleBotMR.EditorTools
{
    /// <summary>
    /// Menu: RealTurtleBotMR > Set Up Scene
    /// 1. Removes the 3 simulated robots and the old multi-robot dashboard from our scene copy.
    /// 2. Builds one "RealTurtleBot" (telemetry + room alignment + floor ring + head tag)
    ///    and a single-robot TelemetryPanel.
    /// 3. Makes our scene the (only enabled) build scene.
    /// Safe to run again: it rebuilds its own objects from scratch.
    /// </summary>
    public static class RealTurtleBotSceneSetup
    {
        const string ScenePath = "Assets/RealTurtleBotMR/Scenes/RealTurtleBotMR.unity";
        const string SpritePath = "Assets/RealTurtleBotMR/UI/RoundedRect.png";

        static readonly string[] RemoveRoots =
        {
            // simulated robots
            "TurtleBot3", "TurtleBot3_R2", "TurtleBot3_R3", "RobotTelemetry_R2", "RobotTelemetry_R3",
            // old multi-robot dashboard + teleop
            "MultiRobotOverview", "BatteryPanel", "RobotStatusPanel", "PositionPanel", "DashboardSystem", "TeleopManager",
            // our own objects (rebuilt below)
            "RealTurtleBot", "TelemetryPanel",
        };

        static Sprite rounded;
        static TMP_FontAsset font;

        [MenuItem("RealTurtleBotMR/Set Up Scene", priority = 0)]
        public static void SetUp()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            if (!EditorUtility.DisplayDialog("Set up RealTurtleBotMR scene",
                    "This removes the 3 simulated robots and the old multi-robot dashboard from RealTurtleBotMR.unity " +
                    "(the original MRScene is not touched) and builds the single real-TurtleBot dashboard.\n\nContinue?",
                    "Set up", "Cancel")) return;

            // 1. Clean up ------------------------------------------------------
            var removed = new List<string>();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (!RemoveRoots.Contains(go.name)) continue;
                removed.Add(go.name);
                Undo.DestroyObjectImmediate(go);
            }

            rounded = EnsureRoundedSprite();
            font = TMP_Settings.defaultFontAsset;

            // 2. Robot ---------------------------------------------------------
            var robot = new GameObject("RealTurtleBot");
            Undo.RegisterCreatedObjectUndo(robot, "Create RealTurtleBot");
            var tele = robot.AddComponent<TurtleBotTelemetry>();
            tele.robotId = "turtlebot3";
            var anchor = robot.AddComponent<RobotWorldAnchor>();
            anchor.telemetry = tele;
            var fp = robot.AddComponent<RobotFootprint>();
            fp.telemetry = tele; fp.anchor = anchor;
            robot.AddComponent<EditorQuickTest>().anchor = anchor;   // Editor-only helpers
            robot.AddComponent<RoomMeshVisibility>();                 // keep the blue room scan hidden
            robot.AddComponent<MapOverlay>().anchor = anchor;         // SLAM map on the floor
            var nav = robot.AddComponent<NavGoalSender>();            // point-and-go Nav2 goals
            nav.anchor = anchor; nav.telemetry = tele;

            var tag = BuildHeadTag(robot.transform);
            tag.telemetry = tele; tag.anchor = anchor; tag.follow = robot.transform;

            // 3. Panel ---------------------------------------------------------
            var panel = BuildPanel();
            panel.telemetry = tele; panel.anchor = anchor; panel.nav = nav;
            Undo.RegisterCreatedObjectUndo(panel.gameObject, "Create TelemetryPanel");

            // 4. Scene tweaks --------------------------------------------------
            // Our RoomMeshVisibility replaces the host project's room-mesh scripts in THIS scene
            // (they unhide the glowing blue room scan).
            foreach (var root in scene.GetRootGameObjects())
            foreach (var name in new[] { "MRSceneConfig", "SceneMeshOcclusion" })
            {
                var b = root.GetComponent(name) as Behaviour;
                if (b == null || !b.enabled) continue;
                Undo.RecordObject(b, "Disable " + name);
                b.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(b);
            }
            // Nothing here uses /tf; listening to it only adds Wi-Fi traffic.
            var ros = Object.FindObjectOfType<Unity.Robotics.ROSTCPConnector.ROSConnection>();
            if (ros != null && ros.listenForTFMessages)
            {
                Undo.RecordObject(ros, "Disable TF listening");
                ros.listenForTFMessages = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ros);
            }

            // 5. Build settings -------------------------------------------------
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath)
                .Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = robot;

            Debug.Log("[RealTurtleBotMR] Scene ready. Removed: " +
                      (removed.Count > 0 ? string.Join(", ", removed) : "nothing (already clean)") +
                      ". Built: RealTurtleBot (+HeadTag), TelemetryPanel. RealTurtleBotMR is now the only enabled build scene.");
        }

        // ================================================================== HEAD TAG
        static RobotHeadTag BuildHeadTag(Transform parent)
        {
            var root = MakeCanvas("HeadTag", parent, new Vector2(360, 150), 0.0005f);
            root.transform.localPosition = new Vector3(0, 0.6f, 0);
            var group = root.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            var tag = root.AddComponent<RobotHeadTag>();
            tag.group = group;

            Img("Bg", root.transform, MRStyle.PanelBg, 1.4f).rectTransform.Stretch();

            tag.statusDot = Img("StatusDot", root.transform, MRStyle.Green, 1f);
            tag.statusDot.rectTransform.TopLeft(26, 30, 22, 22);

            tag.idText = MakeText("Id", root.transform, "TB3-01", 40, MRStyle.TextPrimary, FontStyles.Bold);
            tag.idText.rectTransform.TopLeft(60, 14, 200, 52);
            tag.idText.alignment = TextAlignmentOptions.MidlineLeft;

            tag.batteryText = MakeText("BatteryPct", root.transform, "--%", 36, MRStyle.TextPrimary, FontStyles.Bold);
            tag.batteryText.rectTransform.TopRight(22, 14, 120, 52);
            tag.batteryText.alignment = TextAlignmentOptions.MidlineRight;

            tag.statusText = MakeText("Status", root.transform, "WAITING FOR DATA", 24, MRStyle.Grey, FontStyles.Bold);
            tag.statusText.rectTransform.TopLeft(26, 70, 310, 34);
            tag.statusText.characterSpacing = 4;
            tag.statusText.alignment = TextAlignmentOptions.MidlineLeft;

            var track = Img("BatteryTrack", root.transform, MRStyle.Track, 4f);
            track.rectTransform.BottomLeft(26, 24, 308, 14);
            tag.batteryFill = Img("BatteryFill", track.transform, MRStyle.Green, 4f);
            var fr = tag.batteryFill.rectTransform;
            fr.anchorMin = new Vector2(0, 0); fr.anchorMax = new Vector2(0.6f, 1);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            tag.batteryFillRect = fr;
            return tag;
        }

        // ================================================================== PANEL
        //  ┌────────────────────────────────────────────────────────────────┐
        //  │ ▌turtlebot3                                  ● MOVING   ● LIVE 20Hz│
        //  │  TurtleBot3 Burger · /odom · /battery_state                    │
        //  ├──────────────────────────────────┬─────────────────────────────┤
        //  │                                  │ BATTERY   92 %     12.3 V   │
        //  │        RealSense camera          │ ████████████████░░░         │
        //  │                                  ├──────────────┬──────────────┤
        //  │                                  │ SPEED        │ TURN RATE    │
        //  │                                  │ +0.12 m/s    │ +15 °/s      │
        //  │ ● REALSENSE 424×240 · 15 fps     ├──────────────┴──────────────┤
        //  │                                  │ POSE  X +0.40 m        (↑)  │
        //  │                                  │       Y -0.07 m   compass   │
        //  ├──────────────────────────────────┴─────────────────────────────┤
        //  │ Aligned · grip + stick fine-tune · A re-align · B bring panel  │
        //  └────────────────────────────────────────────────────────────────┘
        static TelemetryPanel BuildPanel()
        {
            const float W = 1000, H = 540;
            var root = MakeCanvas("TelemetryPanel", null, new Vector2(W, H), 0.0005f);
            root.transform.position = new Vector3(0.42f, 1.25f, 1.0f);
            var p = root.AddComponent<TelemetryPanel>();
            var col = root.AddComponent<BoxCollider>();      // so the laser pointer can hit it
            col.size = new Vector3(W, H, 10);

            Img("Bg", root.transform, MRStyle.PanelBg, 1.0f).rectTransform.Stretch();

            // ---- header
            Img("Accent", root.transform, MRStyle.Cyan, 6f).rectTransform.TopLeft(28, 30, 6, 58);
            p.title = MakeText("Title", root.transform, "turtlebot3", 40, MRStyle.TextPrimary, FontStyles.Bold);
            p.title.rectTransform.TopLeft(50, 22, 420, 46);
            p.subtitle = MakeText("Subtitle", root.transform, "TurtleBot3 Burger", 18, MRStyle.TextSecondary, FontStyles.Normal);
            p.subtitle.rectTransform.TopLeft(50, 68, 620, 24);

            var linkPill = Img("LinkPill", root.transform, MRStyle.TileBg, 1.4f);
            linkPill.rectTransform.TopRight(24, 34, 180, 46);
            p.linkDot = Img("Dot", linkPill.transform, MRStyle.Red, 1f);
            p.linkDot.rectTransform.TopLeft(18, 16, 14, 14);
            p.linkText = Label(linkPill.transform, "NO LINK", 18, MRStyle.Red, 42, 0, 132, 46);

            p.statusPill = Img("StatusPill", root.transform, MRStyle.TileBg, 1.4f);
            p.statusPill.rectTransform.TopRight(216, 34, 210, 46);
            p.statusDot = Img("Dot", p.statusPill.transform, MRStyle.Grey, 1f);
            p.statusDot.rectTransform.TopLeft(18, 16, 14, 14);
            p.statusText = Label(p.statusPill.transform, "WAITING FOR DATA", 18, MRStyle.Grey, 42, 0, 164, 46);

            var div = Img("Divider", root.transform, MRStyle.Divider, 1f); div.sprite = null;
            div.rectTransform.TopLeft(24, 106, W - 48, 2);

            // ---- camera card (left)
            const float top = 124, camW = 576, camH = 352;
            var camCard = Img("CameraCard", root.transform, MRStyle.TileBg, 2.2f);
            camCard.rectTransform.TopLeft(24, top, camW, camH);
            var video = Img("Video", camCard.transform, new Color(0, 0, 0, 0.55f), 3f);
            video.rectTransform.TopLeft(10, 10, camW - 20, 298);
            video.gameObject.AddComponent<RectMask2D>();
            var feedGo = new GameObject("Feed", typeof(RectTransform));
            feedGo.transform.SetParent(video.transform, false);
            var raw = feedGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fit = feedGo.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 424f / 240f;
            var placeholder = MakeText("Placeholder", video.transform,
                "No camera stream\n<size=70%><color=#7A8291>start the RealSense on the robot</color></size>",
                22, MRStyle.TextSecondary, FontStyles.Bold);
            placeholder.rectTransform.Stretch();
            placeholder.alignment = TextAlignmentOptions.Center;
            placeholder.enableWordWrapping = true;
            p.cameraLiveDot = Img("LiveDot", camCard.transform, MRStyle.Grey, 1f);
            p.cameraLiveDot.rectTransform.TopLeft(16, 322, 12, 12);
            p.cameraInfo = Label(camCard.transform, "REALSENSE  offline", 16, MRStyle.TextSecondary, 36, 310, camW - 50, 36);

            var feed = root.AddComponent<RobotCameraFeed>();
            feed.target = raw; feed.fitter = fit; feed.placeholder = placeholder.gameObject;
            p.cameraFeed = feed;

            // ---- right column
            const float rx = 24 + camW + 16, rw = W - rx - 24;   // 360 wide

            // battery
            var bat = Card(root.transform, "BatteryCard", rx, top, rw, 108, "BATTERY");
            p.batteryValue = MakeText("Value", bat, "--", 42, MRStyle.TextPrimary, FontStyles.Bold);
            p.batteryValue.rectTransform.TopLeft(16, 30, 170, 50);
            p.batteryValue.alignment = TextAlignmentOptions.MidlineLeft;
            p.batterySub = MakeText("Sub", bat, "", 16, MRStyle.TextSecondary, FontStyles.Normal);
            p.batterySub.rectTransform.TopRight(16, 44, 180, 24);
            p.batterySub.alignment = TextAlignmentOptions.MidlineRight;
            var bTrack = Img("Track", bat, MRStyle.Track, 4f);
            bTrack.rectTransform.BottomLeft(16, 14, rw - 32, 10);
            p.batteryFill = Img("Fill", bTrack.transform, MRStyle.Green, 4f);
            p.batteryFillRect = p.batteryFill.rectTransform;
            p.batteryFillRect.anchorMin = Vector2.zero; p.batteryFillRect.anchorMax = new Vector2(0.5f, 1);
            p.batteryFillRect.offsetMin = p.batteryFillRect.offsetMax = Vector2.zero;

            // motion
            var mot = Card(root.transform, "MotionCard", rx, top + 120, rw, 108, "SPEED");
            var turnLabel = MakeText("Label2", mot, "TURN RATE", 15, MRStyle.TextMuted, FontStyles.Bold);
            turnLabel.characterSpacing = 6; turnLabel.rectTransform.TopLeft(rw / 2 + 8, 12, rw / 2 - 24, 20);
            p.speedValue = MakeText("Speed", mot, "--", 30, MRStyle.TextPrimary, FontStyles.Bold);
            p.speedValue.rectTransform.TopLeft(16, 34, rw / 2 - 24, 40);
            p.speedValue.alignment = TextAlignmentOptions.MidlineLeft;
            p.turnValue = MakeText("Turn", mot, "--", 30, MRStyle.TextPrimary, FontStyles.Bold);
            p.turnValue.rectTransform.TopLeft(rw / 2 + 8, 34, rw / 2 - 24, 40);
            p.turnValue.alignment = TextAlignmentOptions.MidlineLeft;
            p.speedBarRect = CenterBar(mot, 16, rw / 2 - 32);
            p.turnBarRect = CenterBar(mot, rw / 2 + 8, rw / 2 - 32);

            // pose
            var pose = Card(root.transform, "PoseCard", rx, top + 240, rw, 112, "POSE  (ODOM)");
            p.xValue = PoseRow(pose, "X", 36);
            p.yValue = PoseRow(pose, "Y", 60);
            p.headingValue = PoseRow(pose, "YAW", 84);
            var ring = Img("Compass", pose, MRStyle.Track, 0.7f);
            ring.rectTransform.TopRight(18, 14, 84, 84);
            var inner = Img("Inner", ring.transform, MRStyle.PanelBg, 0.8f);
            inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            inner.rectTransform.sizeDelta = new Vector2(72, 72);
            var zero = MakeText("Zero", ring.transform, "0°", 12, MRStyle.TextMuted, FontStyles.Bold);
            zero.rectTransform.anchorMin = zero.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            zero.rectTransform.pivot = new Vector2(0.5f, 1f);
            zero.rectTransform.anchoredPosition = new Vector2(0, -8); zero.rectTransform.sizeDelta = new Vector2(30, 14);
            zero.alignment = TextAlignmentOptions.Center;
            var needle = Img("Needle", ring.transform, MRStyle.Cyan, 6f);
            var nr = needle.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 0.5f);
            nr.pivot = new Vector2(0.5f, 0.1f);
            nr.anchoredPosition = Vector2.zero; nr.sizeDelta = new Vector2(6, 30);
            var hub = Img("Hub", ring.transform, MRStyle.TextPrimary, 1f);
            hub.rectTransform.anchorMin = hub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hub.rectTransform.sizeDelta = new Vector2(10, 10);
            p.compassNeedle = nr;

            // ---- footer
            p.hintText = MakeText("Hint", root.transform, "", 17, MRStyle.TextSecondary, FontStyles.Normal);
            p.hintText.rectTransform.TopLeft(28, 490, W - 56, 34);
            p.hintText.alignment = TextAlignmentOptions.MidlineLeft;
            return p;
        }

        static Transform Card(Transform parent, string name, float x, float y, float w, float h, string label)
        {
            var c = Img(name, parent, MRStyle.TileBg, 2.2f);
            c.rectTransform.TopLeft(x, y, w, h);
            var l = MakeText("Label", c.transform, label, 15, MRStyle.TextMuted, FontStyles.Bold);
            l.characterSpacing = 6; l.rectTransform.TopLeft(16, 12, w - 32, 20);
            return c.transform;
        }

        static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, float x, float y, float w, float h)
        {
            var t = MakeText("Label", parent, text, size, color, FontStyles.Bold);
            t.characterSpacing = 4;
            t.rectTransform.TopLeft(x, y, w, h);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            return t;
        }

        static TextMeshProUGUI PoseRow(Transform card, string label, float y)
        {
            var l = MakeText("Lbl_" + label, card, label, 15, MRStyle.TextMuted, FontStyles.Bold);
            l.characterSpacing = 4; l.rectTransform.TopLeft(16, y, 46, 24);
            l.alignment = TextAlignmentOptions.MidlineLeft;
            var v = MakeText("Val_" + label, card, "--", 22, MRStyle.TextPrimary, FontStyles.Bold);
            v.rectTransform.TopLeft(66, y, 170, 24);
            v.alignment = TextAlignmentOptions.MidlineLeft;
            return v;
        }

        /// <summary>Thin track with a centre tick; returns the fill that grows left/right from the middle.</summary>
        static RectTransform CenterBar(Transform card, float x, float w)
        {
            var track = Img("Bar", card, MRStyle.Track, 4f);
            track.rectTransform.BottomLeft(x, 16, w, 8);
            var tick = Img("Mid", track.transform, MRStyle.TextMuted, 1f); tick.sprite = null;
            var tr = tick.rectTransform;
            tr.anchorMin = new Vector2(0.5f, 0); tr.anchorMax = new Vector2(0.5f, 1);
            tr.sizeDelta = new Vector2(2, 6); tr.anchoredPosition = Vector2.zero;
            var fill = Img("Fill", track.transform, MRStyle.Cyan, 4f);
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0.5f, 0); fr.anchorMax = new Vector2(0.5f, 1);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            return fr;
        }

        // ================================================================== HELPERS
        static GameObject MakeCanvas(string name, Transform parent, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;
            return go;
        }

        /// <param name="roundness">Higher = smaller corner radius (Image.pixelsPerUnitMultiplier).</param>
        static Image Img(string name, Transform parent, Color color, float roundness)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, roundness);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI MakeText(string name, Transform parent, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.TopLeft;
            return t;
        }

        static void Stretch(this RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
        static void TopLeft(this RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }
        static void TopRight(this RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 1);
            r.anchoredPosition = new Vector2(-x, -y); r.sizeDelta = new Vector2(w, h);
        }
        static void BottomLeft(this RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 0);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        }

        /// <summary>66×66 white rounded square (radius 32) imported as a 9-slice sprite.</summary>
        static Sprite EnsureRoundedSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (existing != null) return existing;

            const int size = 66; const float rad = 32f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, rad, size - rad);
                float cy = Mathf.Clamp(y + 0.5f, rad, size - rad);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(rad - d + 0.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
            File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = new Vector4(32, 32, 32, 32);
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
    }
}
