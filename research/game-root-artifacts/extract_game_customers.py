import UnityPy
import os
import json
import re

bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\game_customer_data.json"

# Known rare customer names to search for
key_customers = ["莉格露", "博丽灵梦", "雾雨魔理沙", "芙兰朵露", "八意永琳", "蓬莱山辉夜", "比那名居天子", "伊吹萃香", "上白泽慧音", "蕾米莉亚", "天弓千亦", "饭纲丸龙", "小恶魔", "神绮"]

found_bundles = []

# Step 1: Find which bundles contain customer data
print("Step 1: Scanning bundles for customer data...", flush=True)

for fname in sorted(os.listdir(bundle_dir)):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        env = UnityPy.load(fpath)
        found = False
        for obj in env.objects:
            if obj.type.name == "TextAsset":
                data = obj.read()
                text = data.m_Script if hasattr(data, 'm_Script') else ""
                if isinstance(text, bytes):
                    text = text.decode('utf-8', errors='ignore')
                for name in key_customers[:5]:  # Check first 5 names
                    if name in text:
                        found = True
                        break
                if found:
                    found_bundles.append((fname, fpath))
                    print(f"  Found customer data in: {fname}", flush=True)
                    break
        if found:
            continue
            
        # Also check MonoBehaviour
        for obj in env.objects:
            if obj.type.name == "MonoBehaviour":
                try:
                    data = obj.read()
                    tree = data.read_typetree()
                    if isinstance(tree, dict):
                        text = json.dumps(tree, ensure_ascii=False)
                        for name in key_customers[:5]:
                            if name in text:
                                found_bundles.append((fname, fpath))
                                print(f"  Found customer data in: {fname} (MonoBehaviour)", flush=True)
                                found = True
                                break
                except:
                    pass
                if found:
                    break
    except Exception as e:
        pass

print(f"\nFound {len(found_bundles)} bundles with customer data", flush=True)

# Step 2: Extract data from found bundles
print("\nStep 2: Extracting customer data...", flush=True)

all_game_customers = {}

for fname, fpath in found_bundles:
    print(f"  Processing: {fname}", flush=True)
    env = UnityPy.load(fpath)
    
    for obj in env.objects:
        # Try TextAsset first
        if obj.type.name == "TextAsset":
            data = obj.read()
            text = data.m_Script if hasattr(data, 'm_Script') else ""
            if isinstance(text, bytes):
                text = text.decode('utf-8', errors='ignore')
            
            # Check if this contains customer data
            has_customer = False
            for name in key_customers[:3]:
                if name in text:
                    has_customer = True
                    break
            
            if has_customer:
                print(f"    TextAsset: {data.m_Name if hasattr(data, 'm_Name') else 'unnamed'} ({len(text)} chars)", flush=True)
                
                # Save the raw text for analysis
                raw_path = f"d:\\steam\\steamapps\\common\\Touhou Mystia Izakaya\\game_raw_{data.m_Name if hasattr(data, 'm_Name') else fname}.txt"
                with open(raw_path, 'w', encoding='utf-8') as f:
                    f.write(text)
                print(f"    Saved raw text to: {raw_path}", flush=True)
        
        # Try MonoBehaviour
        if obj.type.name == "MonoBehaviour":
            try:
                data = obj.read()
                tree = data.read_typetree()
                if isinstance(tree, dict):
                    text = json.dumps(tree, ensure_ascii=False)
                    for name in key_customers[:3]:
                        if name in text:
                            print(f"    MonoBehaviour found with customer data", flush=True)
                            # Save for analysis
                            mono_path = f"d:\\steam\\steamapps\\common\\Touhou Mystia Izakaya\\game_mono_{fname.replace('.bundle','')}.json"
                            with open(mono_path, 'w', encoding='utf-8') as f:
                                json.dump(tree, f, ensure_ascii=False, indent=2)
                            print(f"    Saved to: {mono_path}", flush=True)
                            break
            except Exception as e:
                pass

print("\nStep 3: Done. Check the saved files for analysis.", flush=True)