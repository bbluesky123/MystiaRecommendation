log = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\BepInEx\LogOutput.log"
with open(log, 'r', encoding='utf-8', errors='ignore') as f:
    lines = f.readlines()

keywords = ["MystiaRec", "MystiaRecommendation", "Loading plugin", "Load error", "DllNotFound", "Exception", "Error loading"]
for line in lines:
    for kw in keywords:
        if kw.lower() in line.lower():
            print(line.rstrip())
            break