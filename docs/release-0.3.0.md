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
| 3. What's new pictures | ❌ `servers.jpg` shows real community server names (blur or retake); `inventory.jpg` shows the old layout (retake) |
| 4. What's new notes, README, CHANGELOG | to do: add the newer changes (ExtraSlots, Quick Stack Store, bag tabs, mod slots tab, warnings, minimal compass default) |
| 5. Testing | all-mods profile: automated `ct-*` suite passes (2026-10-31). To do: standalone (Jatify UI alone, then with MapTeleport), data safety (death, grave, load without the mod) |
| 6. Screenshots | to do: list for OK, then the standalone profile |
| 7. Versions | to do: manifest, thunderstore.toml and CHANGELOG at 0.3.0 / 1.2.0 |
| 8. mod-compatibility.md | to do |
| 9. Merge, gitleaks, push, tags | to do, after the user's OK |
| 10. Thunderstore, GitHub release, Discord changelog and compat pin | to do, on the user's explicit go |
