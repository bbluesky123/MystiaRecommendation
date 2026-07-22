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

# 直接搜索酒水关键词出现位置
for term in ["高酒精","烧酒","鸡尾酒","可加冰","无酒精"]:
    indices = [m.start() for m in re.finditer(f'"{term}"', content)]
    print(f"'{term}': {len(indices)} occurrences", flush=True)
    if indices:
        idx = indices[0]
        ctx = content[max(0,idx-600):idx+200]
        print(f"  Context: ...{ctx}...", flush=True)
        break

# 酒水可能用不同的字段名，如 tags 而非 positiveTags
# 搜索 id+name+tags 模式（无 positiveTags）
tag_pattern = r'\{id:(\d+),name:"([^"]+)",tags:\[(.*?)\],price:(\d+)(?:,dlc:(\d+))?\}'
tag_matches = list(re.finditer(tag_pattern, content))
print(f"\nid+name+tags+price: {len(tag_matches)} matches", flush=True)
if tag_matches:
    for m in tag_matches[:5]:
        tags = extract_tags(m.group(3))
        print(f"  {m.group(2)}: {tags}", flush=True)

# 搜索含酒精标签的完整数据块
# 酒水数据可能在一个大的数组中
# 搜索 id+name 后面跟着 tags 包含酒精标签的
for m in re.finditer(r'\{id:(\d+),name:"([^"]+)"', content):
    start = m.start()
    # 检查后面 300 字符内是否有酒精标签
    snippet = content[start:start+300]
    if any(t in snippet for t in ["高酒精","低酒精","中酒精","烧酒","清酒","鸡尾酒","啤酒","西洋酒","利口酒","水果","辛","苦","甘","气泡","提神","直饮","可加冰","可加热"]):
        print(f"\nBeverage candidate at {start}:", flush=True)
        print(snippet[:300], flush=True)
        # 尝试提取完整对象
        obj_match = re.search(r'\{id:(\d+),name:"([^"]+)",tags:\[(.*?)\],price:(\d+)(?:,dlc:(\d+))?\}', content[start:start+500])
        if obj_match:
            print(f"  Extracted: {obj_match.group(2)}", flush=True)
        break

print("\nDone.", flush=True)