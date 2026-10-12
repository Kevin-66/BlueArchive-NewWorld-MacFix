#!/bin/bash
# Builds 1.6/Assemblies/BANWMacFix.dll with Mono (brew install mono).
# Override RIMWORLD or WORKSHOP if your Steam library lives elsewhere.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RIMWORLD="${RIMWORLD:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld}"
WORKSHOP="${WORKSHOP:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100}"
MANAGED="$RIMWORLD/RimWorldMac.app/Contents/Resources/Data/Managed"
BANW="$WORKSHOP/3560667190/1.6/Assemblies"
HARMONY="$WORKSHOP/2009463077/Current/Assemblies/0Harmony.dll"

refs=()
for r in mscorlib System System.Core netstandard Assembly-CSharp UnityEngine UnityEngine.CoreModule \
         UnityEngine.UI UnityEngine.UIModule UnityEngine.AssetBundleModule; do
  refs+=("-r:$MANAGED/$r.dll")
done

mkdir -p "$ROOT/1.6/Assemblies"
mcs -target:library -nostdlib -noconfig -langversion:7.2 -optimize+ "${refs[@]}" \
  -r:"$HARMONY" -r:"$BANW/spine-csharp.dll" -r:"$BANW/spine-unity.dll" \
  -out:"$ROOT/1.6/Assemblies/BANWMacFix.dll" "$ROOT/Source/BANWMacFix.cs"
echo "Built $ROOT/1.6/Assemblies/BANWMacFix.dll"
