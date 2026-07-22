import subprocess

script = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\find_data_bundles.py"
output = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\find_data_output.txt"

result = subprocess.run(["python", script], capture_output=True, text=True, timeout=600)

with open(output, 'w', encoding='utf-8') as f:
    f.write("STDOUT:\n")
    f.write(result.stdout)
    f.write("\nSTDERR:\n")
    f.write(result.stderr)
    f.write(f"\nReturn code: {result.returncode}\n")

print("Done.")