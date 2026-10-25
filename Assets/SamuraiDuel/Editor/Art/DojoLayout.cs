using UnityEngine;

namespace SamuraiDuel.EditorTools
{
    /// <summary>Receives the dojo's geometry; the editor tool turns it into GameObjects and assets.</summary>
    internal interface IDojoSink
    {
        /// <param name="parent">"" for the dojo root, otherwise the name of a child group.</param>
        /// <param name="material">Key of a generated material (floorWood, darkWood, plaster, shoji, tatami, scroll, banner, lanternPaper).</param>
        void Mesh(string parent, string name, MeshBuilder builder, string material, bool receiveShadows);
        void Collider(string parent, string name, Vector3 center, Vector3 size);
        void Katana(string parent, string name, Vector3 localPosition, Quaternion localRotation);
        void Lantern(string parent, Vector3 localPosition, Quaternion localRotation);
    }

    /// <summary>
    /// Pure layout of the dojo in duel space (origin = arena centre, +Z = away from the player
    /// start). No UnityEditor dependencies, so it can be previewed outside the editor.
    /// </summary>
    internal static class DojoLayout
    {
        public const float X0 = -5f, X1 = 5f, Z0 = -6f, Z1 = 5.5f;
        public const float WallTop = 4f;
        public const float FloorTop = 0.012f; // dojo floor sits just above the existing floor tiles
        public static readonly Vector3 RackPosition = new Vector3(1.35f, 0f, -3.3f);
        public static readonly Vector3 TablePosition = new Vector3(-1.5f, 0f, -3.3f);
        public const float TableTop = 0.72f;
        public static readonly float[] RackTiers = { 0.78f, 1.03f };

        // ---- architecture ------------------------------------------------------------------------

        public static void BuildArchitecture(IDojoSink sink, float radius)
        {
            var floor = new MeshBuilder();
            var dark = new MeshBuilder();
            var plaster = new MeshBuilder();
            var shoji = new MeshBuilder();
            var tatami = new MeshBuilder();
            var ceiling = new MeshBuilder();
            float midZ = (Z0 + Z1) * 0.5f;

            floor.Box(new Vector3(0f, FloorTop - 0.04f, midZ), new Vector3(X1 - X0 + 0.3f, 0.08f, Z1 - Z0 + 0.3f), 1f / 1.2f);

            // Tatami grid sized to the ring: mats 0.9 m × 1.8 m, aligned to the area's corner.
            int cols = Mathf.CeilToInt((radius * 2f + 0.3f) / 0.9f);
            int rows = Mathf.CeilToInt((radius * 2f + 1.2f) / 1.8f);
            float w = cols * 0.9f, d = rows * 1.8f;
            tatami.Box(new Vector3(0f, FloorTop + 0.012f, 0f), new Vector3(w, 0.024f, d), Quaternion.identity,
                new Vector2(1f / 0.9f, 1f / 1.8f), new Vector2(w * 0.5f / 0.9f, d * 0.5f / 1.8f));
            // Dark wooden edging around the fighting area.
            dark.Box(new Vector3(0f, FloorTop + 0.015f, d * 0.5f + 0.04f), new Vector3(w + 0.16f, 0.03f, 0.08f), 1f / 0.6f);
            dark.Box(new Vector3(0f, FloorTop + 0.015f, -d * 0.5f - 0.04f), new Vector3(w + 0.16f, 0.03f, 0.08f), 1f / 0.6f);
            dark.Box(new Vector3(w * 0.5f + 0.04f, FloorTop + 0.015f, 0f), new Vector3(0.08f, 0.03f, d), 1f / 0.6f);
            dark.Box(new Vector3(-w * 0.5f - 0.04f, FloorTop + 0.015f, 0f), new Vector3(0.08f, 0.03f, d), 1f / 0.6f);

            Wall(new Vector3(X0, 0f, Z1), new Vector3(X1, 0f, Z1), Vector3.back, 3, true, dark, plaster, shoji);   // kamiza (far)
            Wall(new Vector3(X1, 0f, Z0), new Vector3(X0, 0f, Z0), Vector3.forward, 4, false, dark, plaster, shoji); // behind player
            Wall(new Vector3(X0, 0f, Z0), new Vector3(X0, 0f, Z1), Vector3.right, 4, false, dark, plaster, shoji);
            Wall(new Vector3(X1, 0f, Z1), new Vector3(X1, 0f, Z0), Vector3.left, 4, false, dark, plaster, shoji);

            // Ceiling boards with exposed cross beams and a ridge beam.
            ceiling.Box(new Vector3(0f, WallTop + 0.04f, midZ), new Vector3(X1 - X0 + 0.3f, 0.08f, Z1 - Z0 + 0.3f), 1f / 1.2f);
            for (int k = 1; k <= 4; k++)
            {
                float z = Mathf.Lerp(Z0, Z1, k / 5f);
                dark.Box(new Vector3(0f, WallTop - 0.15f, z), new Vector3(X1 - X0, 0.28f, 0.22f), 1f / 0.6f);
            }
            dark.Box(new Vector3(0f, WallTop - 0.1f, midZ), new Vector3(0.22f, 0.2f, Z1 - Z0), 1f / 0.6f);

            sink.Mesh("", "Floor", floor, "floorWood", true);
            sink.Mesh("", "Tatami", tatami, "tatami", true);
            sink.Mesh("", "Woodwork", dark, "darkWood", false);
            sink.Mesh("", "Plaster", plaster, "plaster", false);
            sink.Mesh("", "Shoji", shoji, "shoji", false);
            sink.Mesh("", "Ceiling", ceiling, "floorWood", false);

            // Physics: a floor slab and four walls keep the player and dropped weapons inside.
            sink.Collider("Colliders", "Floor", new Vector3(0f, FloorTop - 0.05f, midZ), new Vector3(X1 - X0, 0.1f, Z1 - Z0));
            sink.Collider("Colliders", "Wall Far", new Vector3(0f, 1.5f, Z1 + 0.1f), new Vector3(X1 - X0, 3f, 0.2f));
            sink.Collider("Colliders", "Wall Near", new Vector3(0f, 1.5f, Z0 - 0.1f), new Vector3(X1 - X0, 3f, 0.2f));
            sink.Collider("Colliders", "Wall Left", new Vector3(X0 - 0.1f, 1.5f, midZ), new Vector3(0.2f, 3f, Z1 - Z0));
            sink.Collider("Colliders", "Wall Right", new Vector3(X1 + 0.1f, 1.5f, midZ), new Vector3(0.2f, 3f, Z1 - Z0));
        }

        /// <summary>
        /// One wall from start to end (room on the 'inward' side): posts, dark wainscot, shoji panels,
        /// nageshi rail, plaster and a top beam. On the kamiza wall the centre bay becomes the alcove.
        /// </summary>
        private static void Wall(Vector3 start, Vector3 end, Vector3 inward, int bays, bool kamiza,
            MeshBuilder dark, MeshBuilder plaster, MeshBuilder shoji)
        {
            Vector3 along = end - start;
            float length = along.magnitude;
            Vector3 dir = along / length;
            Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);
            Vector3 mid = (start + end) * 0.5f;
            var woodUv = Vector2.one / 0.6f;

            for (int i = 0; i <= bays; i++)
            {
                Vector3 p = start + dir * (length * i / bays);
                dark.Box(p + inward * 0.11f + Vector3.up * (WallTop * 0.5f), new Vector3(0.22f, WallTop, 0.22f), rot, woodUv);
            }

            for (int i = 0; i < bays; i++)
            {
                float bayLength = length / bays;
                Vector3 center = start + dir * (bayLength * (i + 0.5f));
                float width = bayLength - 0.22f;
                bool alcove = kamiza && i == bays / 2;

                if (alcove)
                {
                    plaster.Box(center + inward * 0.05f + Vector3.up * ((FloorTop + 2.62f) * 0.5f),
                        new Vector3(width, 2.62f - FloorTop, 0.08f), rot, Vector2.one / 1.5f);
                    continue;
                }

                dark.Box(center + inward * 0.06f + Vector3.up * (FloorTop + 0.27f), new Vector3(width, 0.54f, 0.06f), rot, woodUv);
                int panels = Mathf.Max(1, Mathf.RoundToInt(width / 0.9f));
                float panelWidth = width / panels;
                for (int k = 0; k < panels; k++)
                {
                    Vector3 c = center + dir * (-width * 0.5f + panelWidth * (k + 0.5f));
                    shoji.Panel(c + inward * 0.05f + Vector3.up * 1.55f, inward, Vector3.up, panelWidth, 2.0f);
                }
            }

            dark.Box(mid + inward * 0.07f + Vector3.up * 2.62f, new Vector3(length, 0.15f, 0.1f), rot, woodUv);      // nageshi
            plaster.Box(mid + inward * 0.05f + Vector3.up * 3.25f, new Vector3(length, 1.2f, 0.08f), rot, Vector2.one / 1.5f);
            dark.Box(mid + inward * 0.08f + Vector3.up * (WallTop - 0.08f), new Vector3(length, 0.16f, 0.14f), rot, woodUv);
        }

        // ---- furnishings -------------------------------------------------------------------------

        public static void BuildFurnishings(IDojoSink sink)
        {
            var dark = new MeshBuilder();
            var woodUv = Vector2.one / 0.6f;

            // Tokonoma: raised platform in the kamiza alcove, scroll, display katana on a stand.
            dark.Box(new Vector3(0f, FloorTop + 0.09f, Z1 - 0.45f), new Vector3(3.0f, 0.18f, 0.75f), 1f / 0.6f);
            float tokoTop = FloorTop + 0.18f;
            dark.Box(new Vector3(0f, 0.76f, Z1 - 0.11f), new Vector3(0.82f, 0.035f, 0.035f), 1f / 0.6f);   // scroll roller
            dark.Box(new Vector3(0f, 2.545f, Z1 - 0.11f), new Vector3(0.76f, 0.03f, 0.03f), 1f / 0.6f);    // scroll hanger
            dark.Box(new Vector3(0f, tokoTop + 0.02f, Z1 - 0.45f), new Vector3(0.62f, 0.04f, 0.18f), 1f / 0.6f);
            foreach (float x in new[] { -0.24f, 0.24f })
                dark.Box(new Vector3(x, tokoTop + 0.15f, Z1 - 0.45f), new Vector3(0.04f, 0.26f, 0.06f), 1f / 0.6f);

            var scroll = new MeshBuilder();
            scroll.Panel(new Vector3(0f, 1.65f, Z1 - 0.095f), Vector3.back, Vector3.up, 0.7f, 1.75f);
            sink.Mesh("Decor", "Scroll", scroll, "scroll", false);
            sink.Katana("Decor", "Display Katana", new Vector3(-0.18f, tokoTop + 0.3f, Z1 - 0.45f),
                Quaternion.LookRotation(Vector3.right, Vector3.down));

            // Nobori banners either side of the alcove.
            var banners = new MeshBuilder();
            foreach (float x in new[] { -2.3f, 2.3f })
            {
                dark.Box(new Vector3(x, 1.6f, Z1 - 0.32f), new Vector3(0.05f, 3.2f, 0.05f), 1f / 0.6f);
                dark.Box(new Vector3(x, 3.05f, Z1 - 0.36f), new Vector3(0.72f, 0.04f, 0.04f), 1f / 0.6f);
                banners.Panel(new Vector3(x, 1.85f, Z1 - 0.38f), Vector3.back, Vector3.up, 0.62f, 2.3f);
            }
            sink.Mesh("Decor", "Banners", banners, "banner", false);

            // Andon floor lanterns at the kamiza corners.
            var andonPaper = new MeshBuilder();
            foreach (float x in new[] { -3.6f, 3.6f })
            {
                var c = new Vector3(x, 0f, Z1 - 0.6f);
                andonPaper.Box(c + Vector3.up * 0.46f, new Vector3(0.3f, 0.55f, 0.3f), Quaternion.identity, new Vector2(1f / 0.3f, 1f / 0.55f));
                dark.Box(c + Vector3.up * 0.17f, new Vector3(0.34f, 0.04f, 0.34f), 1f / 0.6f);
                dark.Box(c + Vector3.up * 0.75f, new Vector3(0.34f, 0.04f, 0.34f), 1f / 0.6f);
                foreach (var o in new[] { new Vector3(-0.15f, 0f, -0.15f), new Vector3(0.15f, 0f, -0.15f), new Vector3(-0.15f, 0f, 0.15f), new Vector3(0.15f, 0f, 0.15f) })
                    dark.Box(c + o + Vector3.up * (FloorTop + 0.08f), new Vector3(0.03f, 0.16f, 0.03f), 1f / 0.6f);
            }
            sink.Mesh("Decor", "Andon Paper", andonPaper, "lanternPaper", false);

            // Hanging chochin lanterns over the corners of the tatami.
            foreach (var p in new[] { new Vector3(-2.6f, 0f, -2.6f), new Vector3(2.6f, 0f, -2.6f), new Vector3(-2.6f, 0f, 2.6f), new Vector3(2.6f, 0f, 2.6f) })
            {
                sink.Lantern("Decor", p + Vector3.up * 3.35f, Quaternion.Euler(90f, 0f, 0f)); // mesh hangs along -Y
                dark.Box(p + Vector3.up * ((3.39f + WallTop) * 0.5f), new Vector3(0.012f, WallTop - 3.39f, 0.012f), 1f / 0.6f); // cord to ceiling
            }

            // Katana-kake (sword rack) near the player start: base, two uprights, two tiers of cradles.
            Vector3 rack = RackPosition;
            AddRackBox(sink, dark, "Katana Rack", rack + new Vector3(0f, FloorTop + 0.025f, 0f), new Vector3(0.95f, 0.05f, 0.32f));
            foreach (float x in new[] { -0.34f, 0.34f })
            {
                AddRackBox(sink, dark, "Katana Rack", rack + new Vector3(x, FloorTop + 0.55f, 0.05f), new Vector3(0.06f, 1.05f, 0.08f));
                foreach (float h in new[] { 0.78f, 1.03f })
                {
                    AddRackBox(sink, dark, "Katana Rack", rack + new Vector3(x, FloorTop + h - 0.03f, -0.04f), new Vector3(0.05f, 0.02f, 0.12f));  // cradle
                    AddRackBox(sink, dark, "Katana Rack", rack + new Vector3(x, FloorTop + h - 0.01f, -0.095f), new Vector3(0.05f, 0.06f, 0.02f)); // front lip
                }
            }

            // Low weapon table for the original swords and shields.
            Vector3 table = TablePosition;
            AddRackBox(sink, dark, "Weapon Table", table + new Vector3(0f, FloorTop + TableTop - 0.025f, 0f), new Vector3(1.5f, 0.05f, 0.6f));
            foreach (var o in new[] { new Vector3(-0.68f, 0f, -0.25f), new Vector3(0.68f, 0f, -0.25f), new Vector3(-0.68f, 0f, 0.25f), new Vector3(0.68f, 0f, 0.25f) })
                dark.Box(table + o + Vector3.up * (FloorTop + (TableTop - 0.05f) * 0.5f), new Vector3(0.06f, TableTop - 0.05f, 0.06f), Quaternion.identity, woodUv);

            sink.Mesh("Decor", "Furniture", dark, "darkWood", false);
        }

        private static void AddRackBox(IDojoSink sink, MeshBuilder mesh, string parent, Vector3 center, Vector3 size)
        {
            mesh.Box(center, size, 1f / 0.6f);
            sink.Collider(parent, "Box", center, size);
        }
    }
}
