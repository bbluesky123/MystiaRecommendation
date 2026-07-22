import os
import struct

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\bundle_with_customers.txt"

# Quick search for UTF-8 encoded customer names
target_names = [
    "莉格露".encode('utf-8'),
    "博丽灵梦".encode('utf-8'),
    "芙兰朵露".encode('utf-8'),
]

found = []
for fname in sorted(os.listdir(bundle_dir)):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    fsize = os.path.getsize(fpath)
    try:
        with open(fpath, 'rb') as f:
            data = f.read(50 * 1024 * 1024)  # Read first 50MB
        
        names_found = []
        for tname in target_names:
            if tname in data:
                names_found.append(tname.decode('utf-8'))
        
        if names_found:
            found.append((fname, fsize, names_found))
            print(f"Found: {fname} ({fsize//1024}KB) - {names_found}", flush=True)
    except:
        pass

with open(output_path, 'w', encoding='utf-8') as out:
    for fname, fsize, names in found:
        out.write(f"{fname} ({fsize//1024}KB): {names}\n")

print(f"\nTotal: {len(found)} bundles found", flush=True)