import requests
import json

# The website source is at https://github.com/AnYiEE/touhou-mystia-izakaya-assistant
# Try to get the customer rare data files from GitHub raw

base_url = "https://raw.githubusercontent.com/AnYiEE/touhou-mystia-izakaya-assistant/main"

# Try common data file paths
paths_to_try = [
    "/src/data/customer-rare.json",
    "/src/data/customers.json", 
    "/src/data/customer.json",
    "/data/customer-rare.json",
    "/data/customers.json",
    "/public/data/customer-rare.json",
    "/src/constants/customer.ts",
    "/src/data/customer.ts",
    "/src/data/customer-rare.ts",
    "/src/config/customer.ts",
]

for path in paths_to_try:
    url = base_url + path
    try:
        resp = requests.get(url, timeout=10)
        print(f"{path}: {resp.status_code} ({len(resp.text)} bytes)")
        if resp.status_code == 200 and len(resp.text) > 100:
            fname = path.split("/")[-1]
            with open(f"github_{fname}", "w", encoding="utf-8") as f:
                f.write(resp.text)
            print(f"  Saved as github_{fname}")
            print(f"  Preview: {resp.text[:300]}")
    except Exception as e:
        print(f"{path}: Error - {e}")