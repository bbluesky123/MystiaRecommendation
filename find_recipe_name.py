f = open(r'd:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\discovery_output.txt', 'r', encoding='utf-8')
c = f.read()
f.close()

# Find Recipe class and its properties
in_recipe = False
count = 0
for line in c.split('\n'):
    if 'GameData.Core.Collections.Recipe' in line and '类型:' in line:
        in_recipe = True
        count = 0
    if in_recipe:
        print(line.rstrip())
        count += 1
        if count > 80:
            in_recipe = False
            print("---")

# Find Sellable class
print("\n\n=== SELLABLE ===")
in_sellable = False
count = 0
for line in c.split('\n'):
    if 'GameData.Core.Collections.Sellable' in line and '类型:' in line:
        in_sellable = True
        count = 0
    if in_sellable:
        print(line.rstrip())
        count += 1
        if count > 60:
            in_sellable = False
            break