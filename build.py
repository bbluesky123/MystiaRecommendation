import subprocess
import os

os.chdir(r"D:\new\MystiaRecommendation")

# Restore
r1 = subprocess.run(["dotnet", "restore"], capture_output=True, text=True, timeout=120)
# Build
r2 = subprocess.run(["dotnet", "build", "-c", "Release"], capture_output=True, text=True, timeout=120)

with open(r"D:\new\MystiaRecommendation\build_output.txt", 'w', encoding='utf-8') as f:
    f.write("=== RESTORE ===\n")
    f.write(r1.stdout)
    f.write(r1.stderr)
    f.write(f"\nReturn: {r1.returncode}\n")
    f.write("\n=== BUILD ===\n")
    f.write(r2.stdout)
    f.write(r2.stderr)
    f.write(f"\nReturn: {r2.returncode}\n")

print("Done. Check build_output.txt")