import urllib.request
import json
import re
import os

url = 'https://izakaya.cc/customer-rare'
req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
resp = urllib.request.urlopen(req, timeout=15)
html = resp.read().decode('utf-8')

# Save HTML
out_dir = r'd:\steam\steamapps\common\Touhou Mystia Izakaya'
with open(os.path.join(out_dir, 'customer_rare.html'), 'w', encoding='utf-8') as f:
    f.write(html)

print(f"HTML saved: {len(html)} bytes")

# Look for __NEXT_DATA__ or embedded JSON
next_data = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', html, re.DOTALL)
if next_data:
    data = json.loads(next_data.group(1))
    with open(os.path.join(out_dir, 'next_data.json'), 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print("Found __NEXT_DATA__, saved to next_data.json")
else:
    print("No __NEXT_DATA__ found")

# Look for any JSON in scripts with customer data patterns
scripts = re.findall(r'<script[^>]*>(.*?)</script>', html, re.DOTALL)
for i, s in enumerate(scripts):
    if len(s) > 100 and ('Tag' in s or 'tag' in s or 'name' in s):
        print(f"Script {i}: {len(s)} chars, preview: {s[:200]}")