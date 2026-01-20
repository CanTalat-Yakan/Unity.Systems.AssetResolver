#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    public static class AssetResolverEditor
    {
        /// <summary>
        /// Editor-friendly prefab instantiation with runtime-parity loading:
        /// - Loads prefab via runtime resolver (Addressables -> Resources).
        /// - Instantiates via PrefabUtility for proper prefab instance + undo.
        /// - Editor-only fallback: AssetDatabase search by name.
        /// </summary>
        /// <param name="keyOrPath">Addressables key/address/label, Resources path, or prefab name (AssetDatabase fallback).</param>
        /// <param name="name">Optional instance name override.</param>
        /// <param name="parent">Optional parent Transform. If null, Selection.activeGameObject is used when present.</param>
        /// <param name="tryResources">If true, falls back to Resources.Load when Addressables doesn't resolve.</param>
        public static GameObject InstantiatePrefab(
            string keyOrPath,
            string name = null,
            Transform parent = null,
            bool tryResources = true)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
            {
                Debug.LogError("AssetResolverEditor: keyOrPath is null or empty.");
                return null;
            }

            var instance = TryInstantiateViaRuntimeResolver(keyOrPath, name, parent, tryResources);
            if (instance != null)
                return instance;

            // Editor-only convenience fallback (e.g. when a user passes a prefab name).
            instance = TryInstantiateViaAssetDatabase(keyOrPath, name, parent);
            if (instance != null)
                return instance;

            Debug.LogError($"{keyOrPath} prefab not found via runtime resolver (Addressables/Resources) or AssetDatabase.");
            return null;
        }

        private static GameObject TryInstantiateViaRuntimeResolver(
            string keyOrPath,
            string instantiatedName,
            Transform parent,
            bool tryResources)
        {
            var prefab = AssetResolver.TryGet<GameObject>(keyOrPath, false, tryResources);
            if (prefab == null)
                return null;

            return InstantiateIntoScene(prefab, instantiatedName, parent);
        }

        private static GameObject TryInstantiateViaAssetDatabase(string prefabName, string name, Transform parent)
        {
            var guids = AssetDatabase.FindAssets(prefabName + " t:Prefab");
            if (guids == null || guids.Length == 0)
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"AssetResolverEditor: Failed to load prefab at: {path}");
                return null;
            }

            return InstantiateIntoScene(prefab, name, parent);
        }

        private static GameObject InstantiateIntoScene(GameObject prefab, string name, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            var parentGo = parent != null ? parent.gameObject : null;
            if (parentGo == null && Selection.activeGameObject != null)
                parentGo = Selection.activeGameObject;

            if (parentGo != null)
                GameObjectUtility.SetParentAndAlign(instance, parentGo);

            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            Undo.RegisterCreatedObjectUndo(instance, "Spawn " + instance.name);
            Selection.activeObject = instance;


            return instance;
        }
    }
}
#endif