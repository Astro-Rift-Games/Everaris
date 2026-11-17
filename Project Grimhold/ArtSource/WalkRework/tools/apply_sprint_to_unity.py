"""Imports the staged Sprint animation into the Unity project (file level).

1. Copies ../../SprintAnimation/png into Assets/Art/Character/**/Sprint/. Each sheet gets
   the import settings and sprite slicing of its Walk counterpart: the Walk .meta is copied
   with a new GUID and "-Walk-" renamed to "-Sprint-", so every Sprint sprite keeps the
   internal id of the Walk sprite at the same frame.
2. Creates the Sprint clips next to the Walk ones (body, LeftHand, RightHand, six facings)
   as copies of the Walk clips that reference the Sprint sheets. Because sprite ids match,
   only the sheet GUIDs change. Grips and hand depth are then authored in the Editor.
3. Appends, to every equipment definition, a Sprint mapping for each Walk mapping it has.

GUIDs are derived from the asset path, so running this twice gives the same result.

    python apply_sprint_to_unity.py
"""
import glob
import hashlib
import os
import re
import shutil

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT = os.path.normpath(os.path.join(ROOT, "..", ".."))
STAGED = os.path.normpath(os.path.join(ROOT, "..", "SprintAnimation", "png"))
ART = os.path.join(PROJECT, "Assets", "Art", "Character")
CLIPS = os.path.join(PROJECT, "Assets", "Animations", "Player")
DEFINITIONS = os.path.join(PROJECT, "Assets", "Scriptable Objects", "Loot", "Definitions")
FACINGS = ("N", "NE", "NW", "S", "SE", "SW")


def read(path):
    with open(path, encoding="utf-8", newline="") as handle:
        return handle.read()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def guid_of(meta_text):
    return re.search(r"(?m)^guid: (\w+)", meta_text).group(1)


def new_guid(asset_path):
    relative = os.path.relpath(asset_path, PROJECT).replace(os.sep, "/")
    return hashlib.md5(("grimhold-sprint:" + relative).encode("utf-8")).hexdigest()


def copy_meta(source_meta, target_asset, rename=None):
    text = read(source_meta)
    guid = new_guid(target_asset)
    text = re.sub(r"(?m)^guid: \w+", "guid: " + guid, text, count=1)
    if rename:
        text = text.replace(*rename)
    write(target_asset + ".meta", text)
    return guid


def import_sheets():
    """Returns {walk sheet guid: sprint sheet guid}."""
    guids = {}
    for staged in glob.glob(os.path.join(STAGED, "**", "*.png"), recursive=True):
        relative = os.path.relpath(staged, STAGED)
        target = os.path.join(ART, relative)
        walk = os.path.join(ART, relative.replace("Sprint", "Walk"))
        if not os.path.exists(walk + ".meta"):
            raise SystemExit(f"no Walk sheet to take import settings from: {walk}")
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(staged, target)
        guids[guid_of(read(walk + ".meta"))] = copy_meta(walk + ".meta", target, ("-Walk-", "-Sprint-"))
    return guids


def create_clips(sheet_guids):
    count = 0
    for facing in FACINGS:
        for folder, prefix in (("", ""), ("LeftHand", "LeftHand_"), ("RightHand", "RightHand_")):
            walk = os.path.join(CLIPS, "Walk", folder, f"{prefix}Walk_{facing}.anim")
            sprint = os.path.join(CLIPS, "Sprint", folder, f"{prefix}Sprint_{facing}.anim")
            text = read(walk)
            for old, new in sheet_guids.items():
                text = text.replace("guid: " + old, "guid: " + new)
            text = text.replace(f"m_Name: {prefix}Walk_{facing}", f"m_Name: {prefix}Sprint_{facing}")
            write(sprint, text)
            copy_meta(walk + ".meta", sprint)
            count += 1
    return count


MAPPING = re.compile(
    r"    - BaseSprite: \{fileID: (-?\d+), guid: (\w+), type: 3\}\r?\n"
    r"      EquipmentSprite: \{fileID: (-?\d+), guid: (\w+), type: 3\}\r?\n")


def map_equipment(sheet_guids):
    """Appends the Sprint twin of every Walk mapping; returns {asset name: mappings added}."""
    added = {}
    for path in sorted(glob.glob(os.path.join(DEFINITIONS, "*.asset"))):
        text = read(path)
        newline = "\r\n" if "\r\n" in text else "\n"
        matches = list(MAPPING.finditer(text))
        existing = {(m.group(1), m.group(2)) for m in matches}
        extra = []
        for match in matches:
            base_id, base_guid, equipment_id, equipment_guid = match.groups()
            if base_guid not in sheet_guids or equipment_guid not in sheet_guids:
                continue
            if (base_id, sheet_guids[base_guid]) in existing:
                continue
            extra.append(
                f"    - BaseSprite: {{fileID: {base_id}, guid: {sheet_guids[base_guid]}, type: 3}}{newline}"
                f"      EquipmentSprite: {{fileID: {equipment_id}, guid: {sheet_guids[equipment_guid]}, type: 3}}{newline}")
        if extra:
            end = matches[-1].end()
            write(path, text[:end] + "".join(extra) + text[end:])
            added[os.path.basename(path)] = len(extra)
    return added


if __name__ == "__main__":
    sheets = import_sheets()
    print("sheets imported:", len(sheets))
    print("clips created:", create_clips(sheets))
    mapped = map_equipment(sheets)
    print("equipment definitions mapped:", len(mapped), "| mappings added:", sum(mapped.values()))
    for name, count in mapped.items():
        print(f"  {name}: {count}")
