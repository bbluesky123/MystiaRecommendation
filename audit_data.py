"""审查转换后的 JSON 文件"""
import json, re

# 检查 recipes.json
with open("Data/recipes.json", "r", encoding="utf-8") as f:
    recipes = json.load(f)

with open("Data/data (1).ts", "r", encoding="utf-8") as f:
    ts_content = f.read()

print("=== recipes.json 审查 ===")
print(f"总料理数: {len(recipes)}")

# 统计各种动态标签的分布
for tag in ['招牌', '昂贵', '实惠', '大份']:
    in_pos = sum(1 for r in recipes if tag in r.get('positiveTags', []))
    in_neg = sum(1 for r in recipes if tag in r.get('negativeTags', []))
    print(f"  {tag}: positiveTags中有={in_pos}, negativeTags中有={in_neg}")

# 检查是否有残留的 DYNAMIC_TAG_MAP 引用（未解析）
for r in recipes:
    for t in r.get('positiveTags', []) + r.get('negativeTags', []):
        if 'DYNAMIC_TAG_MAP' in t or 'DARK_MATTER' in t:
            print(f"  ⚠ 未解析引用: {r['name']} tags包含 {t}")

# 检查 TS 中有多少 recipe 条目
ts_entries = len(re.findall(r"^\t\tid:", ts_content, re.MULTILINE))
print(f"TS中料理条目: {ts_entries}")
print(f"JSON中料理条目: {len(recipes)} (差: {ts_entries - len(recipes)})")

# === 审查 ingredients.json ===
with open("Data/ingredients.json", "r", encoding="utf-8") as f:
    ingredients = json.load(f)

with open("Data/ingredientsdata.ts", "r", encoding="utf-8") as f:
    ing_ts = f.read()

print("\n=== ingredients.json 审查 ===")
print(f"总食材数: {len(ingredients)}")

for tag in ['招牌', '昂贵', '实惠', '大份']:
    count = sum(1 for i in ingredients if tag in i.get('tags', []))
    print(f"  {tag}: {count}")

# 未解析引用检查
for i in ingredients:
    for t in i.get('tags', []):
        if 'DYNAMIC_TAG_MAP' in t:
            print(f"  ⚠ 未解析引用: {i['name']} tags包含 {t}")

ts_ing_entries = len(re.findall(r"^\t\tid:", ing_ts, re.MULTILINE))
print(f"TS中食材条目: {ts_ing_entries}")
print(f"JSON中食材条目: {len(ingredients)} (差: {ts_ing_entries - len(ingredients)})")

# 检查 DYNAMIC_TAG_MAP.expensive 是否解析成功
for i in ingredients:
    if '昂贵' in i.get('tags', []):
        print(f"  昂贵食材: {i['name']} tags={i['tags']}")

# 列出所有含"招牌"的料理名称供验证
print("\n=== 含'招牌'的料理 ===")
for r in recipes:
    if '招牌' in r.get('positiveTags', []) + r.get('negativeTags', []):
        print(f"  {r['name']} (id={r['id']}) positiveTags={r['positiveTags']}")
