import requests
import json
import re
import os

output_dir = r"D:\new\MystiaRecommendation\Data"
chunk_url = "https://izakaya.cc/_next/static/chunks/3599-08f118c8ea083da0.js"

print("Downloading chunk 3599...", flush=True)
resp = requests.get(chunk_url, timeout=30, headers={"User-Agent": "Mozilla/5.0"})
content = resp.text
print(f"Downloaded: {len(content)} chars", flush=True)

# 料理实际格式:
# {id:23,recipeId:22,name:"刺身拼盘",description:"...",ingredients:["三文鱼","金枪鱼"],
#  positiveTags:["水产","高级","和风","生","适合拍照"],negativeTags:["灼热"],
#  cooker:"料理台",baseCookTime:5,dlc:0,level:3,price:88,from:{bond:{name:"琪露诺",level:3}}}

# qs 常量
qs = {
    "signature": "招牌",
    "popularPositive": "流行喜爱",
    "popularNegative": "流行厌恶",
    "expensive": "昂贵",
    "economical": "实惠",
    "largePartition": "大份"
}

def resolve_qs(val):
    return qs.get(val, val)

def extract_tags(raw):
    """从 raw tag string 提取并解析 qs"""
    tags = re.findall(r'"([^"]*)"', raw)
    return [resolve_qs(t) for t in tags]

# === 料理数据 ===
recipe_pattern = r'\{id:(\d+),recipeId:(\d+),name:"([^"]+)",description:"[^"]*?",ingredients:\[(.*?)\],positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],cooker:"([^"]*)",baseCookTime:(\d+),dlc:(\d+),level:(\d+),price:(\d+)'
matches = list(re.finditer(recipe_pattern, content))
print(f"Found {len(matches)} recipes", flush=True)

recipes = []
for m in matches:
    r = {
        "id": int(m.group(1)),
        "recipeId": int(m.group(2)),
        "name": m.group(3),
        "ingredients": extract_tags(m.group(4)),
        "positiveTags": extract_tags(m.group(5)),
        "negativeTags": extract_tags(m.group(6)),
        "cooker": m.group(7),
        "baseCookTime": int(m.group(8)),
        "dlc": int(m.group(9)),
        "level": int(m.group(10)),
        "price": int(m.group(11)),
    }
    recipes.append(r)

if recipes:
    with open(os.path.join(output_dir, "recipes.json"), "w", encoding="utf-8") as f:
        json.dump(recipes, f, ensure_ascii=False, indent=2)
    print(f"Saved {len(recipes)} recipes", flush=True)
    print(f"First 3: {[r['name'] for r in recipes[:3]]}", flush=True)
    print(f"Last 3: {[r['name'] for r in recipes[-3:]]}", flush=True)

# === 酒水数据 ===
# 酒水格式可能类似: {id:N,name:"名",tags:[...],price:N}
# 搜索页面 js chunk
bev_page_url = "https://izakaya.cc/_next/static/chunks/pages/beverages-a54b60bde301ab6a.js"
print(f"\nDownloading beverages page...", flush=True)
resp2 = requests.get(bev_page_url, timeout=30, headers={"User-Agent": "Mozilla/5.0"})
if resp2.status_code == 200:
    bev_content = resp2.text
    print(f"Downloaded: {len(bev_content)} chars", flush=True)
    
    # 酒水格式: {id:N,name:"名",positiveTags:[...],negativeTags:[...],price:N,dlc:N}
    bev_pattern = r'\{id:(\d+),name:"([^"]+)",positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],price:(\d+),dlc:(\d+)\}'
    bev_matches = list(re.finditer(bev_pattern, bev_content))
    print(f"Found {len(bev_matches)} beverages", flush=True)
    
    beverages = []
    for m in bev_matches:
        b = {
            "id": int(m.group(1)),
            "name": m.group(2),
            "positiveTags": extract_tags(m.group(3)),
            "negativeTags": extract_tags(m.group(4)),
            "price": int(m.group(5)),
            "dlc": int(m.group(6)),
        }
        beverages.append(b)
    
    if beverages:
        with open(os.path.join(output_dir, "beverages.json"), "w", encoding="utf-8") as f:
            json.dump(beverages, f, ensure_ascii=False, indent=2)
        print(f"Saved {len(beverages)} beverages", flush=True)
        print(f"First 3: {[b['name'] for b in beverages[:3]]}", flush=True)
    else:
        # Try without dlc
        bev_pattern2 = r'\{id:(\d+),name:"([^"]+)",positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],price:(\d+)\}'
        bev_matches2 = list(re.finditer(bev_pattern2, bev_content))
        print(f"Pattern2: {len(bev_matches2)} beverages", flush=True)
        
        for m in bev_matches2:
            b = {
                "id": int(m.group(1)),
                "name": m.group(2),
                "positiveTags": extract_tags(m.group(3)),
                "negativeTags": extract_tags(m.group(4)),
                "price": int(m.group(5)),
            }
            beverages.append(b)
        
        if beverages:
            with open(os.path.join(output_dir, "beverages.json"), "w", encoding="utf-8") as f:
                json.dump(beverages, f, ensure_ascii=False, indent=2)
            print(f"Saved {len(beverages)} beverages", flush=True)
else:
    print(f"Failed to download beverages page: {resp2.status_code}", flush=True)
    # Try from chunk 3599
    bev_pattern3 = r'\{id:(\d+),name:"([^"]+)",positiveTags:\[(.*?)\],negativeTags:\[(.*?)\],price:(\d+)(?:,dlc:(\d+))?\}'
    bev_matches3 = list(re.finditer(bev_pattern3, content))
    print(f"From chunk3599: {len(bev_matches3)} matches", flush=True)

# === 汇总 ===
all_tags = set()
for r in recipes:
    all_tags.update(r["positiveTags"])
    all_tags.update(r["negativeTags"])
print(f"\nAll unique food tags ({len(all_tags)}): {sorted(all_tags)}", flush=True)

print("\nDone.", flush=True)