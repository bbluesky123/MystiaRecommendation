import requests
import re
import os

output_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya"

# Step 1: Scrape the customer page HTML for embedded RSC data
print("=== Fetching customer page with RSC data ===")
resp = requests.get("https://izakaya.cc/customer-rare/莉格露", timeout=15, headers={
    "User-Agent": "Mozilla/5.0",
    "Accept": "text/html"
})

if resp.status_code == 200:
    html = resp.text
    # Look for embedded JSON data in script tags
    # Next.js often embeds data in __NEXT_DATA__ or in RSC flight data
    script_contents = re.findall(r'<script[^>]*>(.*?)</script>', html, re.DOTALL)
    
    with open(os.path.join(output_dir, "customer_html_analysis.txt"), "w", encoding="utf-8") as out:
        out.write(f"HTML length: {len(html)}\n")
        out.write(f"Number of script tags: {len(script_contents)}\n\n")
        
        for i, sc in enumerate(script_contents):
            if len(sc) > 100:
                out.write(f"=== Script {i} (len={len(sc)}) ===\n")
                # Check for customer data
                if any(kw in sc for kw in ["positiveTag", "negativeTag", "beverageTag", "enduranceLimit", "莉格露", "博丽灵梦"]):
                    out.write("CONTAINS CUSTOMER DATA!\n")
                if "self.__next_f" in sc:
                    out.write("RSC flight data\n")
                out.write(sc[:500] + "\n...\n\n")

print("Done. Check customer_html_analysis.txt")