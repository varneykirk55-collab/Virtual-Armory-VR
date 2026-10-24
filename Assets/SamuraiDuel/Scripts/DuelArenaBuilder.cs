using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SamuraiDuel
{
    /// <summary>
    /// Builds the duel objects (samurai rig, HUD, start plate, arena markers) from primitives so no
    /// new art is required. Called by the editor menu (Tools ▸ Samurai Duel); kept in the runtime
    /// assembly so it can also be used from a bootstrap script if needed.
    /// </summary>
    public static class DuelArenaBuilder
    {
        public struct Palette
        {
            public Material armor;
            public Material cloth;
            public Material lacquer;
            public Material gold;
            public Material steel;
            public Material flash;
            public Material telegraph;
            public Material plate;
            public Font font;
        }

        public struct Result
        {
            public GameObject root;
            public DuelManager manager;
            public SamuraiOpponent samurai;
            public DuelHUD hud;
            public dummyTrigger startPlate;
            public Transform arenaCenter;
        }

        private static readonly List<Renderer> bodyRenderers = new List<Renderer>();

        /// <param name="center">Arena centre on the floor.</param>
        /// <param name="forward">Flat direction from the player's start position towards the arena.</param>
        public static Result Build(Vector3 center, Vector3 forward, float arenaRadius, Palette palette,
            PlayerCombatant player)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;

            var result = new Result();
            var root = new GameObject("SamuraiDuel");
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward));
            result.root = root;

            var arenaCenter = new GameObject("ArenaCenter").transform;
            arenaCenter.SetParent(root.transform, false);
            result.arenaCenter = arenaCenter;

            new GameObject("DuelAudio", typeof(DuelAudio)).transform.SetParent(root.transform, false);

            BuildArenaMarkers(root.transform, arenaRadius, palette);
            result.samurai = BuildSamurai(root.transform, arenaCenter, arenaRadius, palette);
            result.startPlate = BuildStartPlate(root.transform, arenaRadius, palette);
            result.hud = BuildHud(root.transform, result.samurai.transform, arenaRadius, palette, out _);

            result.manager = root.AddComponent<DuelManager>();
            result.manager.Wire(result.samurai, player, result.hud);
            return result;
        }

        // ---- samurai -----------------------------------------------------------------------------

        private static SamuraiOpponent BuildSamurai(Transform parent, Transform arenaCenter, float radius, Palette p)
        {
            bodyRenderers.Clear();

            var samuraiGo = new GameObject("Samurai Opponent");
            Transform root = samuraiGo.transform;
            root.SetParent(parent, false);
            // Stand a little past the centre, facing back towards the player's start.
            root.localPosition = new Vector3(0f, 0f, 0.8f);
            root.localRotation = Quaternion.LookRotation(Vector3.back);

            var rb = samuraiGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var health = samuraiGo.AddComponent<CombatHealth>();
            health.SetMax(120f);

            Transform body = Empty("Body", root, Vector3.zero);

            // Lower body
            Part(PrimitiveType.Cylinder, "Hakama", body, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.45f, 0.42f), p.cloth);
            Part(PrimitiveType.Cube, "Foot L", body, new Vector3(-0.12f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 0.26f), p.lacquer);
            Part(PrimitiveType.Cube, "Foot R", body, new Vector3(0.12f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 0.26f), p.lacquer);
            // Armour
            Part(PrimitiveType.Cube, "Kusazuri", body, new Vector3(0f, 0.86f, 0f), new Vector3(0.52f, 0.2f, 0.36f), p.armor);
            Part(PrimitiveType.Cube, "Obi", body, new Vector3(0f, 0.97f, 0f), new Vector3(0.47f, 0.06f, 0.31f), p.gold);
            Part(PrimitiveType.Cube, "Do", body, new Vector3(0f, 1.2f, 0f), new Vector3(0.44f, 0.46f, 0.28f), p.armor);
            Part(PrimitiveType.Cube, "Sode L", body, new Vector3(-0.31f, 1.33f, 0f), new Vector3(0.15f, 0.22f, 0.26f), p.armor, new Vector3(0f, 0f, -14f));
            Part(PrimitiveType.Cube, "Sode R", body, new Vector3(0.31f, 1.33f, 0f), new Vector3(0.15f, 0.22f, 0.26f), p.armor, new Vector3(0f, 0f, 14f));
            // Head
            Part(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.56f, 0f), new Vector3(0.21f, 0.23f, 0.21f), p.lacquer);
            Part(PrimitiveType.Cube, "Menpo", body, new Vector3(0f, 1.52f, 0.095f), new Vector3(0.15f, 0.09f, 0.05f), p.armor);
            Part(PrimitiveType.Sphere, "Kabuto", body, new Vector3(0f, 1.64f, -0.01f), new Vector3(0.26f, 0.16f, 0.27f), p.lacquer);
            Part(PrimitiveType.Cylinder, "Shikoro", body, new Vector3(0f, 1.6f, -0.03f), new Vector3(0.36f, 0.015f, 0.36f), p.lacquer);
            Part(PrimitiveType.Cube, "Kuwagata L", body, new Vector3(-0.05f, 1.76f, 0.11f), new Vector3(0.025f, 0.17f, 0.01f), p.gold, new Vector3(0f, 0f, 22f));
            Part(PrimitiveType.Cube, "Kuwagata R", body, new Vector3(0.05f, 1.76f, 0.11f), new Vector3(0.025f, 0.17f, 0.01f), p.gold, new Vector3(0f, 0f, -22f));

            // Arms (positioned every frame by SamuraiOpponent to reach the grip)
            Transform shoulderL = Empty("Shoulder L", body, new Vector3(-0.21f, 1.36f, 0.04f));
            Transform shoulderR = Empty("Shoulder R", body, new Vector3(0.21f, 1.36f, 0.04f));
            Transform armL = Part(PrimitiveType.Capsule, "Arm L", body, new Vector3(-0.15f, 1.25f, 0.15f), new Vector3(0.1f, 0.2f, 0.1f), p.cloth).transform;
            Transform armR = Part(PrimitiveType.Capsule, "Arm R", body, new Vector3(0.15f, 1.25f, 0.15f), new Vector3(0.1f, 0.2f, 0.1f), p.cloth).transform;

            // Katana: blade along the pivot's +Z.
            Transform pivot = Empty("Sword Pivot", body, new Vector3(0f, 1.12f, 0.22f));
            var katanaGo = new GameObject("Katana");
            Transform katana = katanaGo.transform;
            katana.SetParent(pivot, false);
            Part(PrimitiveType.Cylinder, "Tsuka", katana, new Vector3(0f, 0f, 0f), new Vector3(0.034f, 0.13f, 0.034f), p.lacquer, new Vector3(90f, 0f, 0f), false);
            Part(PrimitiveType.Cylinder, "Tsuba", katana, new Vector3(0f, 0f, 0.14f), new Vector3(0.09f, 0.006f, 0.09f), p.gold, new Vector3(90f, 0f, 0f), false);
            Renderer blade = Part(PrimitiveType.Cube, "Blade", katana, new Vector3(0f, 0f, 0.565f), new Vector3(0.012f, 0.032f, 0.82f), p.steel, Vector3.zero, false);
            Empty("BladeBase", katana, new Vector3(0f, 0f, 0.16f));
            Empty("BladeTip", katana, new Vector3(0f, 0f, 0.97f));

            var bladeCollider = katanaGo.AddComponent<BoxCollider>();
            bladeCollider.center = new Vector3(0f, 0f, 0.565f);
            bladeCollider.size = new Vector3(0.03f, 0.05f, 0.82f);

            var detector = katanaGo.AddComponent<SwordHitDetector>();
            var blocker = katanaGo.AddComponent<Blocker>();
            blocker.Configure(CombatTeam.Enemy, false);

            // Hurtboxes (triggers) under Body so they follow leans and the death fall.
            var samurai = samuraiGo.AddComponent<SamuraiOpponent>();
            var hurtboxes = new List<Collider>
            {
                SphereHurtbox("Hurtbox Head", body, new Vector3(0f, 1.58f, 0f), 0.17f, 1.5f, samurai),
                CapsuleHurtbox("Hurtbox Torso", body, new Vector3(0f, 1.12f, 0f), 0.27f, 0.8f, 1f, samurai),
                CapsuleHurtbox("Hurtbox Legs", body, new Vector3(0f, 0.43f, 0f), 0.25f, 0.86f, 0.7f, samurai)
            };

            samurai.Wire(body, pivot, detector, blocker, shoulderL, shoulderR, armL, armR, hurtboxes.ToArray(),
                bodyRenderers.ToArray(), p.flash, blade, p.telegraph, arenaCenter, radius);
            return samurai;
        }

        // ---- start plate & arena -----------------------------------------------------------------

        private static dummyTrigger BuildStartPlate(Transform parent, float radius, Palette p)
        {
            var plateGo = new GameObject("Duel Start Plate");
            Transform plate = plateGo.transform;
            plate.SetParent(parent, false);
            plate.localPosition = new Vector3(0f, 0f, -(radius - 0.6f));

            Part(PrimitiveType.Cylinder, "Plate", plate, new Vector3(0f, 0.012f, 0f), new Vector3(0.9f, 0.012f, 0.9f), p.plate, Vector3.zero, false);

            var trigger = plateGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.4f, 0f);
            trigger.size = new Vector3(0.9f, 0.8f, 0.9f);

            var label = WorldCanvas("Plate Label", plate, new Vector3(0f, 0.35f, 0f), new Vector2(500f, 120f), 0.002f);
            label.transform.localRotation = Quaternion.Euler(60f, 0f, 0f) * Quaternion.LookRotation(Vector3.forward);
            MakeText(label.transform, "Label", "STEP HERE TO DUEL", p.font, 64, Color.white, Vector2.zero, new Vector2(500f, 120f));

            return plateGo.AddComponent<dummyTrigger>();
        }

        private static void BuildArenaMarkers(Transform parent, float radius, Palette p)
        {
            Transform markers = Empty("Arena Boundary", parent, Vector3.zero);
            const int posts = 16;
            for (int i = 0; i < posts; i++)
            {
                float angle = i * Mathf.PI * 2f / posts;
                var pos = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + 0.2f);
                pos.y = 0.12f;
                Part(PrimitiveType.Cube, "Post " + i, markers, pos, new Vector3(0.07f, 0.24f, 0.07f), p.gold, Vector3.zero, false);
            }
        }

        // ---- HUD ---------------------------------------------------------------------------------

        private static DuelHUD BuildHud(Transform parent, Transform samurai, float radius, Palette p, out Canvas board)
        {
            var hud = parent.gameObject.AddComponent<DuelHUD>();

            // Samurai overhead bar
            Canvas overhead = WorldCanvas("Samurai HUD", samurai, new Vector3(0f, 2.15f, 0f), new Vector2(500f, 260f), 0.0025f);
            MakeText(overhead.transform, "Name", "KAGE  the ronin", p.font, 40, new Color(1f, 0.85f, 0.6f), new Vector2(0f, 10f), new Vector2(500f, 50f));
            RectTransform samuraiFill = Bar(overhead.transform, new Vector2(0f, -35f), new Vector2(460f, 30f), new Color(0.85f, 0.12f, 0.1f));
            Text callout = MakeText(overhead.transform, "Callout", string.Empty, p.font, 80, Color.white, new Vector2(0f, 95f), new Vector2(500f, 100f));

            // Scoreboard behind the arena, facing the player
            board = WorldCanvas("Duel Scoreboard", parent, new Vector3(0f, 2.4f, radius + 0.6f), new Vector2(900f, 440f), 0.0025f);
            Image bg = UiObject<Image>("Background", board.transform, Vector2.zero, new Vector2(900f, 440f));
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;
            Text status = MakeText(board.transform, "Status", "Step on the red plate to duel", p.font, 54, Color.white, new Vector2(0f, 120f), new Vector2(860f, 160f));
            MakeText(board.transform, "You", "YOU", p.font, 36, new Color(0.7f, 1f, 0.7f), new Vector2(-370f, -15f), new Vector2(120f, 50f));
            RectTransform playerFill = Bar(board.transform, new Vector2(40f, -15f), new Vector2(700f, 32f), new Color(0.25f, 0.85f, 0.35f));
            Text score = MakeText(board.transform, "Score", "Score 0   Best 0", p.font, 44, Color.white, new Vector2(0f, -90f), new Vector2(860f, 60f));
            Text combo = MakeText(board.transform, "Combo", string.Empty, p.font, 48, new Color(1f, 0.8f, 0.3f), new Vector2(0f, -160f), new Vector2(860f, 60f));

            hud.Wire(samuraiFill, playerFill, status, score, combo, callout, new[] { overhead.transform, board.transform });
            return hud;
        }

        /// <summary>Screen-space-camera canvas with a full-screen red image used for the damage flash.</summary>
        public static Image BuildDamageFlash(Transform parent, Camera camera)
        {
            var go = new GameObject("Damage Flash Canvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Max(0.05f, camera != null ? camera.nearClipPlane + 0.05f : 0.3f);
            canvas.sortingOrder = 100;

            Image image = UiObject<Image>("Flash", go.transform, Vector2.zero, Vector2.zero);
            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image.color = new Color(0.8f, 0f, 0f, 0f);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        // ---- primitive helpers -------------------------------------------------------------------

        private static Transform Empty(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        private static Renderer Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Vector3 euler = default, bool flashes = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroyNow(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Quest: no dynamic shadows on the rig
            renderer.receiveShadows = false;
            if (flashes)
                bodyRenderers.Add(renderer);
            return renderer;
        }

        private static Collider SphereHurtbox(string name, Transform parent, Vector3 localPosition, float radius,
            float multiplier, MonoBehaviour owner)
        {
            Transform t = Empty(name, parent, localPosition);
            var col = t.gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius;
            t.gameObject.AddComponent<Hurtbox>().Configure(multiplier, owner);
            return col;
        }

        private static Collider CapsuleHurtbox(string name, Transform parent, Vector3 localPosition, float radius,
            float height, float multiplier, MonoBehaviour owner)
        {
            Transform t = Empty(name, parent, localPosition);
            var col = t.gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.radius = radius;
            col.height = height;
            col.direction = 1;
            t.gameObject.AddComponent<Hurtbox>().Configure(multiplier, owner);
            return col;
        }

        private static Canvas WorldCanvas(string name, Transform parent, Vector3 localPosition, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localPosition = localPosition;
            rt.localScale = Vector3.one * scale;
            return canvas;
        }

        private static T UiObject<T>(string name, Transform parent, Vector2 position, Vector2 size) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return go.AddComponent<T>();
        }

        private static Text MakeText(Transform parent, string name, string value, Font font, int size, Color color,
            Vector2 position, Vector2 box)
        {
            Text text = UiObject<Text>(name, parent, position, box);
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Background + fill bar. The fill is sized through anchorMax.x (no sprite needed).</summary>
        private static RectTransform Bar(Transform parent, Vector2 position, Vector2 size, Color fillColor)
        {
            Image bg = UiObject<Image>("Bar", parent, position, size);
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
            bg.raycastTarget = false;

            Image fill = UiObject<Image>("Fill", bg.transform, Vector2.zero, Vector2.zero);
            fill.color = fillColor;
            fill.raycastTarget = false;
            RectTransform rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void DestroyNow(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
