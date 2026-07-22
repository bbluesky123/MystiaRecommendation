import subprocess
import os

dll_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\interop\Assembly-CSharp.dll"
output_path = r"D:\new\MystiaRecommendation\assembly_analysis.txt"

# Use python to read the DLL and search for strings
with open(output_path, 'w', encoding='utf-8') as out:
    with open(dll_path, 'rb') as f:
        data = f.read()
    
    out.write(f"Assembly-CSharp.dll size: {len(data)} bytes\n\n")
    
    # Search for Chinese keywords that indicate game systems
    keywords = [
        "稀客", "普客", "顾客", "料理", "食谱", "酒水", "食材",
        "厨具", "摆件", "衣服", "伙伴", "标签", "评级", "套餐",
        "夜雀", "明星店", "流行趋势", "符卡", "羁绊", "背包",
        "Budget", "Rating", "Customer", "Recipe", "Beverage",
        "Ingredient", "Cooker", "Meal", "SpellCard", "Bond",
        "Tag", "Trend", "Popular", "Inventory", "GameController",
        "GameManager", "OnArrive", "OnEnter", "OnSpawn", "OnLeave",
        "OnExit", "OnDestroy", "CookingSystem", "CookingStation",
    ]
    
    out.write("=== Keywords found in Assembly-CSharp.dll ===\n")
    for kw in keywords:
        encoded = kw.encode('utf-8')
        count = data.count(encoded)
        if count > 0:
            # Find first occurrence and get surrounding context
            pos = data.find(encoded)
            # Get some bytes around it for context
            start = max(0, pos - 20)
            end = min(len(data), pos + len(encoded) + 60)
            context = data[start:end]
            # Try to extract readable text
            readable = ""
            for b in context:
                if 32 <= b < 127 or b >= 0xc0:
                    readable += chr(b) if b < 128 else f"\\x{b:02x}"
                else:
                    readable += "."
            out.write(f"  {kw}: {count} times (first at offset {pos})\n")
    
    # Try to find class names by searching for common patterns
    # In .NET assemblies, class names are stored as UTF-8/UTF-16 strings
    out.write("\n=== Searching for class-like patterns ===\n")
    
    # Search for strings that look like class names (CamelCase, reasonable length)
    import re
    
    # Extract UTF-16LE strings (common in .NET assemblies)
    utf16_pattern = rb'(?:[\x20-\x7e]\x00){5,}'
    matches = list(re.finditer(utf16_pattern, data))
    
    class_names = set()
    for m in matches:
        try:
            s = m.group().decode('utf-16-le')
            # Filter for likely class/method names
            if any(c.isupper() for c in s) and not s.startswith(' ') and len(s) < 80:
                # Check if it contains game-related keywords
                if any(kw.lower() in s.lower() for kw in ['customer', 'recipe', 'beverage', 'ingredient', 'cooker', 'meal', 'tag', 'spell', 'bond', 'rating', 'inventory', 'game', 'controller', 'manager', 'system', 'data', 'info', 'rare', 'normal', 'trend', 'popular']):
                    class_names.add(s.strip())
        except:
            pass
    
    out.write(f"\nGame-related class/method names ({len(class_names)}):\n")
    for name in sorted(class_names):
        out.write(f"  {name}\n")

print("Done. Check assembly_analysis.txt")