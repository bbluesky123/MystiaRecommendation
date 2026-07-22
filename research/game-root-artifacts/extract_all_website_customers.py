import re
import json
import os

chunk_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\chunk_data_3599.js"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\website_customers.json"

with open(chunk_path, "r", encoding="utf-8") as f:
    content = f.read()

# First, find the r.qs constants
# r.qs.signature, r.qs.popularPositive, r.qs.popularNegative, r.qs.expensive, r.qs.economical, r.qs.largePartition
qs = {}
for match in re.finditer(r'qs:?\{([^}]+)\}', content):
    qs_text = match.group(1)
    # Parse key:value pairs
    for kv in re.finditer(r'(\w+):"([^"]*)"', qs_text):
        qs[kv.group(1)] = kv.group(2)

print("Found qs constants:", qs)

# Now extract customer data blocks
# Pattern: {id:NUMBER,name:"NAME",...
customers = []
# Find all customer entries
for match in re.finditer(r'\{id:(\d+),name:"([^"]+)",description:\[', content):
    cid = int(match.group(1))
    cname = match.group(2)
    start = match.start()
    
    # Find the end of this customer object by counting braces
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
    
    # Extract fields
    customer = {"id": cid, "name": cname}
    
    # dlc
    dlc_match = re.search(r'dlc:(\d+)', block)
    if dlc_match:
        customer["dlc"] = int(dlc_match.group(1))
    
    # places
    places_match = re.search(r'places:\[(.*?)\]', block)
    if places_match:
        places_str = places_match.group(1)
        customer["places"] = [p.strip('"') for p in re.findall(r'"([^"]*)"', places_str)]
    
    # price
    price_match = re.search(r'price:\[(\d+),(\d+)\]', block)
    if price_match:
        customer["price"] = [int(price_match.group(1)), int(price_match.group(2))]
    
    # enduranceLimit
    el_match = re.search(r'enduranceLimit:([\d.]+)', block)
    if el_match:
        customer["enduranceLimit"] = float(el_match.group(1))
    
    # positiveTags
    pt_match = re.search(r'positiveTags:\[(.*?)\]', block)
    if pt_match:
        tags_str = pt_match.group(1)
        tags = []
        for t in re.finditer(r'"([^"]*)"', tags_str):
            tags.append(t.group(1))
        # Also check for r.qs.* references
        for t in re.finditer(r'r\.qs\.(\w+)', tags_str):
            tag_name = t.group(1)
            if tag_name in qs:
                tags.append(qs[tag_name])
            else:
                tags.append(f"[qs.{tag_name}]")
        customer["positiveTags"] = tags
    
    # negativeTags
    nt_match = re.search(r'negativeTags:\[(.*?)\]', block)
    if nt_match:
        tags_str = nt_match.group(1)
        tags = []
        for t in re.finditer(r'"([^"]*)"', tags_str):
            tags.append(t.group(1))
        for t in re.finditer(r'r\.qs\.(\w+)', tags_str):
            tag_name = t.group(1)
            if tag_name in qs:
                tags.append(qs[tag_name])
            else:
                tags.append(f"[qs.{tag_name}]")
        customer["negativeTags"] = tags
    
    # beverageTags
    bt_match = re.search(r'beverageTags:\[(.*?)\]', block)
    if bt_match:
        tags_str = bt_match.group(1)
        tags = []
        for t in re.finditer(r'"([^"]*)"', tags_str):
            tags.append(t.group(1))
        for t in re.finditer(r'r\.qs\.(\w+)', tags_str):
            tag_name = t.group(1)
            if tag_name in qs:
                tags.append(qs[tag_name])
            else:
                tags.append(f"[qs.{tag_name}]")
        customer["beverageTags"] = tags
    
    # spellCards
    sc_match = re.search(r'spellCards:\{(.*?)\}(?=,beverageTagMapping|,positiveTagMapping|,collection|\})', block, re.DOTALL)
    if sc_match:
        sc_text = sc_match.group(0)
        customer["spellCards_raw"] = sc_text[:200]
    
    # positiveTagMapping
    ptm_match = re.search(r'positiveTagMapping:\{(.*?)\}', block)
    if ptm_match:
        customer["positiveTagMapping"] = ptm_match.group(1)
    
    # beverageTagMapping
    btm_match = re.search(r'beverageTagMapping:\{(.*?)\}', block)
    if btm_match:
        customer["beverageTagMapping"] = btm_match.group(1)
    
    # collection
    coll_match = re.search(r'collection:(!0|!1|true|false)', block)
    if coll_match:
        customer["collection"] = coll_match.group(1) in ("!0", "true")
    
    customers.append(customer)

# Save
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(customers, f, ensure_ascii=False, indent=2)

print(f"\nExtracted {len(customers)} customers:")
for c in customers:
    print(f"  id={c['id']} name={c['name']} dlc={c.get('dlc','?')} positiveTags={c.get('positiveTags',[])} negativeTags={c.get('negativeTags',[])} beverageTags={c.get('beverageTags',[])} enduranceLimit={c.get('enduranceLimit','?')}")