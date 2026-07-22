import os
import struct

# Search for customer-related strings in bundle files
bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"

# Search for tags/patterns that indicate customer data
search_terms = ["positiveTags", "negativeTags", "beverageTags", "enduranceLimit", "spellCard", "bondRecipe", "CustomerRareData", "CustomerData", "RareCustomer", "rare_customer"]

print("Searching bundle files for customer data patterns...")
found_files = set()

for fname in os.listdir(bundle_dir):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        with open(fpath, 'rb') as f:
            data = f.read()
            data_str = data.decode('utf-8', errors='ignore')
            
            for term in search_terms:
                if term in data_str:
                    found_files.add(fname)
                    print(f"  Found '{term}' in {fname}")
                    break
                    
    except Exception as e:
        pass

print(f"\nFound {len(found_files)} bundles with customer data patterns")
for fname in sorted(found_files):
    print(f"  {fname}")