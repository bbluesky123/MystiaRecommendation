import UnityPy
import os
import json

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\all_sizes_result.txt"

with open(output, 'w', encoding='utf-8') as out:
    # List all bundles by size, search the largest ones first
    bundles = []
    for fname in os.listdir(bundle_dir):
        if fname.endswith('.bundle'):
            fpath = os.path.join(bundle_dir, fname)
            fsize = os.path.getsize(fpath)
            bundles.append((fname, fpath, fsize))
    
    bundles.sort(key=lambda x: -x[2])
    
    out.write(f"Total bundles: {len(bundles)}\n")
    out.write(f"Largest bundles:\n")
    for fname, fpath, fsize in bundles[:20]:
        out.write(f"  {fname}: {fsize//1024}KB ({fsize/(1024*1024):.1f}MB)\n")
    
    # Search bundles > 10MB
    found = []
    for fname, fpath, fsize in bundles:
        if fsize <= 10 * 1024 * 1024:
            break  # Already sorted descending, so we can stop
        
        try:
            env = UnityPy.load(fpath)
            for obj in env.objects:
                if obj.type.name == "TextAsset":
                    try:
                        data = obj.read()
                        text = data.m_Script
                        if isinstance(text, bytes):
                            text = text.decode('utf-8', errors='ignore')
                        if "莉格露" in text and ("positiveTag" in text or "beverageTag" in text):
                            found.append(fname)
                            txt_path = os.path.join(os.path.dirname(output), f"game_text_{fname.replace('.bundle','')}.txt")
                            with open(txt_path, 'w', encoding='utf-8') as f:
                                f.write(text[:1000000])
                            out.write(f"\nFOUND TEXT: {fname} ({fsize//(1024*1024)}MB, {len(text)} chars)\n")
                            print(f"FOUND: {fname}", flush=True)
                    except:
                        pass
                
                if obj.type.name == "MonoBehaviour":
                    try:
                        data = obj.read()
                        tree = data.read_typetree()
                        if isinstance(tree, dict):
                            text = json.dumps(tree, ensure_ascii=False)
                            if "莉格露" in text or "positiveTag" in text:
                                found.append(fname)
                                mono_path = os.path.join(os.path.dirname(output), f"game_data_{fname.replace('.bundle','')}.json")
                                with open(mono_path, 'w', encoding='utf-8') as f:
                                    json.dump(tree, f, ensure_ascii=False, indent=2)
                                out.write(f"\nFOUND MONO: {fname} ({fsize//(1024*1024)}MB)\n")
                                print(f"FOUND MONO: {fname}", flush=True)
                    except:
                        pass
        except:
            pass
    
    out.write(f"\n\nFound {len(found)} bundles with customer data in large bundles\n")
    for b in found:
        out.write(f"  {b}\n")

print("Done.", flush=True)