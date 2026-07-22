f = open(r'd:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\discovery_output.txt', 'r', encoding='utf-8')
c = f.read()
f.close()

for line in c.split('\n'):
    if 'DataBaseLanguage' in line and '类型:' in line:
        print(line.rstrip())