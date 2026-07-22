import requests
import json
import re

# Try to fetch the data from the izakaya.cc API
# The site is a Next.js app, let's try common API patterns
urls_to_try = [
    "https://izakaya.cc/api/customer-rare",
    "https://izakaya.cc/api/customers",
    "https://izakaya.cc/api/data",
]

for url in urls_to_try:
    try:
        resp = requests.get(url, timeout=10)
        print(f"URL: {url} - Status: {resp.status_code}")
        if resp.status_code == 200:
            print(f"Content length: {len(resp.text)}")
            print(f"First 500 chars: {resp.text[:500]}")
            print("---")
    except Exception as e:
        print(f"URL: {url} - Error: {e}")
        print("---")

# Also try to get the Next.js build manifest to find data endpoints
try:
    resp = requests.get("https://izakaya.cc/_next/static/chunks/app/(pages)/customer-rare/%5B%5B...paths%5D%5D/page-951f0aa410dc7ddb.js", timeout=10)
    print(f"\nPage JS - Status: {resp.status_code}")
    if resp.status_code == 200:
        # Save the JS file for analysis
        with open("page_chunk.js", "w", encoding="utf-8") as f:
            f.write(resp.text)
        print(f"Saved page_chunk.js, length: {len(resp.text)}")
except Exception as e:
    print(f"Page JS Error: {e}")