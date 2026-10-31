# Release: Jatify UI 0.3.0 and MapTeleport 1.2.0

Working record for this release, following `~/projects/docs/release-checklist.md`.

## Decisions (2026-10-31)

- **Versions:** Jatify UI 0.2.1 → **0.3.0**; MapTeleport 1.1.0 → **1.2.0**. AdminHammer, BuffMeDaddy and Campfire are unchanged and not released.
- **What ships:** only finished work. Everything on `fix/lucare-batch` is finished: the 83 commits since 0.2.1, including the Lucare batch (#15-#38), all fixed by 2026-10-30. Ideas that were discussed but never built stay out:
  - AdventureBackpacks' chest tools and Backpacks' close button inside our panel;
  - Alt + click with Quick Stack Store;
  - the Ctrl + E fallback for the station card.
- **Merge:** a normal merge of `fix/lucare-batch` into `main`, **not a squash**.
- **Screenshots:** a new set, taken in a rebuilt **standalone** profile (Jatify UI, MapTeleport and their declared dependencies only), with a clean save, a test character and the Jatify Flat world. The shot list gets the user's OK first.
- **Compatibility channel:** `~/projects/docs/mod-compatibility.md` is updated and the publish script refreshes the Discord #mod-compatibility pin (user: "remember the mod-compatibility channel at the end").
- Standing rules unchanged:
  - licence, AI-disclosure line, Discord and Buy me a coffee links;
  - each mod works on its own;
  - the Thunderstore publish, Discord posts and the GitHub release each need the user's explicit go.

## Progress

| Step | State |
|---|---|
| 1. Decisions recorded | ✅ this file |
| 2. Code hygiene | ✅ release DLL has no dev tools, home paths or other UI mod names; MapTeleport gained PathMap (it had a .pdb home path) |
| 3. What's new pictures | ✅ `inventory.jpg` retaken; `servers.jpg` kept as it is (user, 2026-10-31) |
| 4. What's new notes, README, CHANGELOG | ✅ CHANGELOG 0.3.0 / 1.2.0; README checked against the code by a review agent and fixed (2026-10-31) |
| 5. Testing | ✅ all-mods `ct-*` suite; data safety on a new character (rows, fill, no-mod load, save, death, grave); release zips smoke-tested in standalone: Jatify UI alone, with MapTeleport, MapTeleport alone, no exceptions (2026-10-31) |
| 6. Screenshots | ✅ README and What's new retaken in standalone, orange; Players page dropped (user) |
| 7. Versions | ✅ 0.3.0 / 1.2.0 in manifest, thunderstore.toml, Plugin.cs and CHANGELOG |
| 8. mod-compatibility.md | ✅ |
| 9. Merge, gitleaks, push, tags | gitleaks clean; merge, push and tags wait for the user's OK |
| 10. Thunderstore, GitHub release, Discord changelog and compat pin | to do, on the user's explicit go |
