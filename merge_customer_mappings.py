import json
import os
import re
from collections import OrderedDict


ROOT = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(os.environ.get("TEMP", ""), "customer_rare_data.ts")
TARGET = os.path.join(ROOT, "Data", "customers_rare.json")

DYNAMIC_TAGS = {
    "signature": "招牌",
    "popularPositive": "流行喜爱",
    "popularNegative": "流行厌恶",
    "expensive": "昂贵",
    "economical": "实惠",
    "largePartition": "大份",
}


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//.*", "", text)


def find_matching(text: str, start: int, opener: str, closer: str) -> int:
    depth = 0
    quote = None
    escape = False
    for i in range(start, len(text)):
        ch = text[i]
        if quote:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == quote:
                quote = None
            continue
        if ch in ("'", '"', "`"):
            quote = ch
            continue
        if ch == opener:
            depth += 1
        elif ch == closer:
            depth -= 1
            if depth == 0:
                return i
    return -1


def split_top_level(body: str) -> list[str]:
    parts = []
    start = 0
    depth = 0
    quote = None
    escape = False
    for i, ch in enumerate(body):
        if quote:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == quote:
                quote = None
            continue
        if ch in ("'", '"', "`"):
            quote = ch
            continue
        if ch in "{[(":
            depth += 1
        elif ch in "}])":
            depth -= 1
        elif ch == "," and depth == 0:
            part = body[start:i].strip()
            if part:
                parts.append(part)
            start = i + 1
    tail = body[start:].strip()
    if tail:
        parts.append(tail)
    return parts


def parse_value(value: str) -> str:
    value = value.strip()
    dyn = re.fullmatch(r"DYNAMIC_TAG_MAP\.([A-Za-z0-9_]+)", value)
    if dyn:
        return DYNAMIC_TAGS.get(dyn.group(1), "")
    if value.startswith("[") and value.endswith("]"):
        return parse_value(value[1:-1])
    if (value.startswith("'") and value.endswith("'")) or (value.startswith('"') and value.endswith('"')):
        return value[1:-1]
    return value


def parse_mapping(block: str, prop: str) -> OrderedDict:
    marker = f"{prop}:"
    idx = block.find(marker)
    if idx < 0:
        return OrderedDict()
    brace = block.find("{", idx)
    if brace < 0:
        return OrderedDict()
    end = find_matching(block, brace, "{", "}")
    if end < 0:
        return OrderedDict()

    body = block[brace + 1:end].strip()
    result = OrderedDict()
    if not body:
        return result

    for part in split_top_level(body):
        if ":" not in part:
            continue
        key, val = part.split(":", 1)
        key = parse_value(key.strip())
        val = parse_value(val.strip())
        if key and val:
            result[key] = val
    return result


def parse_source() -> dict[str, dict[str, OrderedDict]]:
    with open(SOURCE, "r", encoding="utf-8") as f:
        text = strip_comments(f.read())

    start = text.find("CUSTOMER_RARE_LIST")
    arr_start = text.find("[", start)
    arr_end = find_matching(text, arr_start, "[", "]")
    body = text[arr_start + 1:arr_end]

    mappings = {}
    pos = 0
    while True:
        obj_start = body.find("{", pos)
        if obj_start < 0:
            break
        obj_end = find_matching(body, obj_start, "{", "}")
        if obj_end < 0:
            break
        block = body[obj_start:obj_end + 1]
        pos = obj_end + 1

        name_match = re.search(r"name:\s*'([^']+)'", block)
        if not name_match:
            continue
        name = name_match.group(1)
        mappings[name] = {
            "positiveTagMapping": parse_mapping(block, "positiveTagMapping"),
            "beverageTagMapping": parse_mapping(block, "beverageTagMapping"),
        }
    return mappings


def merge():
    source = parse_source()
    with open(TARGET, "r", encoding="utf-8") as f:
        customers = json.load(f, object_pairs_hook=OrderedDict)

    touched_customers = 0
    added_food = 0
    added_bev = 0
    missing = []

    for customer in customers:
        name = customer.get("name", "")
        src = source.get(name)
        if not src:
            missing.append(name)
            continue

        changed = False
        for prop, counter_name in (("positiveTagMapping", "food"), ("beverageTagMapping", "bev")):
            current = OrderedDict(customer.get(prop) or {})
            for tag, phrase in src[prop].items():
                if current.get(tag) != phrase:
                    if tag not in current:
                        if counter_name == "food":
                            added_food += 1
                        else:
                            added_bev += 1
                    current[tag] = phrase
                    changed = True
            customer[prop] = current

        if changed:
            touched_customers += 1

    with open(TARGET, "w", encoding="utf-8", newline="\n") as f:
        json.dump(customers, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"source customers: {len(source)}")
    print(f"local customers: {len(customers)}")
    print(f"touched customers: {touched_customers}")
    print(f"added food mappings: {added_food}")
    print(f"added beverage mappings: {added_bev}")
    if missing:
        print("missing in source: " + ", ".join(missing))


if __name__ == "__main__":
    merge()
