import subprocess

script = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\parse_catalog.py"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\parse_result.txt"

result = subprocess.run(["python", script], capture_output=True, text=True, timeout=120)

with open(output, 'w', encoding='utf-8') as f:
    f.write("STDOUT:\n")
    f.write(result.stdout)
    f.write("\nSTDERR:\n")
    f.write(result.stderr)

print("Done.")