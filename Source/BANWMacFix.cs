using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace BANWMacFix
{
    [StaticConstructorOnStartup]
    public static class MacFixStartup
    {
        static MacFixStartup()
        {
            BundleTracker.Init();
            new Harmony("local.banw.macfix").PatchAll(typeof(MacFixStartup).Assembly);
            BundleTracker.RegisterStartupBundles();
            var go = new GameObject("BANWMacFix_ShaderSweeper");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ShaderSweeper>();
        }
    }

    /// <summary>
    /// BlueArchive-NewWorld's asset bundles were built for Windows, so the shaders packed into them only contain
    /// DirectX code. Unity still reports many of them as "supported", so they are identified by origin: any shader
    /// used by an asset from one of the mod's bundles that is not one of the game's own shaders.
    /// </summary>
    public static class BundleTracker
    {
        public static readonly bool Active = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12;
        static readonly HashSet<int> banwBundles = new HashSet<int>();
        static string banwRoot = "";

        // Unity's "Always Included" shaders: bundles reference these instead of packing copies.
        static readonly HashSet<string> nativeNames = new HashSet<string>
        {
            "UI/Default", "UI/Default Font", "UI/DefaultETC1", "Sprites/Default", "Sprites/Mask", "Legacy Shaders/Diffuse", "Standard",
        };

        static ModContentPack BanwPack
        {
            get { return LoadedModManager.RunningModsListForReading.FirstOrDefault(p => p.PackageId == "archive.newworld"); }
        }

        /// <summary>Runs before any of the mod's UI is loaded: every shader in memory now belongs to the game.</summary>
        public static void Init()
        {
            var pack = BanwPack;
            if (pack != null)
                banwRoot = Normalize(pack.RootDir);
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s != null && !string.IsNullOrEmpty(s.name))
                    nativeNames.Add(s.name);
        }

        static string Normalize(string p)
        {
            return string.IsNullOrEmpty(p) ? "" : p.Replace('\\', '/').TrimEnd('/');
        }

        public static void RegisterStartupBundles()
        {
            var pack = BanwPack;
            if (pack == null || pack.assetBundles == null)
                return;
            foreach (var b in pack.assetBundles.loadedAssetBundles)
                AddBundle(b, b != null ? b.name : "");
        }

        public static bool IsBanwPath(string path)
        {
            return banwRoot.Length > 0 && Normalize(path).StartsWith(banwRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static void AddBundle(AssetBundle bundle, string label)
        {
            if (bundle == null)
                return;
            string l = (label ?? "").ToLowerInvariant();
            if (l.Contains("_mac") || l.Contains("osx") || l.Contains("metal"))
                return;
            banwBundles.Add(bundle.GetInstanceID());
        }

        public static bool IsBanwBundle(AssetBundle bundle)
        {
            return bundle != null && banwBundles.Contains(bundle.GetInstanceID());
        }

        static void NoteShader(Shader sh)
        {
            if (sh == null || nativeNames.Contains(sh.name ?? "") || (sh.name ?? "").StartsWith("Hidden/"))
                return;
            if (ShaderFixer.BrokenShaders.Add(sh.GetInstanceID()))
                ShaderFixer.Invalidate();
        }

        /// <summary>Records the non-native shaders used by an asset loaded from one of the mod's bundles.</summary>
        public static void NoteAsset(UnityEngine.Object obj)
        {
            if (!Active || obj == null)
                return;
            var mat = obj as Material;
            if (mat != null)
            {
                NoteShader(mat.shader);
                return;
            }
            var go = obj as GameObject;
            if (go == null)
                return;
            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g.material != null)
                    NoteShader(g.material.shader);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null)
                        NoteShader(m.shader);
        }
    }

    /// <summary>Replaces shaders the current GPU cannot run with the game's own UI/sprite shaders.</summary>
    public static class ShaderFixer
    {
        class MatInfo
        {
            public Material Mat;
            public string OriginalName;
            public int OriginalQueue;
            public bool Ui;
            public bool Additive;
        }

        public static readonly HashSet<int> BrokenShaders = new HashSet<int>();
        static readonly Dictionary<int, MatInfo> tracked = new Dictionary<int, MatInfo>();
        static readonly HashSet<int> checkedMaterials = new HashSet<int>();
        static readonly HashSet<int> pending = new HashSet<int>();
        static readonly Dictionary<string, Shader> workingByName = new Dictionary<string, Shader>();
        static readonly HashSet<string> logged = new HashSet<string>();
        public static bool SweepRequested;

        public static void Invalidate()
        {
            checkedMaterials.Clear();
            SweepRequested = true;
        }

        static bool IsBroken(Shader s)
        {
            if (s == null || (s.name ?? "").StartsWith("Hidden/"))
                return false;
            return !s.isSupported || BrokenShaders.Contains(s.GetInstanceID());
        }

        static Shader FindWorking(string name)
        {
            Shader cached;
            if (workingByName.TryGetValue(name, out cached) && cached != null)
                return cached;
            var found = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s != null && s.name == name && !IsBroken(s));
            if (found == null)
            {
                // Game shaders that have not been used yet are not in memory until requested.
                found = Shader.Find(name);
                if (found != null && IsBroken(found))
                    found = null;
            }
            if (found != null)
                workingByName[name] = found;
            return found;
        }

        static string PickReplacement(MatInfo info)
        {
            if (info.Ui || info.OriginalName.StartsWith("UI/") || info.OriginalName.Contains("SkeletonGraphic"))
                return "UI/Default";
            return info.Additive ? "Custom/Unlit additive" : "Sprites/Default";
        }

        static bool DetectAdditive(Material m, Shader sh)
        {
            if (m.HasProperty("_DstBlend") && Mathf.Approximately(m.GetFloat("_DstBlend"), (float)BlendMode.One))
                return true;
            string n = ((sh.name ?? "") + " " + (m.name ?? "")).ToLowerInvariant();
            return n.Contains("additive") || n.Contains("glow");
        }

        static void Apply(MatInfo info)
        {
            var target = FindWorking(PickReplacement(info));
            int id = info.Mat.GetInstanceID();
            if (target == null)
            {
                pending.Add(id);
                return;
            }
            pending.Remove(id);
            if (info.Mat.shader == target)
                return;
            info.Mat.shader = target;
            // An unsupported shader reports the error shader's opaque queue (2000). Keeping that makes Unity
            // sort UI batches front-to-back by distance instead of hierarchy order, so characters vanish behind
            // backgrounds. Only keep queues that were genuinely set for transparent ordering.
            info.Mat.renderQueue = info.OriginalQueue >= 2450 ? info.OriginalQueue : -1;
            string key = info.OriginalName + " -> " + target.name;
            if (logged.Add(key))
                Log.Message("[BANW Mac Fix] Using " + key);
        }

        public static void FixMaterial(Material m, bool ui)
        {
            if (m == null)
                return;
            MatInfo info;
            if (tracked.TryGetValue(m.GetInstanceID(), out info))
            {
                if (ui && !info.Ui)
                {
                    info.Ui = true;
                    Apply(info);
                }
                return;
            }
            var sh = m.shader;
            if (!IsBroken(sh))
                return;
            info = new MatInfo
            {
                Mat = m,
                OriginalName = sh.name ?? "",
                OriginalQueue = sh.isSupported ? m.renderQueue : -1,
                Ui = ui,
                Additive = DetectAdditive(m, sh),
            };
            tracked[m.GetInstanceID()] = info;
            Apply(info);
        }

        public static void FixObject(UnityEngine.Object obj)
        {
            var mat = obj as Material;
            if (mat != null)
            {
                FixMaterial(mat, false);
                return;
            }
            var go = obj as GameObject;
            if (go == null)
                return;
            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                FixMaterial(g.material, true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    FixMaterial(m, false);
        }

        /// <summary>Catches materials created or loaded by paths the patches do not see.</summary>
        public static void Sweep()
        {
            SweepRequested = false;
            foreach (var id in pending.ToList())
            {
                MatInfo info;
                if (tracked.TryGetValue(id, out info) && info.Mat != null)
                    Apply(info);
                else
                    pending.Remove(id);
            }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && checkedMaterials.Add(m.GetInstanceID()))
                    FixMaterial(m, false);
        }
    }

    public class ShaderSweeper : MonoBehaviour
    {
        const float Interval = 3f;
        float nextSweep;

        void Update()
        {
            if (!ShaderFixer.SweepRequested && Time.realtimeSinceStartup < nextSweep)
                return;
            nextSweep = Time.realtimeSinceStartup + Interval;
            try
            {
                ShaderFixer.Sweep();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BANW Mac Fix] Sweep failed: " + e, 0x5A3C1);
            }
        }
    }

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadAsset), new[] { typeof(string), typeof(Type) })]
    static class Patch_AssetBundle_LoadAsset
    {
        static void Postfix(AssetBundle __instance, UnityEngine.Object __result)
        {
            if (__result == null)
                return;
            if (BundleTracker.IsBanwBundle(__instance))
                BundleTracker.NoteAsset(__result);
            ShaderFixer.FixObject(__result);
        }
    }

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile), new[] { typeof(string) })]
    static class Patch_AssetBundle_LoadFromFile
    {
        static void Postfix(string path, AssetBundle __result)
        {
            if (__result != null && BundleTracker.IsBanwPath(path))
                BundleTracker.AddBundle(__result, path);
            ShaderFixer.SweepRequested = true;
        }
    }

    /// <summary>
    /// Some characters' skeletons lack animations the UI scripts request (e.g. "Eye_Close"), which throws.
    /// A detached TrackEntry leaves the character exactly as it was, without the exception.
    /// </summary>
    static class SpineAnimGuard
    {
        public static bool Missing(Spine.AnimationState state, string animationName)
        {
            var data = state != null && state.Data != null ? state.Data.SkeletonData : null;
            return data != null && animationName != null && data.FindAnimation(animationName) == null;
        }
    }

    [HarmonyPatch(typeof(Spine.AnimationState), nameof(Spine.AnimationState.SetAnimation), new[] { typeof(int), typeof(string), typeof(bool) })]
    static class Patch_SetAnimation
    {
        static bool Prefix(Spine.AnimationState __instance, string animationName, ref Spine.TrackEntry __result)
        {
            if (!SpineAnimGuard.Missing(__instance, animationName))
                return true;
            __result = new Spine.TrackEntry();
            return false;
        }
    }

    [HarmonyPatch(typeof(Spine.AnimationState), nameof(Spine.AnimationState.AddAnimation), new[] { typeof(int), typeof(string), typeof(bool), typeof(float) })]
    static class Patch_AddAnimation
    {
        static bool Prefix(Spine.AnimationState __instance, string animationName, ref Spine.TrackEntry __result)
        {
            if (!SpineAnimGuard.Missing(__instance, animationName))
                return true;
            __result = new Spine.TrackEntry();
            return false;
        }
    }
}
