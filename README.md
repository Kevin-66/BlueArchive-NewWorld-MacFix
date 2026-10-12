# BlueArchive-NewWorld Mac Fix

Unofficial macOS fixes for the RimWorld 1.6 mod
[BlueArchive-NewWorld](https://steamcommunity.com/sharedfiles/filedetails/?id=3560667190).
Not affiliated with the mod's author. Contains no files from the original mod.

## What's broken on macOS, and what fixes it

| Symptom | Cause | Fixed by |
|---|---|---|
| Students and Sensei have no textures | Character art ships in `banw_pawns_win`; RimWorld skips `_win` bundles on macOS | `tools/apply_mac_fixes.py` |
| Gacha banner videos and some avatars missing | Paths built with Windows `\` separators in `BANWlLib.dll` | `tools/apply_mac_fixes.py` |
| Arona/Plana and student voice lines silent | `\` in `DraggableAudio` / `aronaAudioClipPath` in Defs | `tools/apply_mac_fixes.py` |
| Magenta backgrounds, particles and gacha effects | Bundled shaders contain DirectX code only | companion mod |
| `Animation not found: Eye_Close` exceptions | Some skeletons lack animations the UI requests | companion mod |
| Debug log opens at every launch | Himari's trait uses Anomaly's `EntityStudyRate` stat; ChezhouLib dependency has no link | companion mod + script |

Blend modes are approximated with the game's own shaders, so some glow/lighting effects look flatter than on Windows.

## Install

1. Clone this repo into RimWorld's local mods folder:
   ```bash
   git clone https://github.com/Kevin-66/BlueArchive-NewWorld-MacFix.git "$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/BANW-MacFix"
   ```
2. In RimWorld's mod list, enable **BlueArchive-NewWorld Mac Fix** and place it after BlueArchive-NewWorld.
3. With RimWorld closed, apply the file fixes:
   ```bash
   python3 "$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/BANW-MacFix/tools/apply_mac_fixes.py"
   ```

Steam restores the original mod files on every update of BlueArchive-NewWorld, so re-run step 3 after each update.
The script is idempotent and backs up each original to `~/Library/Application Support/RimWorld/BANW-MacFix/backup`
before patching it.

## Build

The compiled `1.6/Assemblies/BANWMacFix.dll` is included. To rebuild it you need Mono (`brew install mono`), RimWorld,
Harmony and BlueArchive-NewWorld installed from Steam:

```bash
tools/build.sh
```
