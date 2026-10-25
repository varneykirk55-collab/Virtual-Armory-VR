using System.Collections.Generic;
using UnityEngine;

namespace SamuraiDuel.EditorTools
{
    /// <summary>
    /// Small mesh builder: boxes, panels and lathed tubes with planar / explicit UVs and several
    /// submeshes. Normals are recalculated at the end; faces never share vertices, so boxes stay flat.
    /// Winding follows Unity's convention: Cross(b - a, c - a) points to the visible side.
    /// </summary>
    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int>[] submeshes;

        public int Submesh { get; set; }

        public MeshBuilder(int submeshCount = 1)
        {
            submeshes = new List<int>[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < submeshes.Length; i++)
                submeshes[i] = new List<int>();
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            var tris = submeshes[Submesh];
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
        }

        /// <summary>Rectangle facing <paramref name="normal"/>, with UV (0,0)-(1,1) or a custom rect.</summary>
        public void Panel(Vector3 center, Vector3 normal, Vector3 up, float width, float height, Rect? uvRect = null)
        {
            Vector3 right = Vector3.Cross(normal, up).normalized * (width * 0.5f);
            Vector3 u = up.normalized * (height * 0.5f);
            Rect r = uvRect ?? new Rect(0f, 0f, 1f, 1f);
            Quad(center - right - u, center - right + u, center + right + u, center + right - u,
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin));
        }

        /// <summary>
        /// Box with planar UVs in builder space: uv = (position·faceRight, position·faceUp) * uvScale + uvOffset,
        /// so textures continue seamlessly across neighbouring boxes at a constant texel density.
        /// </summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation, Vector2 uvScale, Vector2 uvOffset = default)
        {
            Vector3 h = size * 0.5f;
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
            Face(center + x * h.x, x, y, h.z, h.y, uvScale, uvOffset);
            Face(center - x * h.x, -x, y, h.z, h.y, uvScale, uvOffset);
            Face(center + y * h.y, y, z, h.x, h.z, uvScale, uvOffset);
            Face(center - y * h.y, -y, z, h.x, h.z, uvScale, uvOffset);
            Face(center + z * h.z, z, y, h.x, h.y, uvScale, uvOffset);
            Face(center - z * h.z, -z, y, h.x, h.y, uvScale, uvOffset);
        }

        public void Box(Vector3 center, Vector3 size, float uvScale = 1f) => Box(center, size, Quaternion.identity, Vector2.one * uvScale);

        private void Face(Vector3 c, Vector3 n, Vector3 up, float halfW, float halfH, Vector2 scale, Vector2 offset)
        {
            Vector3 right = Vector3.Cross(n, up).normalized;
            Vector3 a = c - right * halfW - up * halfH;
            Vector3 b = c - right * halfW + up * halfH;
            Vector3 cc = c + right * halfW + up * halfH;
            Vector3 d = c + right * halfW - up * halfH;
            Quad(a, b, cc, d, PlanarUv(a, right, up, scale, offset), PlanarUv(b, right, up, scale, offset),
                PlanarUv(cc, right, up, scale, offset), PlanarUv(d, right, up, scale, offset));
        }

        private static Vector2 PlanarUv(Vector3 p, Vector3 right, Vector3 up, Vector2 scale, Vector2 offset) =>
            new Vector2(Vector3.Dot(p, right) * scale.x + offset.x, Vector3.Dot(p, up) * scale.y + offset.y);

        /// <summary>
        /// Elliptical tube along +Z from z0 to z1. <paramref name="radiusAt"/> scales the radius along
        /// the length (t = 0..1). Optional flat caps.
        /// </summary>
        public void Tube(float rx, float ry, float z0, float z1, int segments, int rings, System.Func<float, float> radiusAt,
            float uRepeat, float vPerMetre, bool capStart, bool capEnd, Vector3 offset = default)
        {
            int start = vertices.Count;
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;
                float z = Mathf.Lerp(z0, z1, t);
                float s = radiusAt != null ? radiusAt(t) : 1f;
                for (int j = 0; j <= segments; j++)
                {
                    // Negative angle → clockwise when viewed from -Z, which gives outward faces below.
                    float a = -2f * Mathf.PI * j / segments;
                    vertices.Add(offset + new Vector3(Mathf.Cos(a) * rx * s, Mathf.Sin(a) * ry * s, z));
                    uvs.Add(new Vector2(j / (float)segments * uRepeat, (z - z0) * vPerMetre));
                }
            }

            var tris = submeshes[Submesh];
            int stride = segments + 1;
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + stride;
                    int c = b + 1;
                    int d = a + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            }

            if (capStart)
                Cap(z0, rx * (radiusAt != null ? radiusAt(0f) : 1f), ry * (radiusAt != null ? radiusAt(0f) : 1f), segments, true, offset);
            if (capEnd)
                Cap(z1, rx * (radiusAt != null ? radiusAt(1f) : 1f), ry * (radiusAt != null ? radiusAt(1f) : 1f), segments, false, offset);
        }

        private void Cap(float z, float rx, float ry, int segments, bool facesNegativeZ, Vector3 offset)
        {
            int center = vertices.Count;
            vertices.Add(offset + new Vector3(0f, 0f, z));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int j = 0; j <= segments; j++)
            {
                float a = -2f * Mathf.PI * j / segments;
                vertices.Add(offset + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, z));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            var tris = submeshes[Submesh];
            for (int j = 0; j < segments; j++)
            {
                int r0 = center + 1 + j, r1 = r0 + 1;
                if (facesNegativeZ) { tris.Add(center); tris.Add(r0); tris.Add(r1); }
                else { tris.Add(center); tris.Add(r1); tris.Add(r0); }
            }
        }

        /// <summary>Raw strip quad used by the blade generator.</summary>
        public void AddVertex(Vector3 p, Vector2 uv) { vertices.Add(p); uvs.Add(uv); }
        public int VertexCount => vertices.Count;
        public void Triangle(int a, int b, int c) { var t = submeshes[Submesh]; t.Add(a); t.Add(b); t.Add(c); }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++)
                mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Procedural katana / lantern meshes and painted textures for the dojo.</summary>
    internal static class ProceduralArt
    {
        // ---- katana ------------------------------------------------------------------------------
        // Local frame: tsuba (guard) at the origin, blade along +Z, cutting edge facing -Y,
        // curvature (sori) lifting the tip towards +Y like a real blade held edge-down.

        public const float HabakiLength = 0.035f;
        public const float BladeLength = 0.78f;
        public const float Sori = 0.02f;
        public static float TipZ => HabakiLength + BladeLength;

        /// <summary>Shinogi-zukuri blade: 7-sided cross-section with hard edges, curved, with a kissaki.</summary>
        public static Mesh KatanaBlade()
        {
            const int segments = 32;
            const float tipStart = 0.93f;
            var mb = new MeshBuilder();
            var rings = new Vector3[segments + 1][];
            var ts = new float[segments + 1];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                ts[i] = t;
                float z = HabakiLength - 0.01f + t * (BladeLength + 0.01f);
                float yc = Sori * t * t;
                float h = Mathf.Lerp(0.032f, 0.025f, t);
                float mune = Mathf.Lerp(0.0075f, 0.005f, t);
                float shinogi = Mathf.Lerp(0.0082f, 0.0056f, t);
                float top = yc + h * 0.5f;

                if (t > tipStart)
                {
                    float k = (t - tipStart) / (1f - tipStart);
                    float s = Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
                    h *= Mathf.Max(s, 0.0001f);
                    mune *= s;
                    shinogi *= s;
                }
                float c = top - h * 0.5f;
                rings[i] = new[]
                {
                    new Vector3(0f, c + h * 0.5f + 0.0015f * (h / 0.03f), z),   // P0 mune ridge
                    new Vector3(mune * 0.5f, c + h * 0.5f, z),                    // P1
                    new Vector3(shinogi * 0.5f, c + h * 0.5f - 0.32f * h, z),     // P2 shinogi
                    new Vector3(0.0005f, c - h * 0.5f, z),                        // P3 edge
                    new Vector3(-0.0005f, c - h * 0.5f, z),                       // P4 edge
                    new Vector3(-shinogi * 0.5f, c + h * 0.5f - 0.32f * h, z),    // P5
                    new Vector3(-mune * 0.5f, c + h * 0.5f, z)                    // P6
                };
            }

            // Each strip gets its own vertices so the facets stay crisp. The two ha/ji faces map
            // u = 0 (shinogi) → 1 (edge) for the hamon texture; everything else samples dark steel.
            for (int p = 0; p < 7; p++)
            {
                int q = (p + 1) % 7;
                float ua = 0.02f, ub = 0.02f;
                if (p == 2) { ua = 0f; ub = 1f; }
                else if (p == 3) { ua = 1f; ub = 1f; }
                else if (p == 4) { ua = 1f; ub = 0f; }

                int baseIndex = mb.VertexCount;
                for (int i = 0; i <= segments; i++)
                {
                    mb.AddVertex(rings[i][p], new Vector2(ua, ts[i]));
                    mb.AddVertex(rings[i][q], new Vector2(ub, ts[i]));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = baseIndex + i * 2;       // Pa ring i
                    int d = a + 1;                   // Pb ring i
                    int b = a + 2;                   // Pa ring i+1
                    int c = a + 3;                   // Pb ring i+1
                    mb.Triangle(a, b, c);
                    mb.Triangle(a, c, d);
                }
            }
            return mb.ToMesh("Katana_Blade");
        }

        /// <summary>Submeshes: 0 = tsuka wrap, 1 = dark iron (tsuba, kashira), 2 = brass (habaki, fuchi).</summary>
        public static Mesh KatanaFittings()
        {
            var mb = new MeshBuilder(3);

            mb.Submesh = 0; // tsuka: slight hourglass, taller than wide
            mb.Tube(0.0125f, 0.0165f, -0.268f, -0.017f, 14, 12, t => 1f - 0.06f * Mathf.Sin(t * Mathf.PI),
                2f, 1f / 0.03f, false, false);

            mb.Submesh = 1;
            mb.Tube(0.040f, 0.044f, -0.007f, 0f, 28, 1, null, 1f, 10f, true, true);          // tsuba
            mb.Tube(0.0130f, 0.0170f, -0.283f, -0.266f, 14, 2, t => 1f - 0.25f * t * t, 1f, 10f, true, true); // kashira

            mb.Submesh = 2;
            mb.Tube(0.0135f, 0.0172f, -0.019f, -0.007f, 14, 1, null, 1f, 10f, true, false);  // fuchi
            mb.Box(new Vector3(0f, 0f, HabakiLength * 0.5f), new Vector3(0.0115f, 0.036f, HabakiLength), 20f); // habaki
            return mb.ToMesh("Katana_Fittings");
        }

        /// <summary>Chochin paper lantern along +Z (rotate to hang). Submesh 0 = paper, 1 = black caps.</summary>
        public static Mesh Lantern()
        {
            var mb = new MeshBuilder(2);
            mb.Submesh = 0;
            mb.Tube(0.17f, 0.17f, 0f, 0.46f, 20, 12, t => 0.55f + 0.45f * Mathf.Sin(Mathf.Lerp(0.12f, 0.88f, t) * Mathf.PI),
                1f, 1f / 0.46f, false, false);
            mb.Submesh = 1;
            mb.Tube(0.105f, 0.105f, -0.04f, 0.02f, 20, 1, null, 1f, 10f, true, false);
            mb.Tube(0.105f, 0.105f, 0.44f, 0.5f, 20, 1, null, 1f, 10f, false, true);
            return mb.ToMesh("Lantern");
        }

        // ---- textures ----------------------------------------------------------------------------

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f * 0.5f;
        }

        private static float Fbm(float x, float y, int octaves = 3)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                freq *= 2f;
                amp *= 0.5f;
            }
            return sum / (1f - Mathf.Pow(0.5f, octaves));
        }

        private static Texture2D Paint(string name, int w, int h, System.Func<int, int, Color> pixel)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>8 floorboards, 1.2 m per tile (0.15 m boards), running along V.</summary>
        public static Texture2D WoodFloor() => Paint("T_WoodFloor", 512, 512, (x, y) =>
        {
            const int boardWidth = 64;
            int board = x / boardWidth;
            int bx = x % boardWidth;
            float tone = Hash(board * 31 + 7) * 0.5f + 0.5f;
            Color baseColor = Color.Lerp(new Color(0.47f, 0.31f, 0.18f), new Color(0.63f, 0.45f, 0.28f), tone);
            float warp = Fbm(x * 0.02f + board * 3.1f, y * 0.004f) * 14f;
            float grain = Mathf.Sin(bx * 0.33f + warp) * 0.5f + 0.5f;
            float streak = Fbm(x * 0.15f + board, y * 0.01f, 2);
            Color c = baseColor * (0.82f + 0.12f * grain + 0.1f * streak);
            int joint = Mathf.FloorToInt((Hash(board * 17 + 3) * 0.5f + 0.5f) * 512f);
            if (bx < 2 || Mathf.Abs(y - joint) < 1)
                c = new Color(0.16f, 0.10f, 0.06f);
            c.a = 1f;
            return c;
        });

        public static Texture2D DarkWood() => Paint("T_DarkWood", 256, 256, (x, y) =>
        {
            float warp = Fbm(x * 0.03f, y * 0.006f) * 10f;
            float grain = Mathf.Sin(x * 0.45f + warp) * 0.5f + 0.5f;
            Color c = new Color(0.19f, 0.11f, 0.065f) * (0.8f + 0.25f * grain + 0.1f * Fbm(x * 0.2f, y * 0.02f, 2));
            c.a = 1f;
            return c;
        });

        public static Texture2D Plaster() => Paint("T_Plaster", 256, 256, (x, y) =>
        {
            float n = Fbm(x * 0.03f, y * 0.03f, 4);
            Color c = new Color(0.86f, 0.80f, 0.68f) * (0.92f + 0.1f * n);
            c.a = 1f;
            return c;
        });

        /// <summary>One shoji panel (UV 0-1): kumiko lattice over washi paper.</summary>
        public static Texture2D Shoji() => Paint("T_Shoji", 256, 512, (x, y) =>
        {
            Color wood = new Color(0.33f, 0.22f, 0.13f);
            bool frame = x < 12 || x > 243 || y < 14 || y > 497;
            bool bar = Mathf.Abs(x - 89) < 3 || Mathf.Abs(x - 167) < 3;
            for (int r = 1; r < 6 && !bar; r++)
                bar = Mathf.Abs(y - (14 + r * 484 / 6)) < 3;
            if (frame || bar)
                return wood * (0.9f + 0.15f * Fbm(x * 0.2f, y * 0.05f, 2));
            float fibre = Fbm(x * 0.08f, y * 0.08f, 3);
            Color c = new Color(0.96f, 0.93f, 0.85f) * (0.95f + 0.05f * fibre);
            c.a = 1f;
            return c;
        });

        /// <summary>One tatami mat (0.9 × 1.8 m, UV 0-1): woven igusa with dark heri borders on the long sides.</summary>
        public static Texture2D Tatami() => Paint("T_Tatami", 256, 512, (x, y) =>
        {
            if (x < 14 || x > 241)
            {
                float cloth = Mathf.Sin(y * 0.9f) * 0.5f + 0.5f;
                return new Color(0.08f, 0.12f, 0.09f) * (0.85f + 0.2f * cloth) + new Color(0, 0, 0, 1);
            }
            float strand = Mathf.Sin(x * 1.7f) * 0.5f + 0.5f;
            float weave = (y % 14) < 1 ? 0.88f : 1f;
            float tone = Fbm(x * 0.02f, y * 0.01f, 2);
            Color c = Color.Lerp(new Color(0.66f, 0.64f, 0.38f), new Color(0.76f, 0.72f, 0.46f), tone) * (0.9f + 0.1f * strand) * weave;
            if (y < 2 || y > 509)
                c *= 0.7f;
            c.a = 1f;
            return c;
        });

        /// <summary>Tsuka-ito: black silk diamonds over white samegawa (tiles every 64 px).</summary>
        public static Texture2D TsukaIto() => Paint("T_TsukaIto", 128, 128, (x, y) =>
        {
            int a = (x + y) & 63, b = (x - y + 128) & 63;
            bool silk = a < 22 || b < 22;
            if (silk)
            {
                float sheen = Mathf.Sin((a < 22 ? a : b) * 0.6f) * 0.5f + 0.5f;
                return new Color(0.05f, 0.05f, 0.06f) + new Color(0.06f, 0.06f, 0.07f) * sheen + new Color(0, 0, 0, 1);
            }
            float dots = Mathf.PerlinNoise(x * 0.6f, y * 0.6f);
            Color c = new Color(0.9f, 0.88f, 0.82f) * (0.85f + 0.15f * dots);
            c.a = 1f;
            return c;
        });

        /// <summary>Blade steel: U = shinogi → edge, V = along the blade. Wavy hamon with a frosted edge.</summary>
        public static Texture2D Hamon() => Paint("T_Hamon", 64, 512, (x, y) =>
        {
            float u = x / 63f, v = y / 511f;
            float line = 0.58f + 0.07f * Mathf.Sin(v * Mathf.PI * 2f * 9f) + 0.035f * Mathf.Sin(v * Mathf.PI * 2f * 23f + 1.3f);
            Color ji = new Color(0.58f, 0.61f, 0.66f);
            Color ha = new Color(0.93f, 0.95f, 0.97f);
            float blend = Mathf.SmoothStep(0f, 1f, (u - line) / 0.05f + 0.5f);
            float nie = Mathf.PerlinNoise(x * 1.3f, y * 0.9f);
            Color c = Color.Lerp(ji, ha, blend);
            if (Mathf.Abs(u - line) < 0.05f)
                c += new Color(0.08f, 0.08f, 0.08f) * nie;
            c.a = 1f;
            return c;
        });

        /// <summary>Kakejiku hanging scroll: brocade mount, washi paper, ink ensō and a red seal.</summary>
        public static Texture2D Scroll() => Paint("T_Scroll", 256, 640, (x, y) =>
        {
            bool mount = x < 22 || x > 233 || y < 90 || y > 560;
            if (mount)
            {
                float pattern = (((x / 8) + (y / 8)) & 1) == 0 ? 1f : 0.9f;
                return new Color(0.12f, 0.14f, 0.27f) * pattern + new Color(0, 0, 0, 1);
            }
            Color paper = new Color(0.93f, 0.89f, 0.78f) * (0.95f + 0.05f * Fbm(x * 0.05f, y * 0.05f));
            paper.a = 1f;

            // Ensō: one brush circle, thick at the start, dry and broken towards the end.
            float dx = x - 128f, dy = y - 360f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float sweep = Mathf.Repeat(110f - angle, 360f);          // brush starts at 110° and runs clockwise
            if (sweep < 320f)
            {
                float progress = sweep / 320f;
                float thickness = Mathf.Lerp(15f, 5f, progress) * (0.85f + 0.3f * Mathf.PerlinNoise(sweep * 0.05f, 3f));
                float distance = Mathf.Abs(radius - 78f);
                bool dry = progress > 0.75f && Mathf.PerlinNoise(sweep * 0.4f, radius * 0.6f) < (progress - 0.75f) * 2.4f;
                if (distance < thickness && !dry)
                    return new Color(0.06f, 0.05f, 0.05f);
            }
            if (x > 176 && x < 198 && y > 128 && y < 150)
                return (x - 176) % 7 < 2 && (y - 128) % 7 < 2 ? paper : new Color(0.72f, 0.1f, 0.08f);
            return paper;
        });

        /// <summary>Nobori banner: red cloth with a white crest.</summary>
        public static Texture2D Banner() => Paint("T_Banner", 128, 512, (x, y) =>
        {
            Color red = new Color(0.6f, 0.07f, 0.06f) * (0.9f + 0.1f * Fbm(x * 0.05f, y * 0.02f));
            red.a = 1f;
            if (y > 488)
                return red * 0.6f + new Color(0, 0, 0, 0.4f);
            Vector2 p = new Vector2(x - 64f, y - 380f);
            if (p.magnitude < 42f)
            {
                // Four-lobed flower crest with a white centre.
                bool lobe = false;
                foreach (var o in new[] { new Vector2(17f, 0f), new Vector2(-17f, 0f), new Vector2(0f, 17f), new Vector2(0f, -17f) })
                    lobe |= (p - o).magnitude < 15f;
                if (lobe && p.magnitude > 6f)
                    return red;
                return new Color(0.95f, 0.93f, 0.88f);
            }
            return red;
        });

        public static Texture2D LanternPaper() => Paint("T_LanternPaper", 128, 256, (x, y) =>
        {
            float rib = (y % 16) < 2 ? 0.75f : 1f;
            Color c = new Color(0.98f, 0.82f, 0.56f) * rib * (0.94f + 0.06f * Fbm(x * 0.1f, y * 0.1f));
            c.a = 1f;
            return c;
        });
    }
}
