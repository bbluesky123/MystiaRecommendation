import requests
import os
import re

output_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya"

# Download ALL chunks referenced in the page and search for module 73337
# Module 73337 contains the data (A.oY, A.xN, etc.)
chunks = [
    ("bfee6300", "https://izakaya.cc/_next/static/chunks/bfee6300-78bb669697bef373.js"),
    ("c541caa6", "https://izakaya.cc/_next/static/chunks/c541caa6-049bb35c5bd01aaa.js"),
    ("6820", "https://izakaya.cc/_next/static/chunks/6820-bdcfbea1180560f5.js"),
    ("7814", "https://izakaya.cc/_next/static/chunks/7814-5ccdc9926f3b8ad1.js"),
    ("2982", "https://izakaya.cc/_next/static/chunks/2982-5f3809fc3c00d231.js"),
    ("8891", "https://izakaya.cc/_next/static/chunks/8891-2aeb21b4b4b66ac1.js"),
    ("5374", "https://izakaya.cc/_next/static/chunks/5374-9f43fd923e26267e.js"),
    ("1705", "https://izakaya.cc/_next/static/chunks/1705-540aa8a1aea35c10.js"),
    ("3599", "https://izakaya.cc/_next/static/chunks/3599-08f118c8ea083da0.js"),
    ("8012", "https://izakaya.cc/_next/static/chunks/8012-dfc1686b49a13be1.js"),
    ("334", "https://izakaya.cc/_next/static/chunks/334-d3d833d54ca9baa2.js"),
    ("page", "https://izakaya.cc/_next/static/chunks/app/(pages)/customer-rare/%5B%5B...paths%5D%5D/page-951f0aa410dc7ddb.js"),
    # Also try other chunks that might contain data
    ("13599", "https://izakaya.cc/_next/static/chunks/13599-bfee6300-78bb669697bef373.js"),
    ("73337", "https://izakaya.cc/_next/static/chunks/73337.js"),
]

with open(os.path.join(output_dir, "data_module_search.txt"), "w", encoding="utf-8") as out:
    for name, url in chunks:
        try:
            resp = requests.get(url, timeout=15, headers={"User-Agent": "Mozilla/5.0"})
            if resp.status_code == 200:
                content = resp.text
                # Search for module 73337 definition
                if "73337" in content:
                    out.write(f"=== {name} contains 73337 (size={len(content)}) ===\n")
                    # Find the module definition
                    idx = content.find("73337")
                    context = content[max(0,idx-200):idx+500]
                    out.write(f"Context: ...{context}...\n\n")
                    
                # Also search for customer data patterns
                has_data = "oY" in content and "positiveTag" not in content
                if "莉格露" in content or "博丽灵梦" in content or ("oY" in content and "label" in content and "shortLabel" in content):
                    out.write(f"=== {name} likely contains customer data (size={len(content)}) ===\n")
                    # Find oY definition
                    idx = content.find("oY")
                    if idx > 0:
                        context = content[max(0,idx-100):idx+1000]
                        out.write(f"oY context: ...{context}...\n\n")
                    fname = f"chunk_data_{name}.js"
                    with open(os.path.join(output_dir, fname), "w", encoding="utf-8") as f2:
                        f2.write(content)
                    out.write(f"Saved as {fname}\n\n")
                    
        except Exception as e:
            out.write(f"Error {name}: {e}\n")

print("Done. Check data_module_search.txt")