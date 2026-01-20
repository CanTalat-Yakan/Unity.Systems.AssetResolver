using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace UnityEssentials
{
    public static class AssetResolver
    {
        private static readonly Dictionary<string, Object> s_cachedAssets = new();
        private static readonly Dictionary<string, AsyncOperationHandle> s_asyncOperationHandles = new();
        private static readonly Dictionary<string, Task<Object>> s_activePreloads = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnDomainReload()
        {
            // Domain reload / assembly reload invalidates operation handles.
            // Clearing here prevents later access to stale handles throwing
            // "Attempting to use an invalid operation handle".
            s_asyncOperationHandles.Clear();
            s_activePreloads.Clear();
            s_cachedAssets.Clear();
        }

        /// <summary>
        /// Blocking, deterministic API:
        /// - Returns cached asset if available.
        /// - Otherwise loads synchronously (Addressables WaitForCompletion, then Resources fallback).
        /// - Optional: kicks off background preload when cacheResource=true and load succeeds.
        /// </summary>
        public static T TryGet<T>(string keyOrPath, bool cache = false, bool tryResources = true)
            where T : Object
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
            {
                Debug.LogWarning("ResourceLoader: keyOrPath is null or empty.");
                return null;
            }

            if (s_cachedAssets.TryGetValue(keyOrPath, out var cachedObject))
            {
                if (cachedObject is T typedObject)
                    return typedObject;

                Debug.LogWarning($"ResourceLoader: Cached resource at '{keyOrPath}' is not of type {typeof(T).Name}.");
                return null;
            }

            var addressable = TryLoadAddressables<T>(keyOrPath, cache);
            if (addressable != null)
                return addressable;

            if (!tryResources)
                return null;

            var resource = Resources.Load<T>(keyOrPath);
            if (resource == null)
            {
                Debug.LogWarning(
                    $"ResourceLoader: Could not find resource '{keyOrPath}' via Addressables or Resources.");
                return null;
            }

            if (cache)
                s_cachedAssets[keyOrPath] = resource;

            return resource;
        }

        /// <summary>
        /// Blocking, deterministic API:
        /// - Instantiates immediately once prefab is available.
        /// - Addressables first (blocking), then Resources fallback.
        /// </summary>
        public static GameObject InstantiatePrefab(string keyOrPath, string name = null, Transform parent = null,
            bool tryResourcesFallback = true)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
            {
                Debug.LogWarning("ResourceLoader: keyOrPath is null or empty.");
                return null;
            }

            // If cached prefab exists, use it.
            if (s_cachedAssets.TryGetValue(keyOrPath, out var cached) && cached is GameObject cachedPrefab)
            {
                var cachedInstance = Object.Instantiate(cachedPrefab, parent);
                if (!string.IsNullOrEmpty(name))
                    cachedInstance.name = name;
                return cachedInstance;
            }

            var instance = TryInstantiateAddressables(keyOrPath, name, parent);
            if (instance != null)
                return instance;

            if (!tryResourcesFallback)
                return null;

            var prefab = Resources.Load<GameObject>(keyOrPath);
            if (prefab == null)
            {
                Debug.LogWarning($"ResourceLoader: Could not find prefab '{keyOrPath}' via Addressables or Resources.");
                return null;
            }

            var resourcesInstance = Object.Instantiate(prefab, parent);
            if (!string.IsNullOrEmpty(name))
                resourcesInstance.name = name;

            return resourcesInstance;
        }

        /// <summary>
        /// Async performance hook:
        /// Preloads into cache in the background to reduce later stalls from TryGet/InstantiatePrefab.
        /// Safe to call repeatedly; de-duped by key.
        /// </summary>
        public static void Preload<T>(string keyOrPath, bool tryResources = true) where T : Object
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return;

            if (s_cachedAssets.ContainsKey(keyOrPath))
                return;

            if (s_activePreloads.TryGetValue(keyOrPath, out var existing) && !existing.IsCompleted)
                return;

            s_activePreloads[keyOrPath] = LoadIntoCacheAsync<T>(keyOrPath, tryResources);
        }

        public static bool IsLoaded(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return false;

            return s_cachedAssets.ContainsKey(keyOrPath);
        }

        public static bool IsLoading(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return false;

            return s_activePreloads.TryGetValue(keyOrPath, out var t) && !t.IsCompleted;
        }

        private static async Task<Object> LoadIntoCacheAsync<T>(string keyOrPath, bool tryResources)
            where T : Object
        {
            try
            {
                // Addressables first (true async)
                var addressable = await TryLoadAddressablesAsync<T>(keyOrPath);
                if (addressable != null)
                {
                    s_cachedAssets[keyOrPath] = addressable;
                    return addressable;
                }

                if (!tryResources)
                    return null;

                // Resources fallback (sync)
                var res = Resources.Load<T>(keyOrPath);
                if (res != null)
                {
                    s_cachedAssets[keyOrPath] = res;
                    return res;
                }

                return null;
            }
            finally
            {
                s_activePreloads.Remove(keyOrPath);
            }
        }

        private static bool TryLoadAssetHandle<T>(string keyOrAddress, out AsyncOperationHandle<T> handle) where T : Object
        {
            try
            {
                handle = Addressables.LoadAssetAsync<T>(keyOrAddress);
                return true;
            }
            catch
            {
                handle = default;
                return false;
            }
        }

        private static bool TryInstantiateHandle(string keyOrAddress, Transform parent, out AsyncOperationHandle<GameObject> handle)
        {
            try
            {
                handle = Addressables.InstantiateAsync(keyOrAddress, parent);
                return true;
            }
            catch
            {
                handle = default;
                return false;
            }
        }

        private static T TryLoadAddressables<T>(string keyOrAddress, bool cache) where T : Object
        {
            // If we already have a retained handle, reuse it.
            if (s_asyncOperationHandles.TryGetValue(keyOrAddress, out var existingHandle))
            {
                if (TryGetValidResult(existingHandle, out T typedExisting))
                {
                    if (cache)
                        s_cachedAssets[keyOrAddress] = typedExisting;

                    return typedExisting;
                }

                s_asyncOperationHandles.Remove(keyOrAddress);
                SafeRelease(existingHandle);
            }

            if (!TryLoadAssetHandle<T>(keyOrAddress, out var handle))
                return null;

            try
            {
                handle.WaitForCompletion();
            }
            catch
            {
                SafeRelease(handle);
                return null;
            }

            if (!TryGetValidResult<T>(handle, out var result) || result == null)
            {
                SafeRelease(handle);
                return null;
            }

            if (cache)
            {
                s_cachedAssets[keyOrAddress] = result;
                // Retain handle so Release/ClearCache can free it later.
                s_asyncOperationHandles[keyOrAddress] = handle;
            }
            else
            {
                SafeRelease(handle);
            }

            return result;
        }

        private static async Task<T> TryLoadAddressablesAsync<T>(string keyOrAddress) where T : Object
        {
            // If we already have a retained handle, reuse it.
            if (s_asyncOperationHandles.TryGetValue(keyOrAddress, out var existingHandle))
            {
                if (TryGetValidResult(existingHandle, out T typedExisting))
                    return typedExisting;

                s_asyncOperationHandles.Remove(keyOrAddress);
                SafeRelease(existingHandle);
            }

            if (!TryLoadAssetHandle<T>(keyOrAddress, out var handle))
                return null;

            try
            {
                await handle.Task;
            }
            catch
            {
                SafeRelease(handle);
                return null;
            }

            if (!TryGetValidResult<T>(handle, out var result) || result == null)
            {
                SafeRelease(handle);
                return null;
            }

            // Retain handle so ClearCache/Release can free it later.
            s_asyncOperationHandles[keyOrAddress] = handle;
            return result;
        }

        private static GameObject TryInstantiateAddressables(string keyOrAddress, string name, Transform parent)
        {
            if (!TryInstantiateHandle(keyOrAddress, parent, out var handle))
                return null;

            try
            {
                handle.WaitForCompletion();
            }
            catch
            {
                SafeRelease(handle);
                return null;
            }

            // Note: Result getter can throw if handle got invalidated.
            if (!TryGetValidResult(handle, out GameObject instance) || instance == null)
            {
                SafeRelease(handle);
                return null;
            }

            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            // Do NOT release the handle; releasing would destroy the instance.
            // Caller should later call Addressables.ReleaseInstance(instance) when done.
            return instance;
        }

        public static void Release(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return;

            s_cachedAssets.Remove(keyOrPath);
            s_activePreloads.Remove(keyOrPath);

            if (s_asyncOperationHandles.TryGetValue(keyOrPath, out var handle))
            {
                s_asyncOperationHandles.Remove(keyOrPath);
                SafeRelease(handle);
            }
        }

        internal static void ClearCache()
        {
            foreach (var kvp in s_asyncOperationHandles)
                SafeRelease(kvp.Value);

            s_asyncOperationHandles.Clear();
            s_activePreloads.Clear();
            s_cachedAssets.Clear();
        }
        
        private static void SafeRelease(AsyncOperationHandle handle)
        {
            if (!handle.IsValid()) return;
            // Swallow: Release can throw if op is already invalid/released.
            try { Addressables.Release(handle); } 
            catch { }
        }

        private static bool TryGetValidResult<T>(AsyncOperationHandle handle, out T result) where T : class
        {
            result = null;

            if (!handle.IsValid())
                return false;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                return false;

            try
            {
                result = handle.Result as T;
                return result != null;
            }
            catch
            {
                // Accessing Result can throw if the underlying internal op was released.
                result = null;
                return false;
            }
        }

    }
}