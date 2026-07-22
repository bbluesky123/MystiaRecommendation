import requests
import json
import re
import os

output_dir = r"D:\new\MystiaRecommendation\Data"
qs = {"signature":"招牌","popularPositive":"流行喜爱","popularNegative":"流行厌恶","expensive":"昂贵","economical":"实惠","largePartition":"大份"}
def resolve_qs(val): return qs.get(val, val)
def extract_tags(raw): return [resolve_qs(t) for t in re.findall(r'"([^"]*)"', raw)]

# 下载酒水页面专属 chunk
bev_page_url = "https://izakaya.cc/_next/static/chunks/app/(pages)/beverages/page-379296bff16ec9bc.js"
print("Downloading beverage page chunk...", flush=True)
resp = requests.get(bev_page_url, timeout=30, headers={"User-Agent": "Mozilla/5.0"})
if resp.status_code != 200:
    print(f"Failed: {resp.status_code}", flush=True)
    exit(1)
content = resp.text
print(f"Downloaded: {len(content)} chars", flush=True)

# 搜索酒水数据
# 先看内容结构
for term in ["清酒", "烧酒", "鸡尾酒", "啤酒", "可加冰", "无酒精"]:
    idx = content.find(f'"{term}"')
    if idx >= 0:
        print(f"\nFound '{term}' at {idx}:", flush=True)
        ctx = content[max(0,idx-300):idx+100]
        print(ctx[:400], flush=True)
        break

# 尝试提取
bev_pattern = r'\{id:(\d+),name:"([^"]+)",positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],price:(\d+),dlc:(\d+)\}'
matches = list(re.finditer(bev_pattern, content))
print(f"\nPattern1: {len(matches)} matches", flush=True)

if not matches:
    bev_pattern2 = r'\{id:(\d+),name:"([^"]+)",positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],price:(\d+)\}'
    matches = list(re.finditer(bev_pattern2, content))
    print(f"Pattern2: {len(matches)} matches", flush=True)

if not matches:
    # 尝试更宽松的模式
    bev_pattern3 = r'\{id:(\d+),name:"([^"]+)",(\w+):\[(.*?)\](?:,(\w+):\[(.*?)\])?(?:,price:(\d+))?\}'
    all_matches = list(re.finditer(bev_pattern3, content))
    print(f"Pattern3 (loose): {len(all_matches)} matches", flush=True)
    if all_matches:
        for m in all_matches[:5]:
            tags = extract_tags(m.group(4))
            print(f"  {m.group(2)}: tags={tags}, price={m.group(6) or 'N/A'}", flush=True)

beverages = []
for m in matches:
    b = {
        "id": int(m.group(1)),
        "name": m.group(2),
        "positiveTags": extract_tags(m.group(3)),
        "negativeTags": extract_tags(m.group(4)),
        "price": int(m.group(5)),
    }
    if m.lastindex >= 6 and m.group(6):
        b["dlc"] = int(m.group(6))
    beverages.append(b)

if beverages:
    with open(os.path.join(output_dir, "beverages.json"), "w", encoding="utf-8") as f:
        json.dump(beverages, f, ensure_ascii=False, indent=2)
    print(f"\nSaved {len(beverages)} beverages", flush=True)
    print(f"First 5: {[b['name'] for b in beverages[:5]]}", flush=True)
    print(f"Last 5: {[b['name'] for b in beverages[-5:]]}", flush=True)
    all_tags = set()
    for b in beverages:
        all_tags.update(b["positiveTags"])
        all_tags.update(b["negativeTags"])
    print(f"Unique tags ({len(all_tags)}): {sorted(all_tags)}", flush=True)
else:
    print("No beverages found with patterns", flush=True)

print("\nDone.", flush=True)