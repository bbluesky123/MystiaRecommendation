import os
import re

# Search global-metadata.dat for customer-related strings
meta_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\il2cpp_data\Metadata\global-metadata.dat"
output_file = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\metadata_search.txt"

# Known rare customer names
customers = [
    "莉格露", "露米娅", "橙", "稗田阿求", "上白泽慧音",
    "茨木华扇", "博丽灵梦", "伊吹萃香", "比那名居天子", "雾雨魔理沙",
    "藤原妹红", "魂魄妖梦", "西行寺幽幽子", "八意永琳", "蓬莱山辉夜",
    "因幡帝", "铃仙", "爱丽丝", "帕秋莉", "红美铃",
    "十六夜咲夜", "蕾米莉亚", "芙兰朵露",
    # DLC characters
    "古明地觉", "古明地恋", "水桥帕露西", "星熊勇仪",
    "琪露诺", "大妖精", "蕾蒂", "琪斯美", "黑谷山女",
    "犬走椛", "射命丸文", "东风谷早苗", "洩矢诹访子",
    "河城荷取", "火焰猫燐", "灵乌路空",
    "天弓千亦", "饕餮尤魔", "日白残无", "菅牧典",
    "坂田合欢", "饭纲丸龙", "豪德寺三花",
    "萌澄果", "三月精"
]

# Search for food/cooking tags
tags = [
    "辣", "甜", "酸", "苦", "咸", "鲜", "清淡", "浓郁",
    "肉类", "海鲜", "蔬菜", "主食", "小吃", "汤品", "甜品",
    "和风", "中华", "洋风", "天界", "月都", "地狱",
    "妖怪", "人类", "魔法", "吸血鬼", "幽灵",
    "素", "炸", "烤", "煮", "蒸", "炒", "凉拌",
    "酒", "茶", "果汁", "咖啡",
    "水果", "菌菇", "水产", "豆制品"
]

with open(output_file, 'w', encoding='utf-8') as out:
    with open(meta_path, 'rb') as f:
        data = f.read()
    
    out.write(f"File size: {len(data)} bytes\n\n")
    
    # Search for customer names
    out.write("=== Customer names found ===\n")
    for name in customers:
        encoded = name.encode('utf-8')
        count = data.count(encoded)
        if count > 0:
            out.write(f"  {name}: {count} occurrences\n")
    
    # Search for tags
    out.write("\n=== Tags found ===\n")
    for tag in tags:
        encoded = tag.encode('utf-8')
        count = data.count(encoded)
        if count > 0:
            out.write(f"  {tag}: {count} occurrences\n")
    
    # Search for key English terms
    out.write("\n=== Key English terms ===\n")
    for term in [b"positiveTag", b"negativeTag", b"beverageTag", b"enduranceLimit", b"CustomerRare", b"spellCard", b"bondRecipe", b"RareCustomer"]:
        count = data.count(term)
        if count > 0:
            out.write(f"  {term.decode()}: {count} occurrences\n")

print("Done. Check metadata_search.txt")