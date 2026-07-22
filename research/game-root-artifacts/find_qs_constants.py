import re
import os

chunk_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\chunk_data_3599.js"
output_path = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\qs_constants.txt"

with open(chunk_path, "r", encoding="utf-8") as f:
    content = f.read()

with open(output_path, "w", encoding="utf-8") as out:
    # Search for qs definitions - these are likely in module 58534 based on earlier search
    # Let's find all patterns where qs is defined with string values
    
    # Method 1: Find qs object literal
    # Look for signature, popularPositive, popularNegative, expensive, economical, largePartition
    keywords = ["signature", "popularPositive", "popularNegative", "expensive", "economical", "largePartition"]
    
    for kw in keywords:
        # Search for patterns like signature:"VALUE" or signature="VALUE"
        patterns = [
            f'{kw}:"([^"]*)"',
            f"{kw}='([^']*)'",
            f'{kw}=("([^"]*)")',
        ]
        for pat in patterns:
            for match in re.finditer(pat, content):
                val = match.group(1) if match.lastindex >= 1 else match.group(2)
                ctx_start = max(0, match.start() - 100)
                ctx = content[ctx_start:match.end() + 50]
                out.write(f"  {kw} = '{val}' at pos {match.start()}\n")
                out.write(f"  Context: ...{ctx}...\n\n")
    
    # Method 2: Search for the module that exports qs
    # From earlier search we saw: 58534:(e,t,a)=>{a.d(t,{$S:()=>d,A2:()=>r,KI:()=>o,Sp:()=>p,Xq:()=>c,iK:()=>i,oY:()=>n,qs:()=>s,rh:()=>g,sW:()=>l});
    # So qs is defined as variable 's' in module 58534
    out.write("\n=== Searching for module 58534 (qs definition) ===\n")
    idx = content.find("58534:")
    if idx >= 0:
        # Get the module content
        start = idx
        # Find the end of this module (next module definition)
        next_mod = content.find("},", start + 100)
        if next_mod >= 0:
            module_content = content[start:next_mod+1]
            out.write(f"Module 58534 (first 2000 chars):\n{module_content[:2000]}\n\n")
            
            # Find qs/s variable definition
            # Look for let s= or var s= or s=
            s_match = re.search(r'let\s+s\s*=\s*\{([^}]+)\}', module_content)
            if not s_match:
                s_match = re.search(r'var\s+s\s*=\s*\{([^}]+)\}', module_content)
            if not s_match:
                # Try to find s={ pattern
                s_match = re.search(r's\s*=\s*\{([^}]+)\}', module_content)
            if s_match:
                out.write(f"qs object content:\n{s_match.group(0)}\n")
            else:
                out.write("Could not find s= assignment, searching for individual values...\n")
                # Search for signature, popularPositive etc in this module
                for kw in keywords:
                    kw_idx = module_content.find(kw)
                    if kw_idx >= 0:
                        out.write(f"  Found '{kw}' at offset {kw_idx}: ...{module_content[kw_idx:kw_idx+100]}...\n")

print("Done. Check qs_constants.txt")