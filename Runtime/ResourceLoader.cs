using System.Collections.Generic;
using UnityEngine;

#if UNITY_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace UnityEssentials
{
    public static class ResourceLoader
    {
        private static readonly Dictionary<string, Object> _resourceCache = new();

#if UNITY_ADDRESSABLES
        // Keep handles so we can release Addressables assets on ClearCache().
        private static readonly Dictionary<string, AsyncOperationHandle> _addressablesHandles = new();
#endif

        public static T LoadResource<T>(string resourcePath, bool cacheResource = true) where T : Object
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                Debug.LogWarning("ResourceLoader: resourcePath is null or empty.");
                return null;
            }

            if (_resourceCache.TryGetValue(resourcePath, out var cachedObject))
            {
                if (cachedObject is T typedObject)
                    return typedObject;
                Debug.LogWarning($"ResourceLoader: Cached resource at '{resourcePath}' is not of type {typeof(T).Name}.");
            }

            var resource = Resources.Load<T>(resourcePath);
            if (resource == null)
            {
                Debug.LogWarning($"ResourceLoader: Could not find resource '{resourcePath}' in any Resources folder.");
                return null;
            }

            if (cacheResource)
                _resourceCache[resourcePath] = resource;

            return resource;
        }

        /// <summary>
        /// Loads an asset via Addressables (if available) and falls back to Resources.
        /// For Addressables this uses LoadAssetAsync + WaitForCompletion (sync). If you need async, we can add it.
        /// </summary>
        public static T LoadResource<T>(
            string resourcePath,
            bool cacheResource,
            bool tryAddressablesFirst,
            bool tryResourcesFallback) where T : Object
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                Debug.LogWarning("ResourceLoader: resourcePath is null or empty.");
                return null;
            }

            if (_resourceCache.TryGetValue(resourcePath, out var cachedObject))
            {
                if (cachedObject is T typedObject)
                    return typedObject;

                Debug.LogWarning($"ResourceLoader: Cached resource at '{resourcePath}' is not of type {typeof(T).Name}.");
            }

#if UNITY_ADDRESSABLES
            if (tryAddressablesFirst)
            {
                var addressable = TryLoadAddressables<T>(resourcePath, cacheResource);
                if (addressable != null)
                    return addressable;
            }
#endif

            if (!tryResourcesFallback)
                return null;

            var resource = Resources.Load<T>(resourcePath);
            if (resource == null)
            {
                Debug.LogWarning($"ResourceLoader: Could not find resource '{resourcePath}' via Addressables or Resources.");
                return null;
            }

            if (cacheResource)
                _resourceCache[resourcePath] = resource;

            return resource;
        }

        public static GameObject InstantiatePrefab(string prefabName, string instantiatedName, Transform parent = null)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                Debug.LogWarning("PrefabSpawner: prefabName is null or empty.");
                return null;
            }

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"PrefabSpawner: Could not find prefab '{prefabName}' in any Resources folder.");
                return null;
            }

            var instance = Object.Instantiate(prefab, parent);
            if (!string.IsNullOrEmpty(instantiatedName))
                instance.name = instantiatedName;

            return instance;
        }

        public static GameObject InstantiatePrefab(
            string prefabName,
            string instantiatedName,
            Transform parent,
            bool tryAddressablesFirst,
            bool tryResourcesFallback)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                Debug.LogWarning("PrefabSpawner: prefabName is null or empty.");
                return null;
            }

#if UNITY_ADDRESSABLES
            if (tryAddressablesFirst)
            {
                var instance = TryInstantiateAddressables(prefabName, instantiatedName, parent);
                if (instance != null)
                    return instance;
            }
#endif

            if (!tryResourcesFallback)
                return null;

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"PrefabSpawner: Could not find prefab '{prefabName}' via Addressables or Resources.");
                return null;
            }

            var resourcesInstance = Object.Instantiate(prefab, parent);
            if (!string.IsNullOrEmpty(instantiatedName))
                resourcesInstance.name = instantiatedName;

            return resourcesInstance;
        }

#if UNITY_ADDRESSABLES
        private static T TryLoadAddressables<T>(string keyOrAddress, bool cacheResource) where T : Object
        {
            AsyncOperationHandle<T> handle;
            try { handle = Addressables.LoadAssetAsync<T>(keyOrAddress); } catch { return null; }

            // Can throw if Addressables isn't initialized properly.
            try { handle.WaitForCompletion(); } catch { }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Addressables.Release(handle);
                return null;
            }

            if (cacheResource)
            {
                _resourceCache[keyOrAddress] = handle.Result;
                // Keep handle so the asset can be released later.
                _addressablesHandles[keyOrAddress] = handle;
            }
            else
            {
                // If we're not caching, release immediately.
                Addressables.Release(handle);
            }

            return handle.Result;
        }

        private static GameObject TryInstantiateAddressables(string keyOrAddress, string instantiatedName, Transform parent)
        {
            AsyncOperationHandle<GameObject> handle;
            try { handle = Addressables.LoadAssetAsync<GameObject>(keyOrAddress); } catch { return null; }

            try { handle.WaitForCompletion(); } catch { }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Addressables.Release(handle);
                return null;
            }

            var instance = Object.Instantiate(handle.Result, parent);
            if (!string.IsNullOrEmpty(instantiatedName))
                instance.name = instantiatedName;

            // We only loaded the prefab asset to instantiate it. Release the loaded prefab reference now.
            Addressables.Release(handle);

            return instance;
        }
#endif

        public static void ClearCache()
        {
#if UNITY_ADDRESSABLES
            foreach (var kvp in _addressablesHandles)
                // Ignore release errors (e.g., already released).
                try { Addressables.Release(kvp.Value); } catch { }

            _addressablesHandles.Clear();
#endif

            _resourceCache.Clear();
        }
    }
}