import requests
import json
import re
import os

# Strategy: Download the JS data chunks from izakaya.cc
# The page references module 73337 which contains the data (A.xN, A.oY, etc.)
# Let's download the relevant JS chunks

output_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya"

# From the page HTML, the chunks are:
chunks_to_try = [
    "https://izakaya.cc/_next/static/chunks/334-d3d833d54ca9baa2.js",
    "https://izakaya.cc/_next/static/chunks/5374-9f43fd923e26267e.js",  
    "https://izakaya.cc/_next/static/chunks/1705-540aa8a1aea35c10.js",
    "https://izakaya.cc/_next/static/chunks/8012-dfc1686b49a13be1.js",
    "https://izakaya.cc/_next/static/chunks/7814-5ccdc9926f3b8ad1.js",
    "https://izakaya.cc/_next/static/chunks/8891-2aeb21b4b4b66ac1.js",
    "https://izakaya.cc/_next/static/chunks/1136-6310b1e98abec4fa.js",
    # Data chunk - module 73337 contains data constants
    "https://izakaya.cc/_next/static/chunks/bfee6300-78bb669697bef373.js",
]

results = {}
for url in chunks_to_try:
    try:
        resp = requests.get(url, timeout=15, headers={"User-Agent": "Mozilla/5.0"})
        fname = url.split("/")[-1]
        if resp.status_code == 200:
            content = resp.text
            # Check if this chunk contains customer data
            has_customer = "positiveTag" in content or "beverageTag" in content or "enduranceLimit" in content
            has_names = "莉格露" in content or "博丽灵梦" in content or "雾雨魔理沙" in content
            results[fname] = {
                "size": len(content),
                "has_customer": has_customer,
                "has_names": has_names
            }
            if has_customer or has_names:
                fpath = os.path.join(output_dir, f"chunk_{fname}")
                with open(fpath, "w", encoding="utf-8") as f:
                    f.write(content)
                results[fname]["saved"] = True
                print(f"SAVED: {fname} (size={len(content)}, customer={has_customer}, names={has_names})")
            else:
                print(f"Skip: {fname} (size={len(content)})")
        else:
            print(f"Error: {fname} status={resp.status_code}")
    except Exception as e:
        print(f"Error: {url} - {e}")

# Also try to get the layout chunk which may contain data
layout_url = "https://izakaya.cc/_next/static/chunks/app/(pages)/customer-rare/layout-afec4d216a0ab04f.js"
try:
    resp = requests.get(layout_url, timeout=15, headers={"User-Agent": "Mozilla/5.0"})
    if resp.status_code == 200:
        with open(os.path.join(output_dir, "chunk_layout.js"), "w", encoding="utf-8") as f:
            f.write(resp.text)
        print(f"SAVED: layout chunk ({len(resp.text)} bytes)")
except Exception as e:
    print(f"Layout error: {e}")

print("\nSummary:")
for fname, info in results.items():
    print(f"  {fname}: {info}")