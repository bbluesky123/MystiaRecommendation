f = open(r'd:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\discovery_output.txt', 'r', encoding='utf-8')
c = f.read()
f.close()

# 搜索包含 DataBaseLanguage 的所有行
for line in c.split('\n'):
    if 'DataBaseLanguage' in line:
        print(line.rstrip())