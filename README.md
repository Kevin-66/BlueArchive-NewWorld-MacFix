# BlueArchive-NewWorld Mac Fix

Unofficial macOS compatibility mod for the RimWorld 1.6 mod
[BlueArchive-NewWorld](https://steamcommunity.com/sharedfiles/filedetails/?id=3560667190).
Not affiliated with the mod's author. Contains no files from the original mod.

All fixes run automatically at runtime via Harmony and XML patches—no external scripts or manual re-patching after Steam Workshop updates required.

## What's broken on macOS, and how this mod fixes it

| Symptom | Cause | Runtime Fix |
|---|---|---|
| Students and Sensei have no textures | Character art ships in `banw_pawns_win`; RimWorld skips `_win` bundles on macOS | Redirects bundle OS suffix lookup for `archive.newworld` so `banw_pawns_win` loads directly |
| Gacha banner videos and some avatars missing | Paths built with Windows `\` separators in `BANWlLib.dll` | Harmony prefixes normalize `\` to `/` in `PlayLocalVideo` and `LoadSpriteFromFile` |
| Arona/Plana and student voice lines silent | `\` in `DraggableAudio` / `aronaAudioClipPath` in Defs | Harmony prefixes normalize `\` to `/` in `LoadAudioClipBlocking` and `PlayClipDirectly` |
| Magenta backgrounds, damage numbers, particles, and gacha effects | Bundled shaders contain DirectX code only | Early `Mod` initialization & `AssetBundle` hooks swap broken shaders (including `SkeletonGraphic` blend materials) to Metal-compatible game shaders |
| `Animation not found: Eye_Close` exceptions | Some skeletons lack animations the UI requests | Harmony prefix on `Spine.AnimationState` returns a no-op `TrackEntry` |
| Non-NVIDIA GPU modal warning on every launch | `StartupModCheck` warns whenever GPU vendor ID is not NVIDIA | Harmony prefix skips `CheckGPUCompatibility` on Metal/macOS |
| Debug log opens at every launch | Himari's trait uses Anomaly's `EntityStudyRate` stat; `ChezhouLib` dependency lacks Workshop URL | Conditional XML patch removes `EntityStudyRate` when Anomaly is inactive; auto-heals `About.xml` |

Blend modes are approximated with the game's own shaders, so some glow/lighting effects look flatter than on Windows.

## Install

1. Clone this repo into RimWorld's local `Mods` folder:
   ```bash
   git clone https://github.com/Kevin-66/BlueArchive-NewWorld-MacFix.git "$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/BANW-MacFix"
   ```
2. In RimWorld's mod list, enable **BlueArchive-NewWorld Mac Fix** and place it after **BlueArchive-NewWorld**.

## Build

The compiled `1.6/Assemblies/BANWMacFix.dll` is included. To rebuild it you need Mono (`brew install mono`), RimWorld,
Harmony, and BlueArchive-NewWorld installed from Steam:

```bash
./tools/build.sh
```
