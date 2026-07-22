import requests
import json
import re
import os

output_dir = r"D:\new\MystiaRecommendation\Data"

qs = {"signature":"招牌","popularPositive":"流行喜爱","popularNegative":"流行厌恶","expensive":"昂贵","economical":"实惠","largePartition":"大份"}

def resolve_qs(val): return qs.get(val, val)
def extract_tags(raw): return [resolve_qs(t) for t in re.findall(r'"([^"]*)"', raw)]

# 先获取酒水页面 HTML 找到正确的 JS chunk URL
print("Fetching beverage page...", flush=True)
bev_html = requests.get("https://izakaya.cc/beverages", timeout=30, headers={"User-Agent": "Mozilla/5.0"}).text

# 从 HTML 中找到 _buildManifest 或 chunk 引用
# Next.js buildId: 找 buildId
build_id_match = re.search(r'"buildId":"([^"]+)"', bev_html)
if build_id_match:
    build_id = build_id_match.group(1)
    print(f"Build ID: {build_id}", flush=True)

# 找到所有 JS chunk 引用
chunks = set(re.findall(r'/_next/static/chunks/([^"]+\.js)', bev_html))
print(f"Found {len(chunks)} unique chunks", flush=True)

# 之前稀客页面的 chunk 是 3599，找新出现的 chunk
for c in sorted(chunks):
    if c not in ["3599-08f118c8ea083da0.js", "1136-6310b1e98abec4fa.js"]:
        print(f"  New chunk: {c}", flush=True)

# 直接搜索 _buildManifest 获取酒水页面的专属 chunk
manifest_match = re.findall(r'/_next/static/[^/]+/_buildManifest\.js', bev_html)
print(f"BuildManifest: {manifest_match}", flush=True)

# 尝试从 chunk 3599 中搜索酒水数据（同一个 data 模块）
chunk_url = "https://izakaya.cc/_next/static/chunks/3599-08f118c8ea083da0.js"
content = requests.get(chunk_url, timeout=30, headers={"User-Agent": "Mozilla/5.0"}).text

# 酒水标签特征: 含"酒精"、"清酒"、"烧酒"、"鸡尾酒"、"啤酒"等
# 搜索含有这些标签的对象
# 先搜索 chunk 中所有 id+name+tags 模式
all_obj_pattern = r'\{id:(\d+),name:"([^"]+)",(\w+):\[(.*?)\](?:,(\w+):\[(.*?)\])?,price:(\d+)(?:,dlc:(\d+))?\}'
all_matches = list(re.finditer(all_obj_pattern, content))
print(f"\nAll id+name+tags+price matches: {len(all_matches)}", flush=True)

# 分类：检查标签内容判断是料理还是酒水
bev_keywords = {"无酒精","低酒精","中酒精","高酒精","可加冰","可加热","直饮","清酒","烧酒","鸡尾酒","啤酒","水果","辛","苦","甘","气泡","提神","西洋酒","古典","现代","利口酒"}
food_keywords = {"肉","素","水产","高级","传说","家常","清淡","和风","中华","文化底蕴","汤羹","辣","甜","生","鲜"}

beverages = []
for m in all_matches:
    tags_raw = m.group(4) + "," + (m.group(6) or "")
    tags = extract_tags(tags_raw)
    bev_score = sum(1 for t in tags if t in bev_keywords)
    food_score = sum(1 for t in tags if t in food_keywords)
    
    if bev_score > food_score:
        b = {
            "id": int(m.group(1)),
            "name": m.group(2),
            "tags": tags,
            "price": int(m.group(7)),
        }
        if m.group(8): b["dlc"] = int(m.group(8))
        beverages.append(b)

print(f"Identified {len(beverages)} beverages by tag analysis", flush=True)

if not beverages:
    # 尝试直接搜索包含酒精关键词的字符串附近对象
    for term in ["无酒精", "低酒精", "高酒精", "可加冰", "清酒", "烧酒"]:
        indices = [m.start() for m in re.finditer(f'"{term}"', content)]
        if indices:
            print(f"\n'{term}' found {len(indices)} times", flush=True)
            idx = indices[0]
            context = content[max(0,idx-500):idx+500]
            print(f"Context around first: ...{context[:300]}...", flush=True)
            break

if beverages:
    with open(os.path.join(output_dir, "beverages.json"), "w", encoding="utf-8") as f:
        json.dump(beverages, f, ensure_ascii=False, indent=2)
    print(f"Saved {len(beverages)} beverages to beverages.json", flush=True)
    print(f"First 5: {[b['name'] for b in beverages[:5]]}", flush=True)
    print(f"Last 5: {[b['name'] for b in beverages[-5:]]}", flush=True)
    # 检查唯一标签
    all_bev_tags = set()
    for b in beverages:
        all_bev_tags.update(b["tags"])
    print(f"Unique bev tags ({len(all_bev_tags)}): {sorted(all_bev_tags)}", flush=True)
else:
    print("No beverages found!", flush=True)

print("\nDone.", flush=True)