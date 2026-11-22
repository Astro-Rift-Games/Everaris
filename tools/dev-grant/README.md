# dev-grant — test items and gold for a local character

Developer-only tool to test Town upgrades (TASK-303), trading or equipment without raiding. It writes
directly to your **local** MongoDB; it never touches backend code and refuses any non-local database.

## Requirements

- Backend dependencies installed (`cd backend && npm install`).
- `MONGODB_URI` in the backend's local configuration pointing to `mongodb://localhost`, `127.0.0.1` or `[::1]`.
  `mongodb+srv://` and remote hosts are rejected before connecting.
- The account must exist (log in once from the game) and have an inventory schema v3 character.

## Unity Editor window

**Grimhold/Dev/Grant Items...** — nothing to run by hand. In Play Mode with your character it shows three buttons:
**Dar materiales de mejora**, **Dar equipo de prueba de mejoras** and **Dar 10000 de oro**. They target the
character of the running session (`--character <id>`) and the game rereads the profile automatically.
"Opciones" adds loose catalog items, custom gold, or (outside Play Mode) an account username. The result appears
in the window and the Console. If `node` is not on the Editor's PATH, set its full path in "Ejecutable de node".
Editor-only code (`Assets/Editor/DevTools/`); it never ships.

## Usage (from the repository root)

```bash
node tools/dev-grant/grant.js --user tester_a --preset upgrade-materials
node tools/dev-grant/grant.js --user tester_a --preset upgrade-gear --gold 5000
node tools/dev-grant/grant.js --user tester_a --item RES_RUNE_FRAGMENT:10 --item rapier:1
```

| Option | Meaning |
|---|---|
| `--user <username>` | Account username (or use `--character`). |
| `--character <id>` | Character id (24 hex), as used by the Editor window. |
| `--preset upgrade-materials` | 20 of each upgrade material: `RES_RUNE_FRAGMENT`, `RES_ETHER_DUST`, `RES_ATTUNEMENT_GEM`, `RES_BLACK_IRON`, `RES_TANNED_LEATHER`, `RES_ANCIENT_ARMOR`. |
| `--preset upgrade-gear` | rapier without scaling, rapier at C, rapier with one empty socket, magic_wand, `heavy_armor_breastplate` +0 and +3 (NPC limits). |
| `--item lootId:amount` | Any catalog lootId, amount 1..999; repeatable (max. 50). Equipment gets one new instance per unit. |
| `--gold N` | Adds gold, 1..100000. |

- Everything goes to the **Inventory** (loadout), never the Stash, in one write. If it does not fit it fails with
  `INVENTORY_FULL` and nothing changes; also `RESERVATION_PENDING`, `INVENTORY_MIGRATION_REQUIRED`,
  `INVALID_INVENTORY`, `VALIDATION_FAILED`, `ACCOUNT_NOT_FOUND`.
- The character revision advances, so the game syncs the new items the next time it reads the backend: re-enter
  the Town (or relaunch) after running the command.

## Tests

```bash
node --test tools/dev-grant/grant.test.js
```

Uses an in-memory MongoDB and synthetic configuration; it never reads your local database or secrets.
