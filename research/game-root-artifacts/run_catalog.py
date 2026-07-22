import subprocess
import os

# Run find_data_bundle.py and capture output
script = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\find_data_bundle.py"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\catalog_result.txt"

result = subprocess.run(["python", script], capture_output=True, text=True, timeout=60)

with open(output, 'w', encoding='utf-8') as f:
    f.write("STDOUT:\n")
    f.write(result.stdout)
    f.write("\nSTDERR:\n")
    f.write(result.stderr)
    f.write(f"\nReturn code: {result.returncode}\n")

print("Done. Output saved to catalog_result.txt")