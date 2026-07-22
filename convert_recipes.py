"""从 data (1).ts 提取料理数据为 JSON（处理 DYNAMIC_TAG_MAP 引用）"""
import json
import re

# DYNAMIC_TAG_MAP 引用 → 实际标签名
DYNAMIC_TAG_MAP = {
    'DYNAMIC_TAG_MAP.signature': '招牌',
    'DYNAMIC_TAG_MAP.expensive': '昂贵',
    'DYNAMIC_TAG_MAP.economical': '实惠',
    'DYNAMIC_TAG_MAP.largePartition': '大份',
}

with open("Data/data (1).ts", "r", encoding="utf-8") as f:
    content = f.read()

# Find array start
array_start = content.index('RECIPE_LIST = [')
content = content[array_start:]

# Split entries by brace depth tracking
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
        for ch in stripped:
            if ch == '{': depth += 1
            elif ch == '}': depth -= 1
        if depth == 0 and (stripped == '},' or stripped == '}'):
            entries.append(current)
            current = ""
            in_entry = False

def extract_int(pattern, text, default=0):
    m = re.search(pattern, text)
    return int(m.group(1)) if m else default

def extract_str(pattern, text, default=''):
    m = re.search(pattern, text)
    return m.group(1) if m else default

def extract_tags(pattern, text):
    """从 tags: [...] 中提取标签列表，处理字符串和 DYNAMIC_TAG_MAP 引用"""
    m = re.search(pattern, text, re.DOTALL)
    if not m:
        return []
    tags_str = m.group(1)
    # 匹配字符串字面量 'xxx' 或 DYNAMIC_TAG_MAP.xxx 或 DARK_MATTER_META_MAP.xxx
    raw = re.findall(r"'([^']*)'|(DYNAMIC_TAG_MAP\.\w+)|(DARK_MATTER_META_MAP\.\w+)", tags_str)
    tags = []
    for t in raw:
        str_tag, dyn_tag, dark_tag = t
        if str_tag:
            tags.append(str_tag)
        elif dyn_tag:
            resolved = DYNAMIC_TAG_MAP.get(dyn_tag, dyn_tag)
            tags.append(resolved)
        elif dark_tag:
            # DARK_MATTER_META_MAP 引用 → 跳过，暗黑物质已在原JSON中
            pass
    return tags

def extract_list(pattern, text):
    """从 ingredients: [...] 中提取字符串列表"""
    m = re.search(pattern, text, re.DOTALL)
    if not m:
        return []
    return re.findall(r"'([^']*)'", m.group(1))

recipes = []
for entry in entries:
    # 跳过暗黑物质特殊条目
    if 'DARK_MATTER_META_MAP' in entry:
        continue

    recipe = {
        "id": extract_int(r'id:\s*(\d+)', entry),
        "recipeId": extract_int(r'recipeId:\s*(\d+)', entry),
        "name": extract_str(r"name:\s*'([^']*)'", entry),
        "ingredients": extract_list(r'ingredients:\s*\[(.*?)\]', entry),
        "positiveTags": extract_tags(r'positiveTags:\s*\[(.*?)\]', entry),
        "negativeTags": extract_tags(r'negativeTags:\s*\[(.*?)\]', entry),
        "cooker": extract_str(r"cooker:\s*'([^']*)'", entry, ''),
        "baseCookTime": extract_int(r'baseCookTime:\s*(\d+)', entry),
        "dlc": extract_int(r'dlc:\s*(\d+)', entry),
        "level": extract_int(r'level:\s*(\d+)', entry),
        "price": extract_int(r'price:\s*(\d+)', entry),
    }

    if recipe["name"]:
        recipes.append(recipe)

with open("Data/recipes.json", "w", encoding="utf-8") as f:
    json.dump(recipes, f, ensure_ascii=False, indent=2)

print(f"Extracted {len(recipes)} recipes → Data/recipes.json")

# 统计有"招牌"标签的
sig = [r for r in recipes if '招牌' in r.get('positiveTags', []) or '招牌' in r.get('negativeTags', [])]
print(f"  含'招牌'标签: {len(sig)} ({', '.join(r['name'] for r in sig[:10])}{'...' if len(sig)>10 else ''})")

# 统计有动态标签的
dyn = [r for r in recipes if any(
    t in ['招牌','昂贵','实惠','大份'] for t in (r.get('positiveTags',[]) + r.get('negativeTags',[]))
)]
print(f"  含动态标签: {len(dyn)}")

# 验证 烤八目鳗
for r in recipes:
    if r['name'] == '烤八目鳗':
        print(f"  烤八目鳗: positiveTags={r['positiveTags']}")
