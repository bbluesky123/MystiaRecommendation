import re
import json
import os

chunk_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\chunk_data_3599.js"
bundle_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\StandaloneWindows64"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\comparison_result.txt"

# qs constants
qs = {
    "signature": "招牌",
    "popularPositive": "流行喜爱",
    "popularNegative": "流行厌恶",
    "expensive": "昂贵",
    "economical": "实惠",
    "largePartition": "大份"
}

# Read website data
with open(chunk_path, "r", encoding="utf-8") as f:
    content = f.read()

# Extract all customer data
customers = []
for match in re.finditer(r'\{id:(\d+),name:"([^"]+)",description:\[', content):
    cid = int(match.group(1))
    cname = match.group(2)
    start = match.start()
    
    depth = 0
    pos = start
    while pos < len(content):
        if content[pos] == '{':
            depth += 1
        elif content[pos] == '}':
            depth -= 1
            if depth == 0:
                break
        pos += 1
    
    block = content[start:pos+1]
    customer = {"id": cid, "name": cname}
    
    dlc_match = re.search(r'dlc:(\d+)', block)
    if dlc_match:
        customer["dlc"] = int(dlc_match.group(1))
    
    el_match = re.search(r'enduranceLimit:([\d.]+)', block)
    if el_match:
        customer["enduranceLimit"] = float(el_match.group(1))
    
    price_match = re.search(r'price:\[(\d+),(\d+)\]', block)
    if price_match:
        customer["price"] = [int(price_match.group(1)), int(price_match.group(2))]
    
    for tag_field in ["positiveTags", "negativeTags", "beverageTags"]:
        tag_match = re.search(rf'{tag_field}:\[(.*?)\]', block)
        if tag_match:
            tags_str = tag_match.group(1)
            tags = []
            for t in re.finditer(r'"([^"]*)"', tags_str):
                tags.append(t.group(1))
            for t in re.finditer(r'r\.qs\.(\w+)', tags_str):
                tag_name = t.group(1)
                tags.append(qs.get(tag_name, f"[{tag_name}]"))
            customer[tag_field] = tags
    
    # positiveTagMapping
    ptm_match = re.search(r'positiveTagMapping:\{(.*?)\}', block)
    if ptm_match:
        mapping = {}
        for kv in re.finditer(r'(?:"([^"]*)"|r\.qs\.(\w+)):"([^"]*)"', ptm_match.group(1)):
            key = kv.group(1) if kv.group(1) else qs.get(kv.group(2), kv.group(2))
            mapping[key] = kv.group(3)
        if mapping:
            customer["positiveTagMapping"] = mapping
    
    # beverageTagMapping
    btm_match = re.search(r'beverageTagMapping:\{(.*?)\}', block)
    if btm_match:
        mapping = {}
        for kv in re.finditer(r'"([^"]*)":"([^"]*)"', btm_match.group(1)):
            mapping[kv.group(1)] = kv.group(2)
        if mapping:
            customer["beverageTagMapping"] = mapping
    
    # spellCards - extract names and descriptions
    spell_cards = {"positive": [], "negative": []}
    for sc_type in ["positive", "negative"]:
        sc_pattern = rf'{sc_type}:\[\{{name:"([^"]*)",description:"([^"]*)"\}}'
        for sc_match in re.finditer(sc_pattern, block):
            spell_cards[sc_type].append({"name": sc_match.group(1), "description": sc_match.group(2)})
    if spell_cards["positive"] or spell_cards["negative"]:
        customer["spellCards"] = spell_cards
    
    customers.append(customer)

# Now search game bundles for customer data
print(f"Extracted {len(customers)} website customers", flush=True)
print("Searching game bundles for customer names...", flush=True)

# All customer names from website
website_names = {c["name"] for c in customers}

# Search bundles for customer names
game_strings = {}
for fname in os.listdir(bundle_dir):
    if not fname.endswith('.bundle'):
        continue
    fpath = os.path.join(bundle_dir, fname)
    try:
        with open(fpath, 'rb') as f:
            data = f.read()
        
        # Search for each customer name
        for name in website_names:
            encoded = name.encode('utf-8')
            if encoded in data:
                if name not in game_strings:
                    game_strings[name] = []
                game_strings[name].append(fname)
    except:
        pass

print(f"Found {len(game_strings)} customers in game bundles", flush=True)

# Write results
with open(output_path, "w", encoding="utf-8") as out:
    out.write(f"=== Website Customers: {len(customers)} ===\n\n")
    
    for c in sorted(customers, key=lambda x: (x.get("dlc", 0), x["id"])):
        out.write(f"[DLC{c.get('dlc', '?')}] {c['name']} (id={c['id']})\n")
        out.write(f"  enduranceLimit: {c.get('enduranceLimit', '?')}\n")
        out.write(f"  price: {c.get('price', '?')}\n")
        out.write(f"  positiveTags: {c.get('positiveTags', [])}\n")
        out.write(f"  negativeTags: {c.get('negativeTags', [])}\n")
        out.write(f"  beverageTags: {c.get('beverageTags', [])}\n")
        if "positiveTagMapping" in c:
            out.write(f"  positiveTagMapping: {c['positiveTagMapping']}\n")
        if "beverageTagMapping" in c:
            out.write(f"  beverageTagMapping: {c['beverageTagMapping']}\n")
        if "spellCards" in c:
            sc = c["spellCards"]
            if sc["positive"]:
                out.write(f"  spellCards(positive): {[s['name'] for s in sc['positive']]}\n")
            if sc["negative"]:
                out.write(f"  spellCards(negative): {[s['name'] for s in sc['negative']]}\n")
        out.write("\n")
    
    out.write(f"\n=== Game Bundle Customer Search ===\n\n")
    out.write(f"Found {len(game_strings)} customer names in game bundles\n\n")
    
    for name in sorted(game_strings.keys()):
        bundles = game_strings[name]
        out.write(f"  {name}: found in {len(bundles)} bundle(s): {bundles[:3]}{'...' if len(bundles) > 3 else ''}\n")
    
    # Check which website customers are NOT found in game
    missing_in_game = website_names - set(game_strings.keys())
    if missing_in_game:
        out.write(f"\n=== Customers in website but NOT found in game bundles ({len(missing_in_game)}) ===\n")
        for name in sorted(missing_in_game):
            out.write(f"  {name}\n")
    
    # Check which game customers are NOT in website
    extra_in_game = set(game_strings.keys()) - website_names
    if extra_in_game:
        out.write(f"\n=== Customers in game but NOT in website ({len(extra_in_game)}) ===\n")
        for name in sorted(extra_in_game):
            out.write(f"  {name}\n")

print("Done. Check comparison_result.txt")