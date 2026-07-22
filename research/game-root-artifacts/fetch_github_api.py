import requests
import json

# Use GitHub API to find data files
try:
    resp = requests.get("https://api.github.com/repos/AnYiEE/touhou-mystia-izakaya-assistant/git/trees/main?recursive=1", timeout=15)
    print(f"API Status: {resp.status_code}")
    if resp.status_code == 200:
        data = resp.json()
        tree = data.get("tree", [])
        # Filter for data files
        for item in tree:
            path = item["path"]
            if any(kw in path.lower() for kw in ["customer", "rare"]):
                print(f"  {path}")
        print("\n--- All .json and .ts data files ---")
        for item in tree:
            path = item["path"]
            if ("/data/" in path or "/constant" in path or "/config/" in path) and (path.endswith(".json") or path.endswith(".ts")):
                print(f"  {path}")
except Exception as e:
    print(f"Error: {e}")