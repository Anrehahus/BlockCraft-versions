# Classic 0.24 Survival Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Classic 0.24 Survival Test profile while preserving current creative, save, and network behavior.

**Architecture:** Store the selected gameplay profile with each world. Route interaction, item, health, mob, and generation rules through that profile; retain existing behavior for old worlds. Keep server authority for pickups, damage, and saved world state.

**Tech Stack:** Unity 6000.3.11f1, C#, Mirror, existing ScriptableObjects and JSON save format.

**Spec:** `docs/superpowers/specs/2026-11-10-classic-024-survival-mode-design.md`

## Global Constraints

- Do not download packages or Unity components.
- Do not remove existing creative or network modes.
- Old saves without a profile field must load with their current behavior.
- Treat the wiki as the reference; the original 0.24 client and corresponding server are unavailable.
- Do not claim an executable build unless the installed Unity editor completes it.

## Review Focus

- Existing worlds have no profile field: keep their behavior and load successfully.
- A client requests pickup or attack: server validates ownership, range, and item availability.
- Player dies while saving or reconnecting: keep final score and terminal state.
- Generated chunks meet at negative and positive coordinates without seams or a broken bedrock/lava floor.
- Missing item/drop configuration: fail safely without losing a mined block or crashing.

## Files and responsibilities

- `Assets/Project/Scripts/Management/SaveSystem.cs`, world creation UI, and `WorldManager.cs`: persist and apply the world profile.
- `Assets/Project/Scripts/Player/PlayerBlockInteraction.cs`, `InventoryUI.cs`, and `PlayerHotbarUI.cs`: mining, placement, item inventory, and UI.
- `Assets/Project/Scripts/Mobs/`: authoritative dropped items and player arrow use, reusing current projectile code.
- `Assets/Project/Scripts/Player/PlayerHealth.cs`, `DeathScreenUI.cs`, and `PlayerModel.cs`: survival healing, death, and final score display.
- `Assets/Project/Scripts/Mobs/Data/MobDataSO.cs` and `Assets/Resources/MobData/*.asset`: version-specific health, damage, and death explosion values.
- `Assets/Project/Scripts/Generation/`: profile-specific terrain, cave, and lava generation.
- `docs/classic-024-survival-test.md`: verified matches, remaining caveats, and exact build result.

## Tasks

### Task 1: Preserve a per-world profile

- Add a defaulted profile field to the saved world model and expose the choice in world creation.
- Make loading old saves default to their current mode; pass the selected profile into `WorldManager` and terrain generation.
- Verify save/load serialization and that old save JSON still deserializes.

### Task 2: Blocks, inventory, and persistence

- Add the smallest item stack representation needed for block drops and the nine-slot hotbar.
- Implement documented drops: most blocks drop themselves, grass drops dirt, logs drop 3–5 planks, leaves drop saplings at 10%.
- Make mining, pickup, placement, multiplayer replication, and world/player save data use the same authoritative inventory state.
- Keep creative inventory behavior unchanged for existing worlds.

### Task 3: Player survival actions

- Add the initial arrow supply and Tab firing using the existing projectile implementation.
- Make brown mushrooms heal 5 health points and red mushrooms damage 3, matching the wiki's heart units.
- Keep arrow count and inventory state across save/load and synchronized on the server.

### Task 4: Death, score, and mobs

- Preserve the existing score values (zombie 100, skeleton 100, creeper 250, pig 10) and show the final score on Game Over.
- Prevent respawn in the Survival Test profile while preserving current respawn in other profiles.
- Configure the documented mob health and attack ranges; make creepers attack in melee and explode on death with version-appropriate damage.
- Keep damage and death authoritative in multiplayer.

### Task 5: Version-specific world and interaction rules

- Retain the lava layer over bedrock and make heightmap/caves match documented terrain traits where the source gives enough detail.
- Apply the one-block reduced reach and auto-jump only in the Survival Test profile.
- Verify neighboring chunk boundaries and keep old-world generation unchanged.

### Task 6: Documentation and build

- Update `docs/classic-024-survival-test.md` with implemented mechanics, unresolved source assumptions, and how to select the profile.
- Build the configured Windows player using the installed Unity editor only.
- Record the exact command, Unity version, output path, and whether compilation/build succeeded. If Unity is absent, leave the build explicitly blocked and do not download it.

## Execution notes

- The developer instruction for this workspace prohibits adding/running tests unless explicitly requested; use code review, `git diff --check`, and the requested Unity build as available verification.
- Split commits by completed task; do not push incomplete features to `main`.
