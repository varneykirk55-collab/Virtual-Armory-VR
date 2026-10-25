using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

namespace SamuraiDuel.EditorTools
{
    /// <summary>
    /// Tools ▸ Samurai Duel ▸ Build Dojo Around Duel
    ///
    /// Wraps the existing duel ring in a procedurally built dojo: plank floor, tatami fighting area,
    /// timber posts and beams, shoji walls, plaster, tokonoma alcove with a hanging ensō scroll and a
    /// displayed katana, banners and paper lanterns. Adds two grabbable katanas on a rack near the
    /// player start, upgrades the samurai's primitive katana to the same curved katana mesh, and moves
    /// the existing swords/shields onto a weapon table. All art (meshes, textures, materials, the
    /// katana prefab) is generated into Assets/SamuraiDuel/Generated; no external assets are needed.
    ///
    /// Combat code is untouched: the new katana uses the existing XRGrabInteractable +
    /// SwordHitDetector + Blocker setup, and the samurai keeps its rig and hitboxes.
    /// Re-running the menu rebuilds the dojo in place. Everything goes through Undo.
    /// </summary>
    public static class DojoBuilder
    {
        private const string GeneratedRoot = "Assets/SamuraiDuel/Generated";
        private const string DojoName = "Samurai Dojo";
        private const string DuelRootName = "SamuraiDuel";

        private const float X0 = DojoLayout.X0, X1 = DojoLayout.X1, Z0 = DojoLayout.Z0, Z1 = DojoLayout.Z1;
        private const float FloorTop = DojoLayout.FloorTop;
        private const float TableTop = DojoLayout.TableTop;
        private static Vector3 RackPosition => DojoLayout.RackPosition;
        private static Vector3 TablePosition => DojoLayout.TablePosition;

        private sealed class Art
        {
            public Material floorWood, darkWood, plaster, shoji, tatami, scroll, banner, lanternPaper, lacquer;
            public Material steel, tsukaIto, iron, brass;
            public Mesh blade, fittings, lantern;
            public GameObject katanaPrefab;
        }

        [MenuItem("Tools/Samurai Duel/Build Dojo Around Duel")]
        public static void BuildDojo()
        {
            GameObject duel = GameObject.Find(DuelRootName);
            if (duel == null)
            {
                EditorUtility.DisplayDialog("Samurai Dojo",
                    "Run Tools ▸ Samurai Duel ▸ Build Duel In Open Scene first; the dojo is built around the duel ring.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build Samurai Dojo");
            int undoGroup = Undo.GetCurrentGroup();
            var log = new StringBuilder("Samurai Dojo:\n");

            Art art = CreateArt();

            GameObject old = GameObject.Find(DojoName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            Transform frame = duel.transform;
            float floorY = MeasureFloorTop(frame, frame.position.y);
            var dojo = new GameObject(DojoName);
            Undo.RegisterCreatedObjectUndo(dojo, "Dojo");
            dojo.transform.SetPositionAndRotation(new Vector3(frame.position.x, floorY, frame.position.z),
                Quaternion.Euler(0f, frame.eulerAngles.y, 0f));
            if (Mathf.Abs(floorY - frame.position.y) > 0.03f)
                log.AppendLine($"  ! Existing floor top is {floorY:0.00} m but the duel root is at {frame.position.y:0.00} m; consider moving 'SamuraiDuel' to the floor height.");

            SamuraiOpponent samurai = Object.FindObjectOfType<SamuraiOpponent>(true);
            float radius = 3f;
            if (samurai != null)
            {
                var so = new SerializedObject(samurai);
                SerializedProperty r = so.FindProperty("arenaRadius");
                if (r != null)
                    radius = r.floatValue;
            }

            var sink = new SceneSink(dojo.transform, art);
            DojoLayout.BuildArchitecture(sink, radius);
            DojoLayout.BuildFurnishings(sink);
            log.AppendLine("• Built floor, tatami, walls, ceiling, tokonoma with scroll and katana, banners and lanterns.");

            ClearClutter(dojo.transform, duel.transform, log);
            PlaceRackKatanas(dojo.transform, art, log);
            RelocateWeapons(dojo.transform, log);

            if (samurai != null)
                UpgradeSamuraiKatana(samurai, art, log);
            TweakDuelLayout(duel.transform, log);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = dojo;
            log.AppendLine("Save the scene (Ctrl+S). Undo (Ctrl+Z) reverts the whole dojo build in one step.");
            Debug.Log(log.ToString());
        }

        // ---- assets ------------------------------------------------------------------------------

        private static Art CreateArt()
        {
            foreach (string sub in new[] { "Textures", "Meshes", "Materials", "Prefabs" })
                EnsureFolder($"{GeneratedRoot}/{sub}");
            string mats = $"{GeneratedRoot}/Materials";

            Texture2D tFloor = SaveTexture(ProceduralArt.WoodFloor());
            Texture2D tDark = SaveTexture(ProceduralArt.DarkWood());
            Texture2D tPlaster = SaveTexture(ProceduralArt.Plaster());
            Texture2D tShoji = SaveTexture(ProceduralArt.Shoji());
            Texture2D tTatami = SaveTexture(ProceduralArt.Tatami());
            Texture2D tScroll = SaveTexture(ProceduralArt.Scroll());
            Texture2D tBanner = SaveTexture(ProceduralArt.Banner());
            Texture2D tLantern = SaveTexture(ProceduralArt.LanternPaper());
            Texture2D tHamon = SaveTexture(ProceduralArt.Hamon());
            Texture2D tIto = SaveTexture(ProceduralArt.TsukaIto());

            var art = new Art
            {
                floorWood = SamuraiDuelSetup.GetMaterial("Dojo_FloorWood", Color.white, 0f, 0.35f, Color.black, tFloor, null, mats),
                darkWood = SamuraiDuelSetup.GetMaterial("Dojo_DarkWood", Color.white, 0f, 0.45f, Color.black, tDark, null, mats),
                plaster = SamuraiDuelSetup.GetMaterial("Dojo_Plaster", Color.white, 0f, 0.1f, Color.black, tPlaster, null, mats),
                shoji = SamuraiDuelSetup.GetMaterial("Dojo_Shoji", Color.white, 0f, 0.05f, new Color(0.32f, 0.3f, 0.26f), tShoji, tShoji, mats),
                tatami = SamuraiDuelSetup.GetMaterial("Dojo_Tatami", Color.white, 0f, 0.15f, Color.black, tTatami, null, mats),
                scroll = SamuraiDuelSetup.GetMaterial("Dojo_Scroll", Color.white, 0f, 0.1f, Color.black, tScroll, null, mats),
                banner = SamuraiDuelSetup.GetMaterial("Dojo_Banner", Color.white, 0f, 0.05f, Color.black, tBanner, null, mats),
                lanternPaper = SamuraiDuelSetup.GetMaterial("Dojo_LanternPaper", Color.white, 0f, 0.1f, new Color(1.3f, 0.75f, 0.35f), tLantern, tLantern, mats),
                lacquer = SamuraiDuelSetup.GetMaterial("Dojo_BlackLacquer", new Color(0.04f, 0.04f, 0.04f), 0.1f, 0.7f, Color.black, null, null, mats),
                steel = SamuraiDuelSetup.GetMaterial("Katana_Steel", Color.white, 0.9f, 0.88f, Color.black, tHamon, null, mats),
                tsukaIto = SamuraiDuelSetup.GetMaterial("Katana_TsukaIto", Color.white, 0f, 0.3f, Color.black, tIto, null, mats),
                iron = SamuraiDuelSetup.GetMaterial("Katana_Iron", new Color(0.12f, 0.11f, 0.1f), 0.7f, 0.35f, Color.black, null, null, mats),
                brass = SamuraiDuelSetup.GetMaterial("Katana_Brass", new Color(0.78f, 0.58f, 0.25f), 0.95f, 0.7f, Color.black, null, null, mats),
                blade = SaveMesh(ProceduralArt.KatanaBlade()),
                fittings = SaveMesh(ProceduralArt.KatanaFittings()),
                lantern = SaveMesh(ProceduralArt.Lantern())
            };
            art.katanaPrefab = CreateKatanaPrefab(art);
            AssetDatabase.SaveAssets();
            return art;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Writes the texture as PNG (keeping the asset GUID on re-runs) and returns the imported asset.</summary>
        private static Texture2D SaveTexture(Texture2D generated)
        {
            string path = $"{GeneratedRoot}/Textures/{generated.name}.png";
            File.WriteAllBytes(path, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Saves a mesh asset, updating an existing one in place so scene references survive re-runs.</summary>
        private static Mesh SaveMesh(Mesh mesh, string folder = "Meshes")
        {
            string path = $"{GeneratedRoot}/{folder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---- katana ------------------------------------------------------------------------------

        private static GameObject CreateKatanaVisual(Transform parent, Art art, string name, Vector3 localPosition, Quaternion localRotation)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = localRotation;
            AddRenderer(root, "Blade", art.blade, new[] { art.steel });
            AddRenderer(root, "Fittings", art.fittings, new[] { art.tsukaIto, art.iron, art.brass });
            return root.gameObject;
        }

        private static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material[] materials, bool receiveShadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            return renderer;
        }

        /// <summary>
        /// Grabbable katana prefab using the same component stack as the existing swords:
        /// Rigidbody + XRGrabInteractable (velocity tracking) + SwordHitDetector + Blocker.
        /// </summary>
        private static GameObject CreateKatanaPrefab(Art art)
        {
            var root = new GameObject("Katana");
            root.tag = "Weapon";
            AddRenderer(root.transform, "Blade", art.blade, new[] { art.steel });
            AddRenderer(root.transform, "Fittings", art.fittings, new[] { art.tsukaIto, art.iron, art.brass });

            // Hand sits just below the guard; blade tilted 30° up from the controller's forward axis
            // so a relaxed grip holds it in a natural guard (chudan) position.
            var grip = new GameObject("Grip").transform;
            grip.SetParent(root.transform, false);
            grip.localPosition = new Vector3(0f, 0f, -0.075f);
            grip.localRotation = Quaternion.Euler(30f, 0f, 0f);

            var bladeBase = new GameObject("BladeBase").transform;
            bladeBase.SetParent(root.transform, false);
            bladeBase.localPosition = new Vector3(0f, 0f, 0.06f);
            var bladeTip = new GameObject("BladeTip").transform;
            bladeTip.SetParent(root.transform, false);
            bladeTip.localPosition = new Vector3(0f, ProceduralArt.Sori * 0.9f, ProceduralArt.TipZ);

            var bladeCollider = root.AddComponent<BoxCollider>();
            bladeCollider.center = new Vector3(0f, ProceduralArt.Sori * 0.4f, ProceduralArt.HabakiLength + ProceduralArt.BladeLength * 0.5f);
            bladeCollider.size = new Vector3(0.012f, 0.036f, ProceduralArt.BladeLength);
            var handleCollider = root.AddComponent<CapsuleCollider>();
            handleCollider.direction = 2;
            handleCollider.center = new Vector3(0f, 0f, -0.142f);
            handleCollider.radius = 0.017f;
            handleCollider.height = 0.29f;
            var guardCollider = root.AddComponent<BoxCollider>();
            guardCollider.center = new Vector3(0f, 0f, -0.0035f);
            guardCollider.size = new Vector3(0.08f, 0.088f, 0.008f);

            var body = root.AddComponent<Rigidbody>();
            body.mass = 1.1f;
            body.angularDrag = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.attachTransform = grip;
            grab.throwOnDetach = true;

            root.AddComponent<SwordHitDetector>().Configure(CombatTeam.Player, true, bladeBase, bladeTip, 0f);
            root.AddComponent<Blocker>().Configure(CombatTeam.Player, false);

            string path = $"{GeneratedRoot}/Prefabs/Katana.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void PlaceRackKatanas(Transform dojo, Art art, StringBuilder log)
        {
            // Edge up, handle to the left, resting in the rack's cradles (built in BuildFurnishings).
            Quaternion rotation = Quaternion.LookRotation(Vector3.right, Vector3.down);
            float[] tiers = DojoLayout.RackTiers;
            var weapons = new GameObject("Rack Katanas").transform;
            Undo.RegisterCreatedObjectUndo(weapons.gameObject, "Rack Katanas");
            weapons.SetParent(dojo, false);
            for (int i = 0; i < tiers.Length; i++)
            {
                var katana = (GameObject)PrefabUtility.InstantiatePrefab(art.katanaPrefab, weapons);
                katana.name = $"Katana {i + 1}";
                katana.transform.localPosition = RackPosition + new Vector3(-0.12f, FloorTop + tiers[i] - 0.02f + 0.03f, -0.035f);
                katana.transform.localRotation = rotation;
            }
            log.AppendLine("• Two grabbable katanas on the rack to the player's front-right (prefab: Generated/Prefabs/Katana).");
        }

        private static void UpgradeSamuraiKatana(SamuraiOpponent samurai, Art art, StringBuilder log)
        {
            var detector = samurai.GetComponentInChildren<SwordHitDetector>(true);
            if (detector == null)
                return;
            Transform katana = detector.transform;

            foreach (string child in new[] { "Tsuka", "Tsuba", "Blade", "Katana Mesh" })
            {
                Transform t = katana.Find(child);
                if (t != null)
                    Undo.DestroyObjectImmediate(t.gameObject);
            }

            // In the samurai rig the guard sits 0.14 m up the pivot's +Z, between the two hands.
            const float tsubaZ = 0.14f;
            GameObject visual = CreateKatanaVisual(katana, art, "Katana Mesh", new Vector3(0f, 0f, tsubaZ), Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(visual, "Samurai katana");
            Renderer bladeRenderer = visual.transform.Find("Blade").GetComponent<Renderer>();

            Transform bladeBase = katana.Find("BladeBase");
            Transform bladeTip = katana.Find("BladeTip");
            if (bladeBase != null)
            {
                Undo.RecordObject(bladeBase, "Blade markers");
                bladeBase.localPosition = new Vector3(0f, 0f, tsubaZ + 0.05f);
            }
            if (bladeTip != null)
            {
                Undo.RecordObject(bladeTip, "Blade markers");
                bladeTip.localPosition = new Vector3(0f, ProceduralArt.Sori * 0.9f, tsubaZ + ProceduralArt.TipZ);
            }
            var box = katana.GetComponent<BoxCollider>();
            if (box != null)
            {
                Undo.RecordObject(box, "Blade collider");
                box.center = new Vector3(0f, ProceduralArt.Sori * 0.4f, tsubaZ + ProceduralArt.HabakiLength + ProceduralArt.BladeLength * 0.5f);
                box.size = new Vector3(0.03f, 0.05f, ProceduralArt.BladeLength);
            }

            // Point the telegraph glow at the new blade renderer (serialized field, no code change).
            var so = new SerializedObject(samurai);
            SerializedProperty prop = so.FindProperty("bladeRenderer");
            if (prop != null)
            {
                prop.objectReferenceValue = bladeRenderer;
                so.ApplyModifiedProperties();
            }
            log.AppendLine("• Samurai now wields the curved katana (telegraph glow and hit markers re-pointed).");
        }

        // ---- geometry → scene ----------------------------------------------------------------

        /// <summary>Turns DojoLayout output into GameObjects, saved mesh assets and colliders.</summary>
        private sealed class SceneSink : IDojoSink
        {
            private readonly Transform root;
            private readonly Art art;
            private readonly System.Collections.Generic.Dictionary<string, Transform> groups = new System.Collections.Generic.Dictionary<string, Transform>();

            public SceneSink(Transform root, Art art)
            {
                this.root = root;
                this.art = art;
            }

            private Transform Group(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return root;
                if (!groups.TryGetValue(name, out Transform group))
                {
                    group = new GameObject(name).transform;
                    group.SetParent(root, false);
                    groups[name] = group;
                }
                return group;
            }

            public void Mesh(string parent, string name, MeshBuilder builder, string material, bool receiveShadows)
            {
                Mesh mesh = SaveMesh(builder.ToMesh("Dojo_" + name.Replace(' ', '_')));
                AddRenderer(Group(parent), name, mesh, new[] { MaterialFor(material) }, receiveShadows);
            }

            public void Collider(string parent, string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(Group(parent), false);
                go.transform.localPosition = center;
                go.AddComponent<BoxCollider>().size = size;
            }

            public void Katana(string parent, string name, Vector3 localPosition, Quaternion localRotation) =>
                CreateKatanaVisual(Group(parent), art, name, localPosition, localRotation);

            public void Lantern(string parent, Vector3 localPosition, Quaternion localRotation)
            {
                var lantern = new GameObject("Lantern");
                lantern.transform.SetParent(Group(parent), false);
                lantern.transform.localPosition = localPosition;
                lantern.transform.localRotation = localRotation;
                lantern.AddComponent<MeshFilter>().sharedMesh = art.lantern;
                var r = lantern.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { art.lanternPaper, art.lacquer };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            private Material MaterialFor(string key)
            {
                switch (key)
                {
                    case "floorWood": return art.floorWood;
                    case "darkWood": return art.darkWood;
                    case "plaster": return art.plaster;
                    case "shoji": return art.shoji;
                    case "tatami": return art.tatami;
                    case "scroll": return art.scroll;
                    case "banner": return art.banner;
                    case "lanternPaper": return art.lanternPaper;
                    default: return art.darkWood;
                }
            }
        }

        // ---- scene integration -------------------------------------------------------------------

        private static bool InsideFootprint(Transform dojo, Vector3 worldPoint, float margin)
        {
            Vector3 p = dojo.InverseTransformPoint(worldPoint);
            return p.x > X0 - margin && p.x < X1 + margin && p.z > Z0 - margin && p.z < Z1 + margin;
        }

        private static float MeasureFloorTop(Transform frame, float fallback)
        {
            float top = float.MinValue;
            foreach (var r in Object.FindObjectsOfType<MeshRenderer>())
            {
                string n = r.name;
                if (!n.Contains("Floor_5x5") && !n.Contains("Ground"))
                    continue;
                Vector3 local = frame.InverseTransformPoint(r.bounds.center);
                if (local.x < X0 || local.x > X1 || local.z < Z0 || local.z > Z1)
                    continue;
                if (r.bounds.max.y < fallback + 0.5f)
                    top = Mathf.Max(top, r.bounds.max.y);
            }
            return top > float.MinValue ? top : fallback;
        }

        /// <summary>
        /// Deactivates prototype props whose bounds centre is inside the dojo (crates, columns, ramps,
        /// placeholder characters…). Floors, the XR rig, the duel, interactables, dummies and pressure
        /// plates are never deactivated; pressure plates inside only have their renderer hidden.
        /// </summary>
        private static void ClearClutter(Transform dojo, Transform duel, StringBuilder log)
        {
            GameObject rig = GameObject.FindWithTag("Player");
            var handled = new HashSet<GameObject>();
            var hidden = new List<string>();

            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                GameObject top = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
                if (top == null)
                    top = renderer.gameObject;
                if (!handled.Add(top) || !top.activeInHierarchy)
                    continue;
                Transform t = top.transform;
                if (t.IsChildOf(dojo) || t.IsChildOf(duel) || (rig != null && t.IsChildOf(rig.transform)))
                    continue;
                if (top.GetComponentInParent<XRBaseInteractable>() != null || top.GetComponentInChildren<XRBaseInteractable>() != null)
                    continue;
                if (top.GetComponentInChildren<trainingDummy>(true) != null)
                    continue;

                Bounds b = CombinedBounds(top);
                if (!InsideFootprint(dojo, b.center, 0f) || b.size.x > 14f || b.size.z > 14f)
                    continue;
                float floorY = dojo.position.y;
                if (b.max.y < floorY + 0.06f || top.name.Contains("Floor_5x5") || top.name.Contains("Ground"))
                    continue;

                if (top.GetComponentInChildren<dummyTrigger>(true) != null)
                {
                    foreach (var r in top.GetComponentsInChildren<Renderer>())
                    {
                        Undo.RecordObject(r, "Hide plate");
                        r.enabled = false;
                    }
                    hidden.Add(top.name + " (renderer only, trigger kept)");
                    continue;
                }

                Undo.RecordObject(top, "Clear dojo clutter");
                top.SetActive(false);
                hidden.Add(top.name);
            }

            if (hidden.Count > 0)
                log.AppendLine($"• Deactivated {hidden.Count} prototype props inside the dojo: {string.Join(", ", hidden)}");
        }

        private static Bounds CombinedBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>Moves the original swords and shields onto the weapon table; other props (the water pistol) are put away.</summary>
        private static void RelocateWeapons(Transform dojo, StringBuilder log)
        {
            var toPlace = new List<XRGrabInteractable>();
            foreach (var grab in Object.FindObjectsOfType<XRGrabInteractable>())
            {
                if (grab.transform.IsChildOf(dojo) || !InsideFootprint(dojo, grab.transform.position, 0.5f))
                    continue;
                string lower = grab.name.ToLowerInvariant();
                if (lower.Contains("sword") || lower.Contains("shield") || lower.Contains("katana"))
                {
                    toPlace.Add(grab);
                }
                else
                {
                    Undo.RecordObject(grab.gameObject, "Put away prop");
                    grab.gameObject.SetActive(false);
                    log.AppendLine($"• Put away '{grab.name}' (not a samurai weapon). Re-enable it any time.");
                }
            }

            float tableTop = dojo.TransformPoint(TablePosition + Vector3.up * (FloorTop + TableTop)).y;
            for (int i = 0; i < toPlace.Count; i++)
            {
                Transform t = toPlace[i].transform;
                float slot = toPlace.Count == 1 ? 0f : Mathf.Lerp(-0.55f, 0.55f, i / (float)(toPlace.Count - 1));
                Vector3 target = dojo.TransformPoint(TablePosition + new Vector3(slot, 0f, 0f));
                Bounds b = CombinedBounds(t.gameObject);
                Vector3 delta = new Vector3(target.x - b.center.x, tableTop + 0.01f - b.min.y, target.z - b.center.z);
                Undo.RecordObject(t, "Move weapon to table");
                t.position += delta;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
            if (toPlace.Count > 0)
                log.AppendLine($"• Moved {toPlace.Count} original sword(s)/shield(s) onto the weapon table to the player's front-left.");
        }

        private static void TweakDuelLayout(Transform duel, StringBuilder log)
        {
            Transform board = duel.Find("Duel Scoreboard");
            if (board != null)
            {
                // Lift the scoreboard above the tokonoma so it doesn't hide the scroll.
                Undo.RecordObject(board, "Raise scoreboard");
                Vector3 p = board.localPosition;
                board.localPosition = new Vector3(p.x, 3.2f, p.z);
            }
            Transform posts = duel.Find("Arena Boundary");
            if (posts != null && posts.gameObject.activeSelf)
            {
                Undo.RecordObject(posts.gameObject, "Hide posts");
                posts.gameObject.SetActive(false); // the tatami edge now marks the ring
            }
            log.AppendLine("• Scoreboard raised above the alcove; gold boundary posts hidden (tatami edging marks the ring).");
        }
    }
}
