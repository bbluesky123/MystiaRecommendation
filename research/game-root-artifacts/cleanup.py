import os
import glob

game_dir = r"d:\steam\steamapps\common\Touhou Mystia Izakaya"

# Remove temporary files
patterns = ["*.py", "customer_page_*.html", "rsc_data.txt", "chunk_*.js", "github_*",
            "page_chunk.js", "catalog_result.txt", "check_main_output.txt",
            "find_data_output.txt", "search_all_output.txt", "script_output.txt",
            "data_bundles_result.txt", "all_sizes_result.txt", "main_assets_result.txt",
            "catalog_preview.txt", "metadata_search.txt", "qs_constants.txt",
            "customer_html_analysis.txt", "website_customer_data.txt", "website_customers.json",
            "bundle_with_customers.txt", "bundle_search_all.txt", "final_search.txt",
            "verification_result.txt", "game_data_raw.txt"]

for pat in patterns:
    for f in glob.glob(os.path.join(game_dir, pat)):
        try:
            os.remove(f)
        except:
            pass

print("Cleanup done.")
print(f"Remaining files in game dir:")
for f in os.listdir(game_dir):
    if f.endswith(('.py', '.txt', '.json', '.html', '.js')) and f != 'final_report.txt':
        print(f"  {f}")