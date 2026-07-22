import UnityPy
import os
import json

game_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\main_assets_result.txt"

# Check main Unity asset files
files_to_check = [
    "resources.assets",
    "sharedassets0.assets",
    "globalgamemanagers",
    "globalgamemanagers.assets",
]

with open(output, 'w', encoding='utf-8') as out:
    for fname in files_to_check:
        fpath = os.path.join(game_dir, fname)
        if not os.path.exists(fpath):
            out.write(f"{fname}: NOT FOUND\n")
            continue
        
        fsize = os.path.getsize(fpath)
        out.write(f"\n=== {fname} ({fsize//1024}KB) ===\n")
        
        try:
            env = UnityPy.load(fpath)
            obj_count = 0
            for obj in env.objects:
                obj_count += 1
                if obj.type.name in ["MonoBehaviour", "TextAsset"]:
                    try:
                        data = obj.read()
                        if obj.type.name == "TextAsset":
                            text = data.m_Script
                            if isinstance(text, bytes):
                                text = text.decode('utf-8', errors='ignore')
                            if "莉格露" in text:
                                out.write(f"  FOUND in TextAsset {getattr(data, 'm_Name', 'unknown')}: {len(text)} chars\n")
                                txt_path = os.path.join(os.path.dirname(output), f"main_text_{fname}_{getattr(data, 'm_Name', 'unknown')}.txt")
                                with open(txt_path, 'w', encoding='utf-8') as f:
                                    f.write(text[:500000])
                                out.write(f"  Saved to {os.path.basename(txt_path)}\n")
                        else:
                            try:
                                tree = data.read_typetree()
                                if isinstance(tree, dict):
                                    text = json.dumps(tree, ensure_ascii=False)
                                    if "莉格露" in text or "positiveTag" in text:
                                        out.write(f"  FOUND in MonoBehaviour: {len(text)} chars\n")
                                        mono_path = os.path.join(os.path.dirname(output), f"main_mono_{fname}.json")
                                        with open(mono_path, 'w', encoding='utf-8') as f:
                                            json.dump(tree, f, ensure_ascii=False, indent=2)
                                        out.write(f"  Saved to {os.path.basename(mono_path)}\n")
                            except:
                                pass
                    except:
                        pass
            
            out.write(f"  Total objects: {obj_count}\n")
        except Exception as e:
            out.write(f"  Error: {e}\n")

print("Done.", flush=True)