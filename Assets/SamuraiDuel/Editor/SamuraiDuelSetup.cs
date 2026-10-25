using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace SamuraiDuel.EditorTools
{
    /// <summary>
    /// One-click setup so nobody has to hand-wire the duel:
    ///   Tools ▸ Samurai Duel ▸ Build Duel In Open Scene
    ///   Tools ▸ Samurai Duel ▸ Apply Quest 2 Build Settings
    /// Everything goes through Undo and only adds components/objects; the XR rig and the existing
    /// grab interactables keep their settings.
    /// </summary>
    public static class SamuraiDuelSetup
    {
        private const string MaterialFolder = "Assets/SamuraiDuel/Materials";
        private const string RootName = "SamuraiDuel";
        private const float ArenaRadius = 3f;
        private const float ArenaDistanceFromPlayer = 4f;

        [MenuItem("Tools/Samurai Duel/Build Duel In Open Scene")]
        public static void BuildDuel()
        {
            GameObject rig = GameObject.FindWithTag("Player");
            Camera head = rig != null ? rig.GetComponentInChildren<Camera>(true) : Camera.main;
            if (rig == null || head == null)
            {
                EditorUtility.DisplayDialog("Samurai Duel",
                    "Could not find the XR Origin (tag 'Player') and its camera in the open scene.\n" +
                    "Open Assets/Scenes/SampleScene.unity first.", "OK");
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Samurai Duel", "A SamuraiDuel object already exists. Rebuild it?",
                        "Rebuild", "Cancel"))
                    return;
                Undo.DestroyObjectImmediate(existing);
            }

            Undo.SetCurrentGroupName("Build Samurai Duel");
            int undoGroup = Undo.GetCurrentGroup();
            var log = new StringBuilder("Samurai Duel setup:\n");

            Vector3 forward = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
            Vector3 center = rig.transform.position + forward * ArenaDistanceFromPlayer;
            center.y = rig.transform.position.y;

            PlayerCombatant player = SetupPlayer(rig, head, log);

            DuelArenaBuilder.Result result = DuelArenaBuilder.Build(center, forward, ArenaRadius, CreatePalette(), player);
            Undo.RegisterCreatedObjectUndo(result.root, "Build Samurai Duel");
            UnityEventTools.AddPersistentListener(result.startPlate.OnPlayerEnter, result.manager.RequestStart);
            log.AppendLine($"• Built arena at {center} (radius {ArenaRadius} m), samurai, HUD and start plate.");

            SetupWeapons(log);
            SetupDummies(center, log);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = result.root;
            log.AppendLine("Move the 'SamuraiDuel' object to reposition the whole arena, then save the scene (Ctrl+S).");
            Debug.Log(log.ToString());
        }

        [MenuItem("Tools/Samurai Duel/Apply Quest 2 Build Settings")]
        public static void ApplyQuestSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                EditorUtility.DisplayDialog("Samurai Duel", "Switch the active build target to Android now? " +
                                                             "(This reimports textures and can take a few minutes.)",
                    "Switch", "Later"))
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            // Quest requires 64-bit; IL2CPP is required for ARM64.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 29)
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Physics at the Quest 2 default refresh rate so velocity-tracked swords stay tight to the hand.
            Time.fixedDeltaTime = 1f / 72f;
            var timeManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (timeManager != null && timeManager.Length > 0)
            {
                var serialized = new SerializedObject(timeManager[0]);
                SerializedProperty step = serialized.FindProperty("Fixed Timestep");
                if (step != null && step.propertyType == SerializedPropertyType.Float)
                {
                    step.floatValue = 1f / 72f;
                    serialized.ApplyModifiedProperties();
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Samurai Duel: Android/Quest 2 settings applied (IL2CPP, ARM64, min API 29, Linear, 72 Hz physics). " +
                      "Check Project Settings ▸ XR Plug-in Management ▸ Android ▸ Oculus is ticked.");
        }

        // ---- player ------------------------------------------------------------------------------

        private static PlayerCombatant SetupPlayer(GameObject rig, Camera head, StringBuilder log)
        {
            if (rig.GetComponent<CombatHealth>() == null)
                Undo.AddComponent<CombatHealth>(rig);
            var player = rig.GetComponent<PlayerCombatant>();
            if (player == null)
                player = Undo.AddComponent<PlayerCombatant>(rig);

            Transform hurtboxTransform = rig.transform.Find("Player Hurtbox");
            if (hurtboxTransform == null)
            {
                var go = new GameObject("Player Hurtbox");
                Undo.RegisterCreatedObjectUndo(go, "Player Hurtbox");
                go.transform.SetParent(rig.transform, false);
                go.transform.position = head.transform.position - Vector3.up * 0.35f;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = 0.2f;
                capsule.height = 1.15f;
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                go.AddComponent<Hurtbox>().Configure(1f, player);
                hurtboxTransform = go.transform;
            }

            Transform flashTransform = rig.transform.Find("Damage Flash Canvas");
            Image flash = flashTransform != null ? flashTransform.GetComponentInChildren<Image>(true) : null;
            if (flash == null)
            {
                flash = DuelArenaBuilder.BuildDamageFlash(rig.transform, head);
                Undo.RegisterCreatedObjectUndo(flash.canvas.gameObject, "Damage Flash");
            }

            Undo.RecordObject(player, "Wire player");
            player.Wire(head.transform, hurtboxTransform.GetComponent<CapsuleCollider>(), flash);
            EditorUtility.SetDirty(player);
            log.AppendLine("• XR Origin: added CombatHealth, PlayerCombatant, head/torso hurtbox and damage flash.");
            return player;
        }

        // ---- weapons -----------------------------------------------------------------------------

        private static void SetupWeapons(StringBuilder log)
        {
            foreach (var grab in Object.FindObjectsOfType<XRGrabInteractable>(true))
            {
                string lower = grab.name.ToLowerInvariant();
                if (lower.Contains("sword") || lower.Contains("katana"))
                {
                    SetupSword(grab);
                    log.AppendLine($"• Sword '{grab.name}': SwordHitDetector + Blocker (check BladeBase/BladeTip gizmo).");
                }
                else if (lower.Contains("shield"))
                {
                    EnsureBlocker(grab.gameObject);
                    TuneBody(grab.GetComponent<Rigidbody>());
                    log.AppendLine($"• Shield '{grab.name}': Blocker.");
                }
            }
        }

        private static void SetupSword(XRGrabInteractable grab)
        {
            GameObject go = grab.gameObject;
            var detector = go.GetComponent<SwordHitDetector>();
            if (detector == null)
                detector = Undo.AddComponent<SwordHitDetector>(go);

            Transform bladeBase = go.transform.Find("BladeBase");
            Transform bladeTip = go.transform.Find("BladeTip");
            if (bladeBase == null || bladeTip == null)
            {
                if (!SwordHitDetector.EstimateBlade(go.transform, grab.attachTransform, out Vector3 baseLocal, out Vector3 tipLocal))
                {
                    baseLocal = Vector3.zero;
                    tipLocal = Vector3.forward * 0.8f;
                }
                if (bladeBase == null)
                    bladeBase = CreateMarker(go.transform, "BladeBase", baseLocal);
                if (bladeTip == null)
                    bladeTip = CreateMarker(go.transform, "BladeTip", tipLocal);
            }

            Undo.RecordObject(detector, "Configure sword");
            detector.Configure(CombatTeam.Player, true, bladeBase, bladeTip, 0f);
            EditorUtility.SetDirty(detector);

            EnsureBlocker(go);
            TuneBody(go.GetComponent<Rigidbody>());
        }

        private static void EnsureBlocker(GameObject go)
        {
            var blocker = go.GetComponent<Blocker>();
            if (blocker == null)
                blocker = Undo.AddComponent<Blocker>(go);
            Undo.RecordObject(blocker, "Configure blocker");
            blocker.Configure(CombatTeam.Player, false);
            EditorUtility.SetDirty(blocker);
        }

        private static void TuneBody(Rigidbody body)
        {
            if (body == null)
                return;
            Undo.RecordObject(body, "Tune weapon rigidbody");
            // Smooth rendering of the velocity-tracked weapon and fewer missed contacts at swing speed.
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        }

        private static Transform CreateMarker(Transform parent, string name, Vector3 localPosition)
        {
            var marker = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(marker.gameObject, name);
            marker.SetParent(parent, false);
            marker.localPosition = localPosition;
            return marker;
        }

        // ---- training dummies --------------------------------------------------------------------

        private static void SetupDummies(Vector3 center, StringBuilder log)
        {
            foreach (var dummy in Object.FindObjectsOfType<trainingDummy>(true))
            {
                foreach (var col in dummy.GetComponentsInChildren<Collider>(true))
                {
                    if (!col.isTrigger && col.GetComponent<Hurtbox>() == null)
                        Undo.AddComponent<Hurtbox>(col.gameObject);
                }

                Vector3 offset = dummy.transform.position - center;
                offset.y = 0f;
                if (offset.magnitude < ArenaRadius + 1f && dummy.gameObject.activeSelf)
                {
                    Undo.RecordObject(dummy.gameObject, "Hide dummy in arena");
                    dummy.gameObject.SetActive(false);
                    log.AppendLine($"• Training dummy '{dummy.name}' was inside the duel ring and has been deactivated (re-enable any time).");
                }
                else
                {
                    log.AppendLine($"• Training dummy '{dummy.name}': now also registers swept sword hits.");
                }
            }
        }

        // ---- materials ---------------------------------------------------------------------------

        private static DuelArenaBuilder.Palette CreatePalette()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/SamuraiDuel", "Materials");

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var palette = new DuelArenaBuilder.Palette
            {
                armor = GetMaterial("Samurai_Armor", new Color(0.55f, 0.07f, 0.07f), 0.2f, 0.55f, Color.black),
                cloth = GetMaterial("Samurai_Cloth", new Color(0.09f, 0.1f, 0.18f), 0f, 0.2f, Color.black),
                lacquer = GetMaterial("Samurai_Lacquer", new Color(0.05f, 0.05f, 0.05f), 0.1f, 0.7f, Color.black),
                gold = GetMaterial("Samurai_Gold", new Color(0.85f, 0.65f, 0.2f), 0.9f, 0.6f, Color.black),
                steel = GetMaterial("Samurai_Steel", new Color(0.78f, 0.8f, 0.84f), 0.95f, 0.85f, Color.black),
                flash = GetMaterial("Samurai_HitFlash", Color.white, 0f, 0f, new Color(1f, 1f, 1f)),
                telegraph = GetMaterial("Samurai_Telegraph", new Color(1f, 0.2f, 0.1f), 0.5f, 0.8f, new Color(2.5f, 0.3f, 0.1f)),
                plate = GetMaterial("Duel_StartPlate", new Color(0.7f, 0.08f, 0.08f), 0f, 0.4f, new Color(0.6f, 0.05f, 0.05f)),
                font = font
            };
            AssetDatabase.SaveAssets();
            return palette;
        }

        internal static Material GetMaterial(string name, Color color, float metallic, float smoothness, Color emission,
            Texture baseMap = null, Texture emissionMap = null, string folder = MaterialFolder)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            if (baseMap != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", baseMap);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", baseMap);
            }

            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                if (emissionMap != null && material.HasProperty("_EmissionMap"))
                    material.SetTexture("_EmissionMap", emissionMap);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
