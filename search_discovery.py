import re

filepath = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\discovery_output.txt"
output = r"d:\new\MystiaRecommendation\discovery_summary.txt"

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

keywords = ["GuestsManager", "CookSystem", "RunTimePlayer", "SpecialGuest", "Recipe", "Beverage", "Ingredient", "Cooker", "Spell", "Tag", "Rating", "Order", "Meal", "Inventory", "Popular", "Trend", "Famous", "Bond", "Kizuna"]

with open(output, 'w', encoding='utf-8') as out:
    for kw in keywords:
        # Find all lines containing keyword
        lines = [l.strip() for l in content.split('\n') if kw in l]
        if lines:
            out.write(f"\n=== '{kw}' ({len(lines)} lines) ===\n")
            for l in lines[:30]:
                out.write(f"  {l}\n")
            if len(lines) > 30:
                out.write(f"  ... and {len(lines)-30} more\n")

    # Also find the "与稀客/料理/酒水/食材/厨具相关的方法" section
    idx = content.find("与稀客/料理/酒水/食材/厨具相关的方法")
    if idx >= 0:
        section = content[idx:idx+20000]
        out.write(f"\n\n=== 游戏相关方法 (前500行) ===\n")
        lines = section.split('\n')
        for l in lines[:500]:
            out.write(l + '\n')

print("Done.")