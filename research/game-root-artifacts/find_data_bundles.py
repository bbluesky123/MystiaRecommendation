import UnityPy
import os
import json

catalog_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\catalog.bundle"
bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\data_bundles_result.txt"

with open(output, 'w', encoding='utf-8') as out:
    # First, look at what's in each bundle using UnityPy
    # Focus on bundles that might contain game data (not art/audio)
    # Check a few small bundles first
    
    bundles_checked = 0
    bundles_with_data = []
    
    for fname in sorted(os.listdir(bundle_dir)):
        if not fname.endswith('.bundle'):
            continue
        fpath = os.path.join(bundle_dir, fname)
        fsize = os.path.getsize(fpath)
        
        # Skip very large bundles (likely art/audio) and very small ones
        if fsize > 10 * 1024 * 1024 or fsize < 1024:
            continue
        
        try:
            env = UnityPy.load(fpath)
            has_interesting = False
            obj_types = []
            
            for obj in env.objects:
                obj_types.append(obj.type.name)
                
                if obj.type.name == "MonoBehaviour":
                    try:
                        data = obj.read()
                        tree = data.read_typetree()
                        if isinstance(tree, dict):
                            text = json.dumps(tree, ensure_ascii=False)
                            if "莉格露" in text or "positiveTag" in text or "beverageTag" in text:
                                has_interesting = True
                                mono_path = os.path.join(os.path.dirname(output), f"game_data_{fname.replace('.bundle','')}.json")
                                with open(mono_path, 'w', encoding='utf-8') as f:
                                    json.dump(tree, f, ensure_ascii=False, indent=2)
                                out.write(f"FOUND CUSTOMER DATA in {fname} ({fsize//1024}KB) -> saved to {os.path.basename(mono_path)}\n")
                                print(f"FOUND: {fname}", flush=True)
                    except Exception as e:
                        pass
                
                if obj.type.name == "TextAsset":
                    try:
                        data = obj.read()
                        text = data.m_Script
                        if isinstance(text, bytes):
                            text = text.decode('utf-8', errors='ignore')
                        if "莉格露" in text and ("positiveTag" in text or "beverageTag" in text):
                            has_interesting = True
                            txt_path = os.path.join(os.path.dirname(output), f"game_text_{fname.replace('.bundle','')}.txt")
                            with open(txt_path, 'w', encoding='utf-8') as f:
                                f.write(text[:500000])
                            out.write(f"FOUND TEXT DATA in {fname} ({fsize//1024}KB, {len(text)} chars) -> saved\n")
                            print(f"FOUND TEXT: {fname}", flush=True)
                    except Exception as e:
                        pass
            
            if has_interesting:
                bundles_with_data.append(fname)
            
            bundles_checked += 1
            if bundles_checked % 100 == 0:
                print(f"Checked {bundles_checked} bundles...", flush=True)
                
        except Exception as e:
            pass
    
    out.write(f"\nChecked {bundles_checked} bundles\n")
    out.write(f"Found {len(bundles_with_data)} bundles with customer data\n")
    for b in bundles_with_data:
        out.write(f"  {b}\n")

print(f"Done. Results in data_bundles_result.txt", flush=True)