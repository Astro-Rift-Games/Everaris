"""Applies the Walk rework to the Unity project.

1. Copies ../png over Assets/Art/Character (the .meta files are not touched).
2. Moves the weapon grip keys of the six Walk body clips by the same offset as the hand
   sprite they follow: WeaponPose and OffHandGrip follow LeftHand, MainHandGrip follows
   RightHand. Offsets come from ../hand_grip_offsets.csv (written by validate.py).
3. Slows the Walk blend trees (body and both hands) to the proposed cadence.

Clips and controller are always rebuilt from the committed version (git HEAD), so running
this twice gives the same result.

    python apply_to_unity.py
"""
import csv
import os
import re
import shutil
import subprocess

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT = os.path.normpath(os.path.join(ROOT, "..", ".."))
REPO = os.path.normpath(os.path.join(PROJECT, ".."))
ART = os.path.join(PROJECT, "Assets", "Art", "Character")
CLIPS = "Project Grimhold/Assets/Animations/Player/Walk/Walk_{}.anim"
CONTROLLER = "Project Grimhold/Assets/Animations/Player/Character.controller"

SAMPLE_RATE = 12.0
WALK_TIME_SCALE = 10.0 / 12.0  # plays the 12 fps clips at 10 fps: 0.8 s per cycle
WALK_TREES = ("Walk", "LeftHand-Walk", "RightHand-Walk")
GRIP_HAND = {
    "WeaponPose": "LeftHand",
    "LeftHandPivot/LeftHand/OffHandGrip": "LeftHand",
    "RightHandPivot/RightHand/MainHandGrip": "RightHand",
}


def committed(path):
    return subprocess.run(["git", "-C", REPO, "show", f"HEAD:{path}"], check=True,
                          capture_output=True).stdout.decode("utf-8")


def write(path, text):
    # the project keeps these YAML assets with CRLF line endings in the working tree
    with open(os.path.join(REPO, path), "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write(text.replace("\r\n", "\n"))


def number(value):
    text = repr(round(value, 7))
    return text[:-2] if text.endswith(".0") else text


def copy_sheets():
    count = 0
    for folder, _, files in os.walk(os.path.join(ROOT, "png")):
        for name in files:
            source = os.path.join(folder, name)
            target = os.path.join(ART, os.path.relpath(source, os.path.join(ROOT, "png")))
            if not os.path.exists(target) or not os.path.exists(target + ".meta"):
                raise SystemExit(f"no Unity asset to replace: {target}")
            shutil.copyfile(source, target)
            count += 1
    return count


def load_offsets():
    offsets = {}
    with open(os.path.join(ROOT, "hand_grip_offsets.csv"), newline="") as handle:
        for row in csv.DictReader(handle):
            offsets[(row["facing"], row["hand"], int(row["frame"]))] = (
                float(row["grip_dx_units"]), float(row["grip_dy_units"]))
    return offsets


def shift_grips(facing, offsets):
    text = committed(CLIPS.format(facing))
    start = text.index("  m_PositionCurves:")
    end = text.index("  m_ScaleCurves:")
    head, section, tail = text[:start], text[start:end], text[end:]
    # each curve block runs from "  - curve:" to its "    path: <transform>" line
    blocks = re.split(r"(?m)^(?=  - curve:)", section)
    moved = 0
    for index, block in enumerate(blocks):
        path = re.search(r"(?m)^    path: (.*)$", block)
        hand = GRIP_HAND.get(path.group(1).strip()) if path else None
        if not hand:
            continue

        keys = re.split(r"(?m)^(?=      - serializedVersion: 3)", block)
        for key_index, key in enumerate(keys):
            time = re.search(r"(?m)^        time: (\S+)$", key)
            value = re.search(r"value: \{x: (\S+), y: (\S+), z: (\S+)\}", key)
            if not time or not value:
                continue
            # the closing key at clip length holds the last frame
            frame = min(int(round(float(time.group(1)) * SAMPLE_RATE)), 7)
            dx, dy = offsets[(facing, hand, frame)]
            x, y = float(value.group(1).rstrip(",")), float(value.group(2).rstrip(","))
            replacement = f"value: {{x: {number(x + dx)}, y: {number(y + dy)}, z: {value.group(3)}}}"
            keys[key_index] = key[:value.start()] + replacement + key[value.end():]
            moved += 1
        blocks[index] = "".join(keys)
    # The clip stores the same curves a second time, one scalar curve per axis, under
    # m_EditorCurves; that copy is what the Editor and AnimationUtility read.
    start = tail.index("  m_EditorCurves:")
    end = tail.index("  m_EulerEditorCurves:")
    editor = re.split(r"(?m)^(?=  - serializedVersion: 2\n    curve:)", tail[start:end])
    for index, block in enumerate(editor):
        attribute = re.search(r"(?m)^    attribute: m_LocalPosition\.([xy])$", block)
        path = re.search(r"(?m)^    path: (.*)$", block)
        hand = GRIP_HAND.get(path.group(1).strip()) if path else None
        if not attribute or not hand:
            continue
        axis = 0 if attribute.group(1) == "x" else 1
        keys = re.split(r"(?m)^(?=      - serializedVersion: 3)", block)
        for key_index, key in enumerate(keys):
            time = re.search(r"(?m)^        time: (\S+)$", key)
            value = re.search(r"(?m)^        value: (\S+)$", key)
            if not time or not value:
                continue
            frame = min(int(round(float(time.group(1)) * SAMPLE_RATE)), 7)
            shifted = float(value.group(1)) + offsets[(facing, hand, frame)][axis]
            keys[key_index] = key[:value.start(1)] + number(shifted) + key[value.end(1):]
            moved += 1
        editor[index] = "".join(keys)
    tail = tail[:start] + "".join(editor) + tail[end:]
    write(CLIPS.format(facing), head + "".join(blocks) + tail)
    return moved


def slow_walk_trees():
    text = committed(CONTROLLER)
    documents = re.split(r"(?m)^(?=--- )", text)
    changed = 0
    for index, document in enumerate(documents):
        name = re.search(r"(?m)^  m_Name: (.*)$", document)
        if not document.startswith("--- !u!206") or not name or name.group(1).strip() not in WALK_TREES:
            continue
        document, count = re.subn(r"(?m)^(    m_TimeScale: ).*$", r"\g<1>" + number(WALK_TIME_SCALE), document)
        documents[index] = document
        changed += count
    write(CONTROLLER, "".join(documents))
    return changed


if __name__ == "__main__":
    print("sheets copied:", copy_sheets())
    grip_offsets = load_offsets()
    for facing in ("N", "NE", "NW", "S", "SE", "SW"):
        print(f"Walk_{facing}.anim: {shift_grips(facing, grip_offsets)} grip keys moved")
    print("walk blend-tree children slowed:", slow_walk_trees())
