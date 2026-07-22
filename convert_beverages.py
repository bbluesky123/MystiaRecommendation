import re
import json

with open(r"D:\new\MystiaRecommendation\Data\data.ts", 'r', encoding='utf-8') as f:
    content = f.read()

# Find the start of the array
array_start = content.index('BEVERAGE_LIST = [')
content = content[array_start:]

# Split by beverage entries - each starts with \t\t{ at the beginning of a line
entries = []
current = ""
in_entry = False
depth = 0

lines = content.split('\n')
for line in lines:
    stripped = line.strip()
    if stripped == '{' and not in_entry:
        in_entry = True
        current = '{'
        depth = 1
    elif in_entry:
        current += '\n' + line
        # Track brace depth to handle nested objects like 'from'
        for ch in stripped:
            if ch == '{': depth += 1
            elif ch == '}': depth -= 1
        if depth == 0 and (stripped == '},' or stripped == '}'):
            entries.append(current)
            current = ""
            in_entry = False

# Parse each entry
result = []
for entry in entries:
    bev = {}

    # id
    m = re.search(r'id:\s*(\d+)', entry)
    if m: bev['id'] = int(m.group(1))

    # name
    m = re.search(r"name:\s*'([^']*)'", entry)
    if m: bev['name'] = m.group(1)

    # price
    m = re.search(r'price:\s*(\d+)', entry)
    if m: bev['price'] = int(m.group(1))

    # dlc
    m = re.search(r'dlc:\s*(\d+)', entry)
    if m: bev['dlc'] = int(m.group(1))

    # tags - handle multiline
    tags_match = re.search(r'tags:\s*\[(.*?)\]', entry, re.DOTALL)
    if tags_match:
        tags_str = tags_match.group(1)
        tags = re.findall(r"'([^']*)'", tags_str)
        bev['tags'] = tags
    else:
        bev['tags'] = []

    if bev.get('name'):
        result.append(bev)

print(f"Parsed {len(result)} beverages")

with open(r"D:\new\MystiaRecommendation\Data\beverages.json", 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False, indent=2)

# Check for 大吟酿
for b in result:
    if '大吟酿' in b.get('name', ''):
        print(f"Found: {b['name']} id={b['id']} price={b['price']} tags={b['tags']}")
