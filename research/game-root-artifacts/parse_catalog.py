import UnityPy
import os
import re

catalog_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\catalog.bundle"
output_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya"

print("Loading catalog...", flush=True)
env = UnityPy.load(catalog_path)

for obj in env.objects:
    if obj.type.name == "TextAsset":
        data = obj.read()
        text = data.m_Script
        if isinstance(text, bytes):
            text = text.decode('utf-8', errors='ignore')
        
        # Save first 200KB for analysis
        preview_path = os.path.join(output_dir, "catalog_preview.txt")
        with open(preview_path, 'w', encoding='utf-8') as f:
            f.write(text[:200000])
        print(f"Saved catalog preview ({len(text[:200000])} chars)", flush=True)
        
        # Search for customer-related entries
        # Look for patterns like customer, rare, CustomerRare
        for keyword in ["CustomerRare", "customer_rare", "RareCustomer", "positiveTag", "negativeTag", "beverageTag"]:
            count = text.count(keyword)
            if count > 0:
                idx = text.find(keyword)
                context = text[max(0,idx-100):idx+300]
                print(f"\nFound '{keyword}' {count} times. First context:", flush=True)
                print(context[:400], flush=True)
        
        # Search for Chinese customer names
        for name in ["莉格露", "博丽灵梦", "芙兰朵露"]:
            count = text.count(name)
            if count > 0:
                idx = text.find(name)
                context = text[max(0,idx-100):idx+200]
                print(f"\nFound '{name}' {count} times. Context:", flush=True)
                print(context[:300], flush=True)

print("\nDone.", flush=True)