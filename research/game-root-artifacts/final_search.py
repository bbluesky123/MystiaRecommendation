import os
import struct

# Search resources.assets and other key files
game_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\final_search.txt"

# Unity stores strings with 4-byte little-endian length prefix
def make_unity_string(s):
    """Create Unity-serialized string bytes: 4-byte length + UTF-8 bytes"""
    encoded = s.encode('utf-8')
    return struct.pack('<I', len(encoded)) + encoded

targets = {
    "莉格露": make_unity_string("莉格露"),
    "博丽灵梦": make_unity_string("博丽灵梦"),
    "positiveTags": make_unity_string("positiveTags"),
    "enduranceLimit": make_unity_string("enduranceLimit"),
    "negativeTags": make_unity_string("negativeTags"),
    "beverageTags": make_unity_string("beverageTags"),
}

# Also try raw UTF-8
raw_targets = {
    "莉格露_raw": "莉格露".encode('utf-8'),
    "博丽灵梦_raw": "博丽灵梦".encode('utf-8'),
}

# Key files to search
key_files = [
    os.path.join(game_dir, "resources.assets"),
    os.path.join(game_dir, "sharedassets0.assets"),
    os.path.join(game_dir, "level0"),
    os.path.join(game_dir, "globalgamemanagers"),
    os.path.join(game_dir, "globalgamemanagers.assets"),
]

with open(output_path, "w", encoding="utf-8") as out:
    for fpath in key_files:
        if not os.path.exists(fpath):
            out.write(f"{os.path.basename(fpath)}: NOT FOUND\n")
            continue
        
        fsize = os.path.getsize(fpath)
        out.write(f"\n=== {os.path.basename(fpath)} ({fsize/(1024*1024):.1f}MB) ===\n")
        
        with open(fpath, 'rb') as f:
            data = f.read()
        
        for name, pattern in targets.items():
            count = data.count(pattern)
            if count > 0:
                out.write(f"  Unity format '{name}': {count} occurrences\n")
        
        for name, pattern in raw_targets.items():
            count = data.count(pattern)
            if count > 0:
                out.write(f"  Raw '{name}': {count} occurrences\n")
    
    # Also check bundle files - but only small ones and catalog.bundle
    bundle_dir = os.path.join(game_dir, "StreamingAssets", "aa", "StandaloneWindows64")
    catalog_path = os.path.join(game_dir, "StreamingAssets", "aa", "catalog.bundle")
    
    for check_path, label in [(catalog_path, "catalog.bundle")]:
        if not os.path.exists(check_path):
            out.write(f"\n{label}: NOT FOUND\n")
            continue
        fsize = os.path.getsize(check_path)
        with open(check_path, 'rb') as f:
            data = f.read()
        
        out.write(f"\n=== {label} ({fsize/(1024*1024):.1f}MB) ===\n")
        for name, pattern in targets.items():
            count = data.count(pattern)
            if count > 0:
                out.write(f"  Unity format '{name}': {count} occurrences\n")
        for name, pattern in raw_targets.items():
            count = data.count(pattern)
            if count > 0:
                out.write(f"  Raw '{name}': {count} occurrences\n")

print("Done. Check final_search.txt")