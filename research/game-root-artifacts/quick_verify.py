import os
import json

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\verification_result.txt"

# Only search for a few key customers to identify which bundle has customer data
key_names = [b"\xe8\x8e\x89\xe6\xa0\xbc\xe9\x9c\xb2",  # 莉格露
             b"\xe5\x8d\x9a\xe4\xb8\xbd\xe7\x81\xb5\xe6\xa2\xa6",  # 博丽灵梦
             b"\xe9\x9b\xbe\xe9\x9b\xa8\xe9\xad\x94\xe7\x90\x86\xe6\xb2\x99",  # 雾雨魔理沙
             b"\xe8\x8a\x99\xe5\x85\xb0\xe6\x9c\xb5\xe9\x9c\xb2",  # 芙兰朵露
             ]

# Search for tag strings that would only appear in customer data
tag_strings = [b"\xe8\x82\x89",  # 肉
               b"\xe6\xb5\xb7\xe5\x91\xb3",  # 海味
               b"\xe5\x92\x8c\xe9\xa3\x8e",  # 和风
               b"\xe4\xb8\xad\xe5\x8d\x8e",  # 中华
               ]

results = {}

for fname in os.listdir(bundle_dir):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        fsize = os.path.getsize(fpath)
        if fsize > 50 * 1024 * 1024:  # Skip files > 50MB
            continue
        with open(fpath, 'rb') as f:
            data = f.read()
        
        found_names = []
        for name_bytes in key_names:
            if name_bytes in data:
                found_names.append(name_bytes.decode('utf-8'))
        
        if found_names:
            results[fname] = {"names": found_names, "size": fsize}
    except:
        pass

with open(output_path, "w", encoding="utf-8") as out:
    out.write(f"=== Bundles containing rare customer names ===\n\n")
    for fname, info in sorted(results.items(), key=lambda x: -len(x[1]["names"])):
        out.write(f"{fname} ({info['size']//1024}KB): {info['names']}\n")
    
    out.write(f"\nTotal: {len(results)} bundles\n")

print(f"Done. Found {len(results)} bundles with customer names")
for fname, info in sorted(results.items(), key=lambda x: -len(x[1]["names"])):
    print(f"  {fname}: {info['names']}")