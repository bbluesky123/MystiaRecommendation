f = open(r'd:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\discovery_output.txt', 'r', encoding='utf-8')
c = f.read()
f.close()

# 搜索标签相关方法
keywords = ["GetFoodTag", "GetTag", "TagLang", "GetAllFood", "TagData", "TagProfile", "ReqFood", "ReqBev", "FoodTagId", "BevTagId"]
for kw in keywords:
    lines = [l.rstrip() for l in c.split('\n') if kw in l]
    if lines:
        print(f"\n=== '{kw}' ({len(lines)} lines) ===")
        for l in lines[:10]:
            print(f"  {l}")
        if len(lines) > 10:
            print(f"  ... and {len(lines)-10} more")

# 搜索 SpecialGuestsController 中包含 Req 的方法
print("\n\n=== SpecialGuestsController Req methods ===")
for l in c.split('\n'):
    if 'Req' in l and ('SpecialGuestsController' in l or 'GuestGroup' in l):
        print(l.rstrip())

# 搜索 GetEvaluat 方法
print("\n\n=== Evaluate methods ===")
for l in c.split('\n'):
    if 'Evaluate' in l and ('Int32' in l or 'StructArray' in l):
        print(l.rstrip())