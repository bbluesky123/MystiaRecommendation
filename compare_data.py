import json
import os
import sys

# Force UTF-8 output
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

game_dir = r'd:\steam\steamapps\common\Touhou Mystia Izakaya'

# Load game data
with open(os.path.join(game_dir, r'D:\new\MystiaRecommendation\Data\customers_rare.json'), 'r', encoding='utf-8') as f:
    game_data = json.load(f)

# Load website data
with open(os.path.join(game_dir, 'website_customers.json'), 'r', encoding='utf-8') as f:
    web_data = json.load(f)

# Index by name
game_by_name = {c['name']: c for c in game_data}
web_by_name = {c['name']: c for c in web_data}

game_names = set(game_by_name.keys())
web_names = set(web_by_name.keys())

print(f"游戏数据: {len(game_data)} 个稀客")
print(f"网站数据: {len(web_data)} 个稀客")
print()

# 1. 只在游戏中的
only_game = game_names - web_names
if only_game:
    print(f"=== 只在游戏数据中 ({len(only_game)}) ===")
    for name in sorted(only_game):
        print(f"  {name}")

# 2. 只在网站中的
only_web = web_names - game_names
if only_web:
    print(f"=== 只在网站数据中 ({len(only_web)}) ===")
    for name in sorted(only_web):
        print(f"  {name}")

# 3. 共有的，对比标签
common = game_names & web_names
print(f"\n=== 共有稀客: {len(common)} 个 ===\n")

diff_count = 0
for name in sorted(common):
    g = game_by_name[name]
    w = web_by_name[name]
    
    diffs = []
    
    # Compare positiveTags
    g_pos = set(g.get('positiveTags', []))
    w_pos = set(w.get('positiveTags', []))
    if g_pos != w_pos:
        only_in_game_pos = g_pos - w_pos
        only_in_web_pos = w_pos - g_pos
        if only_in_game_pos:
            diffs.append(f"  positiveTags 多出(游戏): {sorted(only_in_game_pos)}")
        if only_in_web_pos:
            diffs.append(f"  positiveTags 多出(网站): {sorted(only_in_web_pos)}")
    
    # Compare negativeTags
    g_neg = set(g.get('negativeTags', []))
    w_neg = set(w.get('negativeTags', []))
    if g_neg != w_neg:
        only_in_game_neg = g_neg - w_neg
        only_in_web_neg = w_neg - g_neg
        if only_in_game_neg:
            diffs.append(f"  negativeTags 多出(游戏): {sorted(only_in_game_neg)}")
        if only_in_web_neg:
            diffs.append(f"  negativeTags 多出(网站): {sorted(only_in_web_neg)}")
    
    # Compare beverageTags
    g_bev = set(g.get('beverageTags', []))
    w_bev = set(w.get('beverageTags', []))
    if g_bev != w_bev:
        only_in_game_bev = g_bev - w_bev
        only_in_web_bev = w_bev - g_bev
        if only_in_game_bev:
            diffs.append(f"  beverageTags 多出(游戏): {sorted(only_in_game_bev)}")
        if only_in_web_bev:
            diffs.append(f"  beverageTags 多出(网站): {sorted(only_in_web_bev)}")
    
    # Compare price
    g_price = g.get('price', [])
    w_price = w.get('price', [])
    if g_price != w_price:
        diffs.append(f"  price: 游戏={g_price} 网站={w_price}")
    
    # Compare enduranceLimit
    g_end = g.get('enduranceLimit')
    w_end = w.get('enduranceLimit')
    if g_end != w_end:
        diffs.append(f"  enduranceLimit: 游戏={g_end} 网站={w_end}")
    
    # Compare dlc
    g_dlc = g.get('dlc')
    w_dlc = w.get('dlc')
    if g_dlc != w_dlc:
        diffs.append(f"  dlc: 游戏={g_dlc} 网站={w_dlc}")
    
    if diffs:
        diff_count += 1
        print(f"[DIFF] {name} (id={g.get('id')}):")
        for d in diffs:
            print(d)
        print(f"  游戏 positiveTags: {sorted(g_pos)}")
        print(f"  网站 positiveTags: {sorted(w_pos)}")
        print(f"  游戏 negativeTags: {sorted(g_neg)}")
        print(f"  网站 negativeTags: {sorted(w_neg)}")
        print(f"  游戏 beverageTags: {sorted(g_bev)}")
        print(f"  网站 beverageTags: {sorted(w_bev)}")
        print()

print(f"\n=== 总结 ===")
print(f"游戏数据: {len(game_data)} 个稀客")
print(f"网站数据: {len(web_data)} 个稀客")
print(f"共有: {len(common)} 个")
print(f"有差异: {diff_count} 个")
print(f"一致: {len(common) - diff_count} 个")
if only_game:
    print(f"只在游戏中: {sorted(only_game)}")
if only_web:
    print(f"只在网站中: {sorted(only_web)}")