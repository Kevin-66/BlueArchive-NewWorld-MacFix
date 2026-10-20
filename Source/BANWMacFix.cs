using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace BANWMacFix
{
    /// <summary>
    /// Instantiated during LoadedModManager.CreateModClasses(), before any ModAssetBundlesHandler.ReloadAll()
    /// or [StaticConstructorOnStartup] constructors run. Installing Harmony patches here ensures startup bundles
    /// (such as banw_pawns_win and damagefontsystem.ab) are intercepted from the very beginning.
    /// Runs on RimWorld's loading thread, not Unity's main thread, so nothing here may call Unity APIs.
    /// </summary>
    public class MacFixMod : Mod
    {
        public MacFixMod(ModContentPack content) : base(content)
        {
            BundleTracker.ConfigureModRoot();
            var harmony = new Harmony("local.banw.macfix");
            harmony.PatchAll(typeof(MacFixMod).Assembly);
            DynamicPatches.Apply(harmony);
        }
    }

    [StaticConstructorOnStartup]
    public static class MacFixStartup
    {
        static MacFixStartup()
        {
            BundleTracker.CaptureNativeShaders();
            BundleTracker.RegisterStartupBundles();
            var go = new GameObject("BANWMacFix_ShaderSweeper");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ShaderSweeper>();
            ShaderFixer.RequestSweep();
        }
    }

    /// <summary>
    /// BlueArchive-NewWorld's asset bundles were built for Windows, so the shaders packed into them only contain
    /// DirectX code. Unity still reports many of them as "supported", so they are identified by origin: any shader
    /// used by an asset from one of the mod's bundles that is not one of the game's own shaders.
    /// </summary>
    public static class BundleTracker
    {
        static bool? active;

        /// <summary>
        /// Read lazily: MacFixMod's constructor runs on RimWorld's loading thread and touches this class, and
        /// querying SystemInfo off the Unity main thread aborts the game ("Graphics device is null").
        /// </summary>
        public static bool Active
        {
            get
            {
                if (active == null)
                    active = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11
                        && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12;
                return active.Value;
            }
        }

        static readonly HashSet<int> banwBundles = new HashSet<int>();
        static string banwRoot = "";
        static bool nativeCaptured;

        // Unity's "Always Included" shaders: bundles reference these instead of packing copies.
        static readonly HashSet<string> nativeNames = new HashSet<string>
        {
            "UI/Default", "UI/Default Font", "UI/DefaultETC1", "Sprites/Default", "Sprites/Mask", "Legacy Shaders/Diffuse", "Standard",
        };

        static ModContentPack BanwPack
        {
            get
            {
                return LoadedModManager.RunningModsListForReading.FirstOrDefault(
                    p => string.Equals(p.PackageId, "archive.newworld", StringComparison.OrdinalIgnoreCase));
            }
        }

        public static void ConfigureModRoot()
        {
            var pack = BanwPack;
            if (pack == null)
                return;
            banwRoot = Normalize(pack.RootDir);
            MaintainWorkshopFiles(pack.RootDir);
        }

        /// <summary>
        /// Must run on the Unity main thread before any mod AssetBundle is loaded so only genuine game shaders
        /// are recorded as native.
        /// </summary>
        public static void CaptureNativeShaders()
        {
            if (nativeCaptured)
                return;
            nativeCaptured = true;
            if (banwRoot.Length == 0)
            {
                var pack = BanwPack;
                if (pack != null)
                    banwRoot = Normalize(pack.RootDir);
            }
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s != null && !string.IsNullOrEmpty(s.name))
                    nativeNames.Add(s.name);
        }

        static void MaintainWorkshopFiles(string rootDir)
        {
            try
            {
                // Clean up legacy duplicate 33.5 MB bundle created by older script versions;
                // banw_pawns_win is now loaded directly at runtime.
                string winBundle = Path.Combine(rootDir, "1.6", "AssetBundles", "banw_pawns_win");
                string macBundle = Path.Combine(rootDir, "1.6", "AssetBundles", "banw_pawns_mac");
                if (File.Exists(winBundle) && File.Exists(macBundle))
                {
                    var winInfo = new FileInfo(winBundle);
                    var macInfo = new FileInfo(macBundle);
                    if (winInfo.Length == macInfo.Length)
                        File.Delete(macBundle);
                }

                // Keep About/About.xml's ChezhouLib dependency valid across Steam updates so ModLister doesn't warn.
                string aboutPath = Path.Combine(rootDir, "About", "About.xml");
                if (File.Exists(aboutPath))
                {
                    string text = File.ReadAllText(aboutPath);
                    if (text.Contains("Chezhou.ChezhouLib.lib") && !text.Contains("3595247479"))
                    {
                        string updated = Regex.Replace(
                            text,
                            @"(<packageId>\s*Chezhou\.ChezhouLib\.lib\s*</packageId>\s*<displayName>[^<]*</displayName>)(\s*</li>)",
                            "$1\n\t\t\t<steamWorkshopUrl>steam://url/CommunityFilePage/3595247479</steamWorkshopUrl>$2");
                        if (updated != text)
                            File.WriteAllText(aboutPath, updated);
                    }
                }
            }
            catch (Exception)
            {
                // Non-critical housekeeping; ignore read-only filesystem errors.
            }
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
            foreach (var sg in go.GetComponentsInChildren<Spine.Unity.SkeletonGraphic>(true))
            {
                if (sg.additiveMaterial != null) NoteShader(sg.additiveMaterial.shader);
                if (sg.multiplyMaterial != null) NoteShader(sg.multiplyMaterial.shader);
                if (sg.screenMaterial != null) NoteShader(sg.screenMaterial.shader);
            }
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
        static readonly HashSet<int> straightTextures = new HashSet<int>();
        public static bool SweepRequested;
        public static int FollowUpSweeps;

        public static void RequestSweep()
        {
            SweepRequested = true;
            FollowUpSweeps = 2;
        }

        public static void Invalidate()
        {
            checkedMaterials.Clear();
            RequestSweep();
        }

        public static bool HasPendingWork
        {
            get { return SweepRequested || FollowUpSweeps > 0 || pending.Count > 0; }
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
            foreach (var sg in go.GetComponentsInChildren<Spine.Unity.SkeletonGraphic>(true))
            {
                FixMaterial(sg.additiveMaterial, true);
                FixMaterial(sg.multiplyMaterial, true);
                FixMaterial(sg.screenMaterial, true);
                UseStraightAlpha(sg);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    FixMaterial(m, false);
        }

        public static bool IsReplaced(Material m)
        {
            return m != null && tracked.ContainsKey(m.GetInstanceID());
        }

        /// <summary>
        /// Spine premultiplies both its vertex colors and its atlases ("pma:true"), but the replacement UI shader
        /// multiplies color by alpha itself, so everything translucent is darkened twice: slots fading in or out
        /// (e.g. the patterns on Arona's top) dip dark and flash, and soft shading (blush, eye shadows) turns into
        /// grey smudges. Switching both to straight alpha makes the replacement shader's math correct.
        /// Additive slots are hidden by Patch_SkeletonGraphic_UpdateMesh.
        /// </summary>
        static void UseStraightAlpha(Spine.Unity.SkeletonGraphic sg)
        {
            if (!IsReplaced(sg.material) || sg.MeshGenerator == null)
                return;
            sg.MeshGenerator.settings.pmaVertexColors = false;
            var data = sg.skeletonDataAsset;
            if (data == null || data.atlasAssets == null)
                return;
            foreach (var atlas in data.atlasAssets)
                if (atlas != null)
                    foreach (var page in atlas.Materials)
                        if (page != null)
                            Unpremultiply(page.mainTexture as Texture2D);
        }

        /// <summary>Rewrites a premultiplied texture in place with straight alpha, once per texture.</summary>
        static void Unpremultiply(Texture2D tex)
        {
            if (tex == null || !straightTextures.Add(tex.GetInstanceID()))
                return;
            if (tex.format != TextureFormat.RGBA32 || tex.mipmapCount != 1
                || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                Log.Warning("[BANW Mac Fix] Cannot convert " + tex.name + " (" + tex.format + ") to straight alpha");
                return;
            }
            // Atlases are not CPU-readable: read them back through a render texture.
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = copy.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                int a = px[i].a;
                if (a == 0 || a == 255)
                    continue;
                px[i].r = (byte)Math.Min(255, (px[i].r * 255 + a / 2) / a);
                px[i].g = (byte)Math.Min(255, (px[i].g * 255 + a / 2) / a);
                px[i].b = (byte)Math.Min(255, (px[i].b * 255 + a / 2) / a);
            }
            copy.SetPixels32(px);
            copy.Apply(false);
            Graphics.CopyTexture(copy, tex);
            UnityEngine.Object.Destroy(copy);
        }

        /// <summary>Catches materials created or cloned asynchronously after bundle/prefab loads.</summary>
        public static void Sweep()
        {
            if (!SweepRequested && FollowUpSweeps > 0)
                FollowUpSweeps--;
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
            foreach (var sg in Resources.FindObjectsOfTypeAll<Spine.Unity.SkeletonGraphic>())
                if (sg != null)
                    UseStraightAlpha(sg);
        }
    }

    public class ShaderSweeper : MonoBehaviour
    {
        const float FollowUpInterval = 0.75f;
        float nextSweep;

        void Update()
        {
            if (!ShaderFixer.HasPendingWork)
                return;
            if (!ShaderFixer.SweepRequested && Time.realtimeSinceStartup < nextSweep)
                return;
            nextSweep = Time.realtimeSinceStartup + FollowUpInterval;
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

    /// <summary>
    /// Runtime patches for BANWlLib.dll methods that construct Windows backslash paths for videos, portraits,
    /// and voice lines, plus suppressing the NVIDIA-only GPU warning dialog on macOS and keeping damage numbers
    /// off RimWorld's loading thread.
    /// Applied dynamically so BANWMacFix does not require a hard assembly reference to BANWlLib.dll.
    /// </summary>
    static class DynamicPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchPathArg0(harmony, "newpro.imgcvT2d", "LoadSpriteFromFile");
            PatchPathArg0(harmony, "BANWlLib.mainUI.lordHudong", "LoadAudioClipBlocking");
            PatchPathArg0(harmony, "MyCoolMusicMod.MyCoolMusicMod.LoopBGMManager", "PlayClipDirectly");
            PatchPathArg1(harmony, "BANWlLib.mainUI.Gaka.Gakalord", "PlayLocalVideo");

            var gpuCheck = AccessTools.Method("BANWlLib.Init.StartupModCheck:CheckGPUCompatibility");
            if (gpuCheck != null)
                harmony.Patch(gpuCheck, prefix: new HarmonyMethod(typeof(DynamicPatches), nameof(SkipOnMacPrefix)));

            // Every damage/heal number goes through ShowFloatText.
            var floatText = AccessTools.Method("BANWlLib.DamageFontSystem.CriticalObjPool:ShowFloatText");
            if (floatText != null)
                harmony.Patch(floatText, prefix: new HarmonyMethod(typeof(DynamicPatches), nameof(MainThreadOnlyPrefix)));
        }

        static void PatchPathArg0(Harmony harmony, string typeName, string methodName)
        {
            var target = AccessTools.Method(typeName + ":" + methodName);
            if (target != null)
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(DynamicPatches), nameof(NormalizeArg0Prefix)));
        }

        static void PatchPathArg1(Harmony harmony, string typeName, string methodName)
        {
            var target = AccessTools.Method(typeName + ":" + methodName);
            if (target != null)
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(DynamicPatches), nameof(NormalizeArg1Prefix)));
        }

        public static void NormalizeArg0Prefix(ref string __0)
        {
            if (__0 != null && __0.IndexOf('\\') >= 0)
                __0 = __0.Replace('\\', '/');
        }

        public static void NormalizeArg1Prefix(ref string __1)
        {
            if (__1 != null && __1.IndexOf('\\') >= 0)
                __1 = __1.Replace('\\', '/');
        }

        public static bool SkipOnMacPrefix()
        {
            return !BundleTracker.Active;
        }

        /// <summary>
        /// Pawns damaged during map generation (e.g. half-eaten ancients in cryptosleep caskets) are damaged on
        /// RimWorld's loading thread, where laying out the number's UI text aborts the game on Metal. Nobody can
        /// see those numbers anyway. Must not touch Unity (including BundleTracker.Active) before the check.
        /// </summary>
        public static bool MainThreadOnlyPrefix()
        {
            return UnityData.IsInMainThread;
        }
    }

    /// <summary>
    /// BlueArchive-NewWorld ships student/Sensei textures in banw_pawns_win with no _mac bundle.
    /// Redirecting BundleSuffixForCurrentOs for archive.newworld lets RimWorld (and ChezhouLib) load
    /// banw_pawns_win directly without duplicating 33.5 MB on disk.
    /// </summary>
    [HarmonyPatch(typeof(ModAssetBundlesHandler), "get_BundleSuffixForCurrentOs")]
    static class Patch_ModAssetBundlesHandler_BundleSuffix
    {
        static readonly FieldInfo ModField = AccessTools.Field(typeof(ModAssetBundlesHandler), "mod");

        static void Postfix(ModAssetBundlesHandler __instance, ref string __result)
        {
            if (__result != "_mac" || ModField == null)
                return;
            var mod = ModField.GetValue(__instance) as ModContentPack;
            if (mod != null && string.Equals(mod.PackageId, "archive.newworld", StringComparison.OrdinalIgnoreCase))
                __result = "_win";
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
            {
                BundleTracker.NoteAsset(__result);
                if (__result is GameObject)
                    ShaderFixer.RequestSweep();
            }
            ShaderFixer.FixObject(__result);
        }
    }

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadAllAssets), new[] { typeof(Type) })]
    static class Patch_AssetBundle_LoadAllAssets
    {
        static void Postfix(AssetBundle __instance, UnityEngine.Object[] __result)
        {
            if (__result == null)
                return;
            bool isBanw = BundleTracker.IsBanwBundle(__instance);
            for (int i = 0; i < __result.Length; i++)
            {
                var obj = __result[i];
                if (obj == null)
                    continue;
                if (isBanw)
                    BundleTracker.NoteAsset(obj);
                ShaderFixer.FixObject(obj);
            }
        }
    }

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile), new[] { typeof(string) })]
    static class Patch_AssetBundle_LoadFromFile
    {
        static void Prefix()
        {
            BundleTracker.CaptureNativeShaders();
        }

        static void Postfix(string path, AssetBundle __result)
        {
            if (__result != null && BundleTracker.IsBanwPath(path))
                BundleTracker.AddBundle(__result, path);
            ShaderFixer.RequestSweep();
        }
    }

    /// <summary>
    /// The replacement shader cannot blend additively, and additive art is painted on black, so with straight vertex
    /// colors additive slots draw as dark sheets (Momoka's phone screen, halo glows). Hide them; premultiplied
    /// vertex colors hid them too, by zeroing their alpha.
    /// </summary>
    [HarmonyPatch(typeof(Spine.Unity.SkeletonGraphic), nameof(Spine.Unity.SkeletonGraphic.UpdateMesh))]
    static class Patch_SkeletonGraphic_UpdateMesh
    {
        static void Prefix(Spine.Unity.SkeletonGraphic __instance)
        {
            var gen = __instance.MeshGenerator;
            if (gen == null || gen.settings.pmaVertexColors || !ShaderFixer.IsReplaced(__instance.material))
                return;
            var skeleton = __instance.Skeleton;
            if (skeleton == null)
                return;
            var slots = skeleton.Slots.Items;
            for (int i = 0, n = skeleton.Slots.Count; i < n; i++)
                if (slots[i].Data.BlendMode == Spine.BlendMode.Additive)
                    slots[i].A = 0;
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
