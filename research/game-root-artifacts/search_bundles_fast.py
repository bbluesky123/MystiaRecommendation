import os
import sys

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_file = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\search_results.txt"

search_terms = [b"positiveTags", b"negativeTags", b"beverageTags", b"enduranceLimit"]

found = {}

for fname in os.listdir(bundle_dir):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        with open(fpath, 'rb') as f:
            data = f.read(1024*1024)
        for term in search_terms:
            pos = data.find(term)
            if pos != -1:
                if fname not in found:
                    found[fname] = []
                found[fname].append(term.decode())
    except:
        pass

with open(output_file, 'w', encoding='utf-8') as out:
    out.write(f"Found {len(found)} bundles:\n")
    for fname, terms in sorted(found.items()):
        out.write(f"  {fname}: {', '.join(terms)}\n")