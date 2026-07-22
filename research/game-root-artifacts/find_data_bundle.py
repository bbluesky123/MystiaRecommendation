import UnityPy
import os

catalog_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\Touhou Mystia Izakaya_Data\StreamingAssets\aa\catalog.bundle"

print("Loading catalog...", flush=True)
env = UnityPy.load(catalog_path)

print("Objects in catalog:", flush=True)
for obj in env.objects:
    print(f"  type={obj.type.name} pathID={obj.path_id}", flush=True)
    if obj.type.name == "TextAsset":
        data = obj.read()
        print(f"    TextAsset: {data.m_Name} ({len(data.m_Script)} bytes)", flush=True)

print("Done.", flush=True)