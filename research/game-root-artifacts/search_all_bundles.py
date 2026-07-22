import os

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\bundle_search_all.txt"

target = "莉格露".encode('utf-8')
target2 = "博丽灵梦".encode('utf-8')
target3 = "positiveTags".encode('utf-8')

results = []

for fname in sorted(os.listdir(bundle_dir)):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    fsize = os.path.getsize(fpath)
    try:
        with open(fpath, 'rb') as f:
            # Read in chunks for large files
            found = []
            offset = 0
            chunk_size = 10 * 1024 * 1024  # 10MB chunks
            overlap = 100  # overlap to catch strings at boundaries
            
            while True:
                f.seek(offset)
                chunk = f.read(chunk_size + overlap)
                if not chunk:
                    break
                
                if target in chunk:
                    found.append("莉格露")
                if target2 in chunk:
                    found.append("博丽灵梦")
                if target3 in chunk:
                    found.append("positiveTags")
                
                if len(chunk) < chunk_size:
                    break
                offset += chunk_size
                if found:
                    break  # Found enough
            
            if found:
                results.append((fname, fsize, found))
    except Exception as e:
        pass

with open(output_path, "w", encoding="utf-8") as out:
    for fname, fsize, found in results:
        size_mb = fsize / (1024*1024)
        out.write(f"{fname} ({size_mb:.1f}MB): {found}\n")
    out.write(f"\nTotal: {len(results)} bundles\n")

print(f"Found {len(results)} bundles:")
for fname, fsize, found in results:
    print(f"  {fname} ({fsize/(1024*1024):.1f}MB): {found}")