#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

#if UNITY_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace UnityEssentials
{
    public static class ResourceLoaderEditor
    {
        /// <summary>
        /// Instantiates a prefab into the current scene.
        /// Resolution order (when supported): Addressables key/path -> AssetDatabase prefab search -> Resources.
        /// </summary>
        /// <param name="prefabName">
        /// Addressables key / label / address, or prefab name to search in AssetDatabase, or Resources path.
        /// </param>
        /// <param name="instantiatedName">Optional name override for the instantiated GameObject.</param>
        /// <param name="tryAddressablesFirst">If true, tries Addressables first (when package is available).</param>
        /// <param name="tryResourcesFallback">If true, tries Resources.Load fallback if AssetDatabase search fails.</param>
        public static GameObject InstantiatePrefab(
            string prefabName,
            string instantiatedName = null,
            bool tryAddressablesFirst = true,
            bool tryResourcesFallback = true)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                Debug.LogError("ResourceLoaderEditor: prefabName is null or empty.");
                return null;
            }

#if UNITY_ADDRESSABLES
            if (tryAddressablesFirst)
            {
                var instance = TryInstantiateAddressables(prefabName, instantiatedName);
                if (instance != null)
                    return instance;
            }
#endif

            // --- AssetDatabase (Editor) fallback ---
            var guids = AssetDatabase.FindAssets(prefabName + " t:Prefab");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError($"Failed to load prefab at: {path}");
                    return null;
                }

                return InstantiateIntoScene(prefab, instantiatedName);
            }

            // --- Resources fallback ---
            if (tryResourcesFallback)
            {
                var resourcesPrefab = Resources.Load<GameObject>(prefabName);
                if (resourcesPrefab != null)
                    return InstantiateIntoScene(resourcesPrefab, instantiatedName);
            }

            Debug.LogError($"{prefabName} prefab not found via Addressables, AssetDatabase, or Resources.");
            return null;
        }

        private static GameObject InstantiateIntoScene(GameObject prefab, string instantiatedName)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (Selection.activeGameObject is GameObject parent)
                GameObjectUtility.SetParentAndAlign(instance, parent);

            if (!string.IsNullOrEmpty(instantiatedName))
                instance.name = instantiatedName;

            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            Undo.RegisterCreatedObjectUndo(instance, "Spawn " + instance.name);
            Selection.activeObject = instance;

            return instance;
        }

#if UNITY_ADDRESSABLES
        private static GameObject TryInstantiateAddressables(string keyOrAddress, string instantiatedName)
        {
            // In editor tools we want to stay synchronous; Task-based patterns can break menu items.
            // So we block on completion, but keep it wrapped so we can extend later.
            AsyncOperationHandle<GameObject> handle;
            try { handle = Addressables.LoadAssetAsync<GameObject>(keyOrAddress); } catch { return null; }

            // WaitForCompletion can throw if Addressables isn't initialized properly.
            try { handle.WaitForCompletion(); } catch { }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                // We couldn't resolve the address via Addressables. Clean up and let the caller fallback.
                Addressables.Release(handle);
                return null;
            }

            var prefab = handle.Result;
            var instance = InstantiateIntoScene(prefab, instantiatedName);

            // Release the loaded asset reference; the instantiated scene object remains.
            Addressables.Release(handle);

            return instance;
        }
#endif
    }
}
#endif