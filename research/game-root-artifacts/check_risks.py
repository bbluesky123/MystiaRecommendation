import struct
import os
import subprocess

game_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\risk_assessment.txt"

with open(output, 'w', encoding='utf-8') as out:
    # 1. Check IL2CPP metadata version
    meta_path = os.path.join(game_dir, "il2cpp_data", "Metadata", "global-metadata.dat")
    with open(meta_path, 'rb') as f:
        magic = f.read(4)
        version = struct.unpack('<I', f.read(4))[0]
    
    out.write("=== 1. IL2CPP Metadata ===\n")
    out.write(f"  Magic: {magic}\n")
    out.write(f"  Version: {version}\n")
    # Supported versions: 24.x (Unity 2019-2020), 27.x (Unity 2021), 29.x (Unity 2022+)
    if version >= 24 and version <= 31:
        out.write(f"  -> il2cppdumper supports this version: YES\n")
    else:
        out.write(f"  -> il2cppdumper may not support this version\n")
    
    # 2. Check Unity version from globalgamemanagers
    out.write("\n=== 2. Unity Version ===\n")
    ggm_path = os.path.join(game_dir, "globalgamemanagers")
    with open(ggm_path, 'rb') as f:
        data = f.read(1000)
        # Search for Unity version string
        idx = data.find(b'Unity')
        if idx >= 0:
            # Read surrounding bytes
            chunk = data[idx:idx+50]
            # Try to decode version
            for encoding in ['utf-8', 'utf-16-le']:
                try:
                    s = chunk.decode(encoding).rstrip('\x00')
                    out.write(f"  Found: {s}\n")
                    break
                except:
                    pass
    
    # Also check app.info
    app_info_path = os.path.join(game_dir, "app.info")
    if os.path.exists(app_info_path):
        with open(app_info_path, 'rb') as f:
            content = f.read()
            out.write(f"  app.info: {content}\n")
    
    # 3. Check GameAssembly.dll exports (for BepInEx compatibility)
    out.write("\n=== 3. GameAssembly.dll ===\n")
    ga_path = os.path.join(os.path.dirname(game_dir), "GameAssembly.dll")
    fsize = os.path.getsize(ga_path)
    out.write(f"  Size: {fsize/(1024*1024):.1f}MB\n")
    
    # Check for il2cpp exports
    with open(ga_path, 'rb') as f:
        ga_data = f.read()
        exports = [b'il2cpp_init', b'il2cpp_domain_get', b'il2cpp_class_from_name', 
                   b'il2cpp_method_get_name', b'il2cpp_resolve_icall',
                   b'GameAssemblyUsageId', b'UnityBleedingEdge']
        
        for exp in exports:
            if exp in ga_data:
                out.write(f"  Found export: {exp.decode()}\n")
            else:
                out.write(f"  NOT found: {exp.decode()}\n")
    
    # 4. Check for key class names in metadata
    out.write("\n=== 4. Key Class Names in Metadata ===\n")
    with open(meta_path, 'rb') as f:
        meta_data = f.read()
    
    key_classes = [
        "CustomerRare", "CustomerNormal", "CustomerManager",
        "RecipeManager", "RecipeData", "RecipeInfo",
        "BeverageData", "BeverageInfo",
        "PlayerInventory", "InventoryManager",
        "IngredientData", "IngredientInfo", 
        "CookerData", "CookerInfo",
        "GameController", "GameManager",
        "CookingStation", "CookingSystem",
        "MealSystem", "MealResult",
        "PopularTrend", "TrendManager",
        "SpellCard", "BondReward",
        "DLCManager", "ContentManager",
        "TagData", "TagInfo", "FoodTag",
        "BeverageTag", "CookerTag",
        "NightingaleCooker",  # 夜雀厨具
    ]
    
    for cls in key_classes:
        encoded = cls.encode('utf-8')
        if encoded in meta_data:
            out.write(f"  FOUND: {cls}\n")
        else:
            out.write(f"  not found: {cls}\n")
    
    # 5. Search for Chinese class/method names that might be used
    out.write("\n=== 5. Chinese Strings in Metadata (game-specific) ===\n")
    chinese_keys = [
        "稀客", "普客", "顾客", "料理", "食谱", "酒水", "食材", 
        "厨具", "摆件", "衣服", "伙伴", "标签", "评级", "套餐",
        "正面标签", "负面标签", "酒水标签", "夜雀", "明星店",
        "流行趋势", "符卡", "羁绊",
    ]
    
    for key in chinese_keys:
        count = meta_data.count(key.encode('utf-8'))
        if count > 0:
            out.write(f"  '{key}': {count} occurrences\n")
    
    # 6. Check DLL assemblies for game code
    out.write("\n=== 6. Game Script DLLs ===\n")
    scripting_path = os.path.join(game_dir, "ScriptingAssemblies.json")
    import json
    with open(scripting_path, 'r', encoding='utf-8') as f:
        scripts = json.load(f)
    
    game_dlls = [n for n in scripts['names'] if not n.startswith('UnityEngine') and not n.startswith('Unity.')]
    out.write(f"  Game-specific DLLs ({len(game_dlls)}):\n")
    for dll in game_dlls:
        out.write(f"    {dll}\n")

print("Done. Check risk_assessment.txt")