"""从 ingredientsdata.ts 提取食材数据为 JSON（含 id，处理 DYNAMIC_TAG_MAP 引用）"""
import json
import re

# DYNAMIC_TAG_MAP 引用 → 实际标签名
DYNAMIC_TAG_MAP = {
    'DYNAMIC_TAG_MAP.signature': '招牌',
    'DYNAMIC_TAG_MAP.expensive': '昂贵',
    'DYNAMIC_TAG_MAP.economical': '实惠',
    'DYNAMIC_TAG_MAP.largePartition': '大份',
}

with open("Data/ingredientsdata.ts", "r", encoding="utf-8") as f:
    content = f.read()

ingredients = []

# 匹配每个食材对象
pattern = r"\{\s*id:\s*(-?\d+).*?name:\s*'([^']*)'.*?tags:\s*\[(.*?)\].*?price:\s*(-?\d+)"
matches = re.findall(pattern, content, re.DOTALL)

for match in matches:
    ing_id = int(match[0])
    name = match[1]
    tags_str = match[2]
    price = int(match[3])

    # 解析 tags：先取字符串字面量，再替换 DYNAMIC_TAG_MAP 引用
    raw_tags = re.findall(r"'([^']*)'|(DYNAMIC_TAG_MAP\.\w+)", tags_str)
    tags = []
    for t in raw_tags:
        str_tag, dyn_tag = t
        if str_tag:
            tags.append(str_tag)
        elif dyn_tag:
            resolved = DYNAMIC_TAG_MAP.get(dyn_tag, dyn_tag)
            tags.append(resolved)

    ingredients.append({
        "id": ing_id,
        "name": name,
        "tags": tags,
        "price": price
    })

with open("Data/ingredients.json", "w", encoding="utf-8") as f:
    json.dump(ingredients, f, ensure_ascii=False, indent=2)

print(f"Extracted {len(ingredients)} ingredients → Data/ingredients.json")

# 统计有"招牌"标签的食材
sig = [i for i in ingredients if '招牌' in i.get('tags', [])]
print(f"  含'招牌'标签: {len(sig)} ({', '.join(i['name'] for i in sig)})")

# 统计有动态标签的
dyn = [i for i in ingredients if any(t in ['招牌','昂贵','实惠','大份'] for t in i.get('tags', []))]
print(f"  含动态标签: {len(dyn)}")
