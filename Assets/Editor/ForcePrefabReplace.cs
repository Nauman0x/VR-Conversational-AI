using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility to force-replace a prefab instance even when it contains
/// missing script components — something Unity normally blocks in interactive mode.
/// </summary>
public class ForcePrefabReplace
{
    [MenuItem("Tools/Force Replace Selected Prefab Instance")]
    static void ForceReplace()
    {
        // Grab whatever is selected in the Hierarchy panel
        GameObject selectedInstance = Selection.activeGameObject;

        if (selectedInstance == null)
        {
            Debug.LogError("[ForcePrefabReplace] No GameObject selected. " +
                           "Select the prefab instance in the Hierarchy first.");
            return;
        }

        // Let the user pick the replacement prefab asset from the project
        string absolutePath = EditorUtility.OpenFilePanel(
            "Select Replacement Prefab", "Assets", "prefab");

        if (string.IsNullOrEmpty(absolutePath))
        {
            Debug.Log("[ForcePrefabReplace] Cancelled — no prefab selected.");
            return;
        }

        // Unity asset paths must be relative to the project root (e.g. "Assets/...")
        string relativePath = "Assets" + absolutePath.Substring(Application.dataPath.Length);

        GameObject newPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(relativePath);

        if (newPrefabAsset == null)
        {
            Debug.LogError($"[ForcePrefabReplace] Could not load prefab at: {relativePath}. " +
                           "Make sure the file is inside your Assets folder.");
            return;
        }

        /*
         * STEP: Perform the forced replacement.
         *
         * InteractionMode.AutomatedAction bypasses the missing-script safety
         * check that InteractionMode.UserAction enforces. This is intentional —
         * we accept that any serialized data on the missing component will be lost.
         *
         * ObjectMatchMode.ByHierarchy attempts to re-map child objects by their
         * position in the hierarchy rather than by name, which gives the best
         * chance of preserving overrides on surviving components.
         */
        PrefabReplacingSettings settings = new PrefabReplacingSettings
        {
            logInfo = true,
            objectMatchMode = ObjectMatchMode.ByHierarchy
        };

        PrefabUtility.ReplacePrefabAssetOfPrefabInstances(
            new GameObject[] { selectedInstance },
            newPrefabAsset,
            settings,
            InteractionMode.AutomatedAction
        );

        Debug.Log($"[ForcePrefabReplace] Successfully replaced prefab on '{selectedInstance.name}' " +
                  $"with '{newPrefabAsset.name}'.");
    }

    /// <summary>
    /// Validates the menu item — greys it out when nothing is selected in the Hierarchy.
    /// </summary>
    [MenuItem("Tools/Force Replace Selected Prefab Instance", true)]
    static bool ValidateForceReplace()
    {
        return Selection.activeGameObject != null;
    }
}
