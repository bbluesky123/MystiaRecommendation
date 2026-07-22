import subprocess

script = r"d:\steam\steamapps\common\Touhou Mystia Izakaya\check_risks.py"
result = subprocess.run(["python", script], capture_output=True, text=True, timeout=60)

print("STDOUT:")
print(result.stdout)
print("STDERR:")
print(result.stderr)
print("Return code:", result.returncode)