import re
import json
import os

chunk_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\chunk_data_3599.js"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\website_customer_data.txt"

with open(chunk_path, "r", encoding="utf-8") as f:
    content = f.read()

# Find positiveTagMapping and similar patterns
with open(output_path, "w", encoding="utf-8") as out:
    # Search for customer rare data structures
    # Look for patterns like: name:"莉格露",positiveTags:
    
    # Find all occurrences of positiveTags near customer data
    patterns = [
        "positiveTagMapping",
        "negativeTags", 
        "beverageTagMapping",
        "beverageTags",
        "enduranceLimit",
        "spellCard",
        "bondRecipe",
        "bondClothes",
        "bondCooker",
        "bondOrnaments",
        "bondPartner",
    ]
    
    for pat in patterns:
        idx = 0
        count = 0
        while True:
            idx = content.find(pat, idx)
            if idx == -1:
                break
            count += 1
            # Get surrounding context (200 chars before, 500 after)
            start = max(0, idx - 200)
            end = min(len(content), idx + 500)
            ctx = content[start:end]
            out.write(f"=== {pat} occurrence {count} at pos {idx} ===\n")
            out.write(f"...{ctx}...\n\n")
            idx += len(pat)
            if count >= 5:  # Limit to first 5 occurrences
                break
        out.write(f"Total {pat}: {count}\n\n")
    
    # Also search for customer name followed by data
    rare_customers = [
        "莉格露", "露米娅", "橙", "稗田阿求", "上白泽慧音",
        "茨木华扇", "博丽灵梦", "伊吹萃香", "比那名居天子", "雾雨魔理沙",
        "藤原妹红", "魂魄妖梦", "西行寺幽幽子", "八意永琳", "蓬莱山辉夜",
        "因幡帝", "铃仙", "爱丽丝", "帕秋莉", "红美铃",
        "十六夜咲夜", "蕾米莉亚", "芙兰朵露",
        "古明地觉", "古明地恋", "水桥帕露西", "星熊勇仪",
        "琪露诺", "大妖精", "蕾蒂", "琪斯美", "黑谷山女",
        "犬走椛", "射命丸文", "东风谷早苗", "洩矢诹访子",
        "河城荷取", "火焰猫燐", "灵乌路空",
        "天弓千亦", "饕餮尤魔", "日白残无", "菅牧典",
        "坂田合欢", "饭纲丸龙", "豪德寺三花",
        "萌澄果", "蹦蹦跳跳的三妖精"
    ]
    
    out.write("\n=== Customer names found ===\n")
    for name in rare_customers:
        count = content.count(f'"{name}"')
        if count > 0:
            out.write(f"  {name}: {count} occurrences\n")
            # Get first occurrence context
            idx = content.find(f'"{name}"')
            start = max(0, idx - 100)
            end = min(len(content), idx + 1000)
            ctx = content[start:end]
            out.write(f"  Context: ...{ctx[:800]}...\n\n")

print("Done. Check website_customer_data.txt")