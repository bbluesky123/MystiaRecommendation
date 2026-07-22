"""Build script for MystiaRecommendation.

Usage:
    python build.py                          # use game path from .csproj
    python build.py "C:\Games\Touhou Mystia Izakaya"  # override game dir
"""
import subprocess
import sys
import os

# Determine project directory (where this script lives)
PROJECT_DIR = os.path.dirname(os.path.abspath(__file__))
os.chdir(PROJECT_DIR)

# Build args
build_args = ["dotnet", "build", "-c", "Release"]

# Override game directory if provided
if len(sys.argv) > 1:
    game_dir = sys.argv[1]
    build_args.extend(["-p:GameDir=" + game_dir])

print(f"Building from: {PROJECT_DIR}")
if len(sys.argv) > 1:
    print(f"Game directory override: {sys.argv[1]}")
print()

# Restore
print("=== dotnet restore ===")
r1 = subprocess.run(["dotnet", "restore"], capture_output=True, text=True, timeout=120)
print(r1.stdout)
if r1.returncode != 0:
    print("RESTORE FAILED:")
    print(r1.stderr)
    sys.exit(1)

# Build
print("=== dotnet build ===")
r2 = subprocess.run(build_args, capture_output=True, text=True, timeout=120)
print(r2.stdout)
if r2.returncode != 0:
    print("BUILD FAILED:")
    print(r2.stderr)
    sys.exit(1)

print("\nBuild successful!")
print(f"Output: {PROJECT_DIR}\\bin\\Release\\net6.0\\MystiaRecommendation.dll")
