import requests
import json
import re
import os

output_dir = r"D:\new\MystiaRecommendation\Data"
qs = {"signature":"招牌","popularPositive":"流行喜爱","popularNegative":"流行厌恶","expensive":"昂贵","economical":"实惠","largePartition":"大份"}
def resolve_qs(val): return qs.get(val, val)
def extract_tags(raw): return [resolve_qs(t) for t in re.findall(r'"([^"]*)"', raw)]

chunk_url = "https://izakaya.cc/_next/static/chunks/3599-08f118c8ea083da0.js"
content = requests.get(chunk_url, timeout=30, headers={"User-Agent": "Mozilla/5.0"}).text

# 酒水格式: {id:0,name:"绿茶",description:"...",tags:["无酒精"],dlc:0,level:1,price:1,from:{self:!0}}
bev_pattern = r'\{id:(\d+),name:"([^"]+)",description:"[^"]*",tags:\[(.*?)\],dlc:(\d+),level:(\d+),price:(\d+),from:\{'
matches = list(re.finditer(bev_pattern, content))
print(f"Found {len(matches)} beverages", flush=True)

beverages = []
for m in matches:
    b = {
        "id": int(m.group(1)),
        "name": m.group(2),
        "tags": extract_tags(m.group(3)),
        "dlc": int(m.group(4)),
        "level": int(m.group(5)),
        "price": int(m.group(6)),
    }
    beverages.append(b)

if beverages:
    with open(os.path.join(output_dir, "beverages.json"), "w", encoding="utf-8") as f:
        json.dump(beverages, f, ensure_ascii=False, indent=2)
    print(f"Saved {len(beverages)} beverages", flush=True)
    print(f"First 5: {[b['name'] for b in beverages[:5]]}", flush=True)
    print(f"Last 5: {[b['name'] for b in beverages[-5:]]}", flush=True)
    all_tags = set()
    for b in beverages:
        all_tags.update(b["tags"])
    print(f"Unique bev tags ({len(all_tags)}): {sorted(all_tags)}", flush=True)

print("\nDone.", flush=True)