import os
import re

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\game_data_raw.txt"

# Search for "莉格露" followed by tag-like content
# In Unity bundles, strings are stored as UTF-8 with length prefix
target = "莉格露".encode('utf-8')

found_bundles = []

for fname in os.listdir(bundle_dir):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        fsize = os.path.getsize(fpath)
        if fsize > 50 * 1024 * 1024:
            continue
        with open(fpath, 'rb') as f:
            data = f.read()
        
        pos = data.find(target)
        if pos != -1:
            # Extract a large window around the found name
            start = max(0, pos - 500)
            end = min(len(data), pos + 5000)
            window = data[start:end]
            
            # Try to extract readable strings from this window
            strings = []
            # Find UTF-8 strings (sequences of valid UTF-8 that are at least 2 chars)
            for m in re.finditer(rb'[\xe0-\xef][\x80-\xbf]{2}|[\xc0-\xdf][\x80-\xbf]|[\x20-\x7e]{3,}', window):
                try:
                    s = window[m.start():m.end()].decode('utf-8', errors='ignore')
                    if len(s) >= 2:
                        strings.append(s)
                except:
                    pass
            
            found_bundles.append((fname, fsize, pos, strings[:50]))
            print(f"Found in {fname} at pos {pos}, size={fsize}", flush=True)
    except Exception as e:
        pass

with open(output_path, "w", encoding="utf-8") as out:
    for fname, fsize, pos, strings in found_bundles:
        out.write(f"\n=== {fname} (size={fsize}, pos={pos}) ===\n")
        out.write("Strings near customer name:\n")
        for s in strings:
            out.write(f"  {s}\n")

print(f"\nFound in {len(found_bundles)} bundles")
print("Check game_data_raw.txt")