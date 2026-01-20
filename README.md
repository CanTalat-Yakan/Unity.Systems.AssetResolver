# Unity Essentials

This module is part of the Unity Essentials ecosystem and follows the same lightweight, editor-first approach.
Unity Essentials is a lightweight, modular set of editor utilities and helpers that streamline Unity development. It focuses on clean, dependency-free tools that work well together.

All utilities are under the `UnityEssentials` namespace.

```csharp
using UnityEssentials;
```

## Installation

Install the Unity Essentials entry package via Unity's Package Manager, then install modules from the Tools menu.

- Add the entry package (via Git URL)
    - Window → Package Manager
    - "+" → "Add package from git URL…"
    - Paste: `https://github.com/CanTalat-Yakan/UnityEssentials.git`

- Install or update Unity Essentials packages
    - Tools → Install & Update UnityEssentials
    - Install all or select individual modules; run again anytime to update

---

# Asset Resolver

> Quick overview: Tiny, deterministic asset lookup and prefab spawning helper that tries Addressables first, then Resources, with optional caching. Includes an editor helper that spawns prefabs via `PrefabUtility` + Undo.

`AssetResolver` exists to make asset access consistent across your project:
- One call site (`AssetResolver.TryGet<T>(...)`) instead of scattered Addressables/Resources code
- An opt-in cache to avoid repeat loads and reduce stalls
- Safe handling of Addressables handles (including domain reload invalidation)

## Features
- Runtime asset lookup
  - `AssetResolver.TryGet<T>(keyOrPath)` returns `null` on failure
  - Tries **Addressables** first, then optional **Resources** fallback
- Runtime prefab instantiation
  - `AssetResolver.InstantiatePrefab(keyOrPath, name, parent)`
  - Intended for play mode / builds
- Optional caching
  - Cache loaded assets by key/path
  - Background preload helper (`Preload<T>`) to reduce later stalls
- Editor prefab spawner helper
  - `AssetResolverEditor.InstantiatePrefab(keyOrPath, name, parent)`
  - Loads via runtime resolver for parity, instantiates via `PrefabUtility` for proper prefab connections + Undo
  - Editor-only convenience fallback to `AssetDatabase` search by name

## Requirements
- Unity 6000.0+
- Addressables package (for Addressables resolving)
- Optional: Resources folder usage for fallback

## Usage

### Load an asset (runtime)
```csharp
// Addressables key/address/label OR Resources path
var shader = AssetResolver.TryGet<ComputeShader>("UnityEssentials_Shader_CameraLuminance");
if (!shader)
{
    // handle missing
}
```

### Preload into cache (runtime)
```csharp
// Starts a background Addressables load (then Resources fallback) and stores into cache when done.
AssetResolver.Preload<GameObject>("UnityEssentials_Prefab_SplashScreen");
```

### Instantiate a prefab (runtime)
```csharp
var instance = AssetResolver.InstantiatePrefab(
    keyOrPath: "UnityEssentials_Prefab_SplashScreen",
    name: "SplashScreen",
    parent: null);
```

### Instantiate a prefab (editor)
```csharp
#if UNITY_EDITOR
using UnityEssentials;
using UnityEngine;

// Spawns into the open scene, registers Undo, and selects the created instance.
// Resolution: Addressables -> Resources -> AssetDatabase (name search).
AssetResolverEditor.InstantiatePrefab(
    keyOrPath: "UnityEssentials_Prefab_SplashScreen",
    name: "SplashScreen",
    parent: null);
#endif
```

## How It Works
- Resolution order
  - `TryGet<T>`: Addressables → (optional) Resources
  - Editor spawner: runtime resolver first (Addressables/Resources) → AssetDatabase fallback
- Addressables safety
  - Operation handles can become invalid after an assembly / domain reload.
  - The resolver clears retained handles on domain reload to avoid "Attempting to use an invalid operation handle".
- Caching
  - If `cache=true`, the loaded asset is stored in-memory and the Addressables handle is retained
  - Use `Release(keyOrPath)` or `ClearCache()` (internal) to free retained handles

## Notes and Limitations
- `TryGet<T>` is intentionally a *Try* API: it returns `null` on failure.
- If you instantiate via Addressables in runtime (`InstantiatePrefab`), the instance should later be released via Addressables (depending on your lifecycle); the resolver does not automatically release instance handles.
- AssetDatabase fallback is editor-only and may pick the first matching prefab if multiple share the same name.

## Files in This Package
- Runtime
  - `Runtime/AssetResolver.cs` – Addressables/Resources resolving, caching, preload, release
- Editor
  - `Editor/AssetResolverEditor.cs` – Prefab spawning helper (undo/selection) using runtime resolver

## Tags
unity, addressables, resources, asset loading, caching, prefab, editor utilities
