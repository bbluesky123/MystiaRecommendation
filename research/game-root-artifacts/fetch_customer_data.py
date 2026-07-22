import requests
import json
import re
import os

# Step 1: Try to fetch RSC data from specific customer pages
# The site uses Next.js RSC, so visiting a customer page may embed data
customers = [
    "莉格露", "露米娅", "橙", "稗田阿求", "上白泽慧音",
    "茨木华扇", "博丽灵梦", "伊吹萃香", "比那名居天子", "雾雨魔理沙",
    "藤原妹红", "魂魄妖梦", "西行寺幽幽子", "八意永琳", "蓬莱山辉夜",
    "因幡帝", "铃仙", "稗田阿求", "爱丽丝", "帕秋莉",
    "红美铃", "十六夜咲夜", "蕾米莉亚", "芙兰朵露"
]

print("=== Fetching individual customer pages ===")
for cname in customers[:3]:  # test with first 3
    try:
        url = f"https://izakaya.cc/customer-rare/{cname}"
        resp = requests.get(url, timeout=15, headers={
            "User-Agent": "Mozilla/5.0",
            "Accept": "text/html"
        })
        print(f"Customer: {cname}, Status: {resp.status_code}, Length: {len(resp.text)}")
        # Save first one for analysis
        if resp.status_code == 200 and cname == customers[0]:
            with open(f"customer_page_{cname}.html", "w", encoding="utf-8") as f:
                f.write(resp.text)
            print(f"  Saved customer_page_{cname}.html")
    except Exception as e:
        print(f"Customer: {cname}, Error: {e}")

# Step 2: Try RSC flight format
print("\n=== Trying RSC format ===")
try:
    url = "https://izakaya.cc/customer-rare?_rsc=1"
    resp = requests.get(url, timeout=15, headers={
        "User-Agent": "Mozilla/5.0",
        "RSC": "1",
        "Next-Router-State-Tree": "%5B%22%22%2C%7B%22children%22%3A%5B%22(pages)%22%2C%7B%22children%22%3A%5B%22customer-rare%22%2C%7B%22children%22%3A%5B%5B%22paths%22%2C%22%22%2C%22oc%22%5D%2C%7B%22children%22%3A%5B%22__PAGE__%22%2C%7B%7D%5D%7D%5D%7D%5D%7D%5D%7D%5D"
    })
    print(f"RSC Status: {resp.status_code}, Length: {len(resp.text)}")
    if resp.status_code == 200:
        with open("rsc_data.txt", "w", encoding="utf-8") as f:
            f.write(resp.text[:50000])
        print("Saved rsc_data.txt (first 50000 chars)")
except Exception as e:
    print(f"RSC Error: {e}")

# Step 3: Try to find the data JS chunks
print("\n=== Looking for data chunks ===")
# From the HTML, we know the page loads various chunks
# Let's look for the main data chunk
data_chunk_urls = [
    "https://izakaya.cc/_next/static/chunks/7337-78bb669697bef373.js",  # 73337 -> A.xN etc
    "https://izakaya.cc/_next/static/chunks/13599-5374-9f43fd923e26267e.js",
]

# Actually, let me look at the build manifest
try:
    # Next.js build manifest
    resp = requests.get("https://izakaya.cc/_next/static/7b29c9e/_buildManifest.js", timeout=10)
    print(f"BuildManifest Status: {resp.status_code}, Length: {len(resp.text) if resp.status_code == 200 else 0}")
    if resp.status_code == 200:
        print(resp.text[:2000])
except Exception as e:
    print(f"BuildManifest Error: {e}")