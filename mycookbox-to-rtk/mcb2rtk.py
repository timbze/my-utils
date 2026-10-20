# SPDX-License-Identifier: GPL-3.0-or-later
"""Convert a My CookBox backup (.bck) into a RecetteTek / My Recipe Box archive (.rtk).

Format spec: https://github.com/cmonapp/rtk-format

usage: python3 -I mcb2rtk.py INPUT.bck OUTPUT.rtk [--only N,N,...] [--renamed-only]

My Recipe Box merges recipes that share a title on import, so recipes with the same
title are renamed using their "From ..." description, and exact copies are dropped.
"""
import collections
import io
import json
import re
import sys
import uuid
import zipfile
import xml.etree.ElementTree as ET

NS = uuid.UUID("6f1c2a52-6a0e-4c41-9a51-9b1f6f2f8e11")


def text(el, path):
    found = el.find(path)
    if found is None or found.text is None:
        return ""
    return found.text.replace("\r\n", "\n").replace("\r", "\n").strip()


def tidy(s):
    return re.sub(r"[ \t]+", " ", s).strip()


FRACTIONS = {"½": 0.5, "⅓": 1 / 3, "⅔": 2 / 3, "¼": 0.25, "¾": 0.75}


def to_minutes(s):
    """Return whole minutes for simple values like '10 minutes', '1 hour', 'Bake 1½ hrs', else None."""
    m = re.fullmatch(r"(?:bake\s+)?(\d+)([½⅓⅔¼¾]?)\s*(min|mins|minutes|hr|hrs|hour|hours)?", s.strip(), re.I)
    if not m:
        return None
    value = int(m.group(1)) + FRACTIONS.get(m.group(2), 0)
    if m.group(3) and m.group(3).lower().startswith("h"):
        value *= 60
    return int(round(value)) or None


def convert_recipe(rcp_bytes, modified, key):
    inner = zipfile.ZipFile(io.BytesIO(rcp_bytes))
    root = ET.fromstring(inner.read("recipe.xml"))
    info = root.find("recipeinfo")
    rid = str(uuid.uuid5(NS, key + text(root, "title")))

    # Ingredients: one per line, "quantity fooditem"; named groups become section headers.
    # Some recipes used long group names as the method steps; those move to the instructions.
    lines, steps = [], []
    groups = sorted(root.findall("ingredientGroups/group"), key=lambda g: int(text(g, "groupNumber") or 0))
    for g in groups:
        name = tidy(text(g, "groupName"))
        items = sorted(g.findall("ingredientlist/ingredient"), key=lambda i: int(text(i, "orderInGroup") or 0))
        entries = []
        for ing in items:
            qty, food = tidy(text(ing, "quantity")), tidy(text(ing, "fooditem"))
            line = qty if food and food.lower() in qty.lower() else tidy(f"{qty} {food}")
            if line != "0" and any(ch.isalnum() for ch in line):
                entries.append(line)
        if name and (len(name) > 40 or not entries):
            steps.append(name.rstrip(":"))
            name = f"Step {len(steps)}"
        if name and entries:
            if lines:
                lines.append("")
            lines.append(name if name.endswith(":") else name + ":")
        lines.extend(entries)

    instructions = text(root, "preparation")
    if steps:
        method = "\n\n".join(f"{n}. {s}" for n, s in enumerate(steps, 1))
        instructions = f"{method}\n\n{instructions}" if instructions else method

    notes = []
    source = text(info, "source")
    if source:
        notes.append(f"Source: {source}")

    recipe = {
        "uuid": rid,
        "title": text(root, "title"),
        "description": text(info, "blurb"),
        "quantity": tidy(text(info, "yield")),
        "ingredients": "\n".join(lines),
        "instructions": instructions,
        "favorite": text(root, "isFavorite").lower() == "true",
        "lastModifiedDate": modified,
        "categories": [{"title": text(info, "genre")}] if text(info, "genre") else [],
        "tags": [],
        "pictures": [],
    }

    for src, dst, label in (("preptime", "preparationTime", "Prep time"), ("cookingtime", "cookingTime", "Cooking time")):
        raw = text(info, src)
        mins = to_minutes(raw) if raw and raw != "0" else None
        if mins:
            recipe[dst] = str(mins)
        if raw and raw != "0" and not re.fullmatch(r"\d+\s*(min|mins|minutes|hour|hours)", raw, re.I):
            notes.append(f"{label}: {raw}")
    if "preparationTime" in recipe or "cookingTime" in recipe:
        recipe["totalTime"] = int(recipe.get("preparationTime", 0)) + int(recipe.get("cookingTime", 0))

    rating = text(info, "rating")
    if rating:
        recipe["rating"] = float(rating)
    recipe["notes"] = "\n".join(notes)

    picture = None
    if "image.jpg" in inner.namelist():
        name = f"{rid}.jpg"
        recipe["pictures"] = [name]
        picture = (name, inner.read("image.jpg"))
    return recipe, picture


def label_for(recipe):
    label = re.sub(r"^from\s+", "", recipe["description"], flags=re.I)
    label = re.sub(r"\s*\(.*", "", label).strip()
    return label or recipe["quantity"]


def make_titles_unique(recipes):
    """Drop exact copies and rename recipes that share a title. Returns the renamed recipes."""
    seen, kept = set(), []
    for r in recipes:
        key = (r["title"], r["ingredients"], r["instructions"], r["description"], r["quantity"])
        if key not in seen:
            seen.add(key)
            kept.append(r)
    recipes[:] = kept

    by_title = collections.defaultdict(list)
    for r in recipes:
        by_title[r["title"]].append(r)
    renamed = []
    for title, group in by_title.items():
        if len(group) < 2:
            continue
        used = set()
        for n, r in enumerate(group, 1):
            label = label_for(r)
            if not label or label.lower() in used:
                label = f"version {n}"
            used.add(label.lower())
            r["title"] = f"{title} ({label})"
            renamed.append(r)
    return renamed


def main():
    args = sys.argv[1:]
    renamed_only = "--renamed-only" in args
    if renamed_only:
        args.remove("--renamed-only")
    src, dst = args[0], args[1]
    only = None
    if len(args) > 3 and args[2] == "--only":
        only = {int(n) for n in args[3].split(",")}

    outer = zipfile.ZipFile(src)
    entries = [i for i in outer.infolist() if re.fullmatch(r"recipes/recipe_(\d+)\.rcp", i.filename)]
    entries.sort(key=lambda i: int(re.search(r"(\d+)", i.filename).group(1)))

    recipes, pictures = [], []
    for info in entries:
        num = int(re.search(r"(\d+)", info.filename).group(1))
        if only and num not in only:
            continue
        modified = "%04d-%02d-%02d %02d:%02d:%02d" % info.date_time
        recipe, picture = convert_recipe(outer.read(info), modified, info.filename)
        recipes.append(recipe)
        if picture:
            pictures.append(picture)

    renamed = make_titles_unique(recipes)
    if renamed_only:
        recipes = renamed
    wanted = {p for r in recipes for p in r["pictures"]}
    pictures = [(name, data) for name, data in pictures if name in wanted]

    categories = sorted({c["title"] for r in recipes for c in r["categories"]})
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as out:
        for i in range(0, len(recipes), 400):
            out.writestr(f"recipes_{i // 400}.json", json.dumps(recipes[i:i + 400], ensure_ascii=False, indent=1))
        out.writestr("categories.json", json.dumps([{"title": c} for c in categories], ensure_ascii=False))
        out.writestr("tags.json", "[]")
        for name, data in pictures:
            out.writestr(name, data, compress_type=zipfile.ZIP_STORED)

    print(f"{len(recipes)} recipes, {len(pictures)} pictures, categories: {categories} -> {dst}")


if __name__ == "__main__":
    main()
