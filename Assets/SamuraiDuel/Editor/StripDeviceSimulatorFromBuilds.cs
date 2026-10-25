using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace SamuraiDuel.EditorTools
{
    /// <summary>
    /// The XR Device Simulator removes every non-simulated HMD at startup (RemoveOtherHMDDevices),
    /// so if it ships in a Quest build head tracking stops working. SampleScene keeps a simulator
    /// for editor testing; this strips it from scenes while a player build is processed.
    /// Play mode in the editor is unaffected.
    /// </summary>
    internal sealed class StripDeviceSimulatorFromBuilds : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !BuildPipeline.isBuildingPlayer)
                return; // entering play mode in the editor

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var simulator in root.GetComponentsInChildren<XRDeviceSimulator>(true))
                {
                    Debug.Log($"[Samurai Duel] Stripping XR Device Simulator '{simulator.name}' from build of {scene.name}.");
                    Object.DestroyImmediate(simulator.gameObject);
                }
            }
        }
    }
}
