# Competition studio

Choose FC26 or FC27, then Competition editing. FC27 opens the bundled Compdata;
FC26 competition editing is unavailable until matching Compdata is distributed with ATLink.

The seven sections are Overview, Teams, Format & qualification, Calendar, Rules, Settings and
Expert. Names are resolved using the selected game's LOC. Club IDs stay behind
searchable club names and crests. Initial allocations and group assignments are
separate entries; dynamic qualifying rules are not flattened into fixed clubs.

## Editing and history

Section buttons stage changes in the project without writing the source files.
Internal history remains available to the editing services; the main toolbar exposes
only Back, Open project and Export project. Edited rules are staged when changing
competition/scope or using the section's Apply rules button.
Unknown settings and unknown rule values are retained in Expert.

Add competition sits between the search field and competition list with 20px spacing.
In Overview, Translations sits beside Apply name and only edits the selected
competition's full and abbreviated names in the available languages.

FC27 Overview displays the integrated league1024, league128 and leaguetiny PNGs
using the competition's effective asset_id. Custom project artwork takes priority;
native previews use dark then light and show « Image indisponible » when unavailable.
These previews do not change the project or add resources to exports. The image
import editor remains available beneath the previews.
Project exports also record portable competition-to-logo associations in the
manifest. Reopening a .link or folder restores these references and resolves the
images from ATLink's integrated resources, without retaining paths from the exporting PC.
Custom images remain embedded in the project as before.

Open project offers an automatic .link (no password) or a Compdata/exported-project folder.
Export project offers .link, a Compdata folder or FIFAMOD (FC27 only). The folder option
keeps the complete existing project export: manifest, Compdata, translations and any
exported DB data. Cancelling the format chooser does not change the project.

## Calendar

The month view distinguishes possible time slots from assigned fixtures.
Relative offsets use an explicitly illustrative reference season (day zero).
This is not a verified conversion to actual FC27 match dates. Acknowledge the
reference before changing relative dates. Moving a matchday keeps other kick-off
times; the selected slot's hour and capacity are independently editable. Shared
calendar changes require confirmation. Absolute fixture dates stay absolute.

## Creation and export

The default creation mode duplicates a compatible competition's complete Compdata
structure and uses its database identity as a template. Custom mode builds a
league, groups or a cup; league/groups can optionally feed knockout rounds.

New DB competition IDs, optional league IDs and Compdata object IDs are allocated
separately and checked for collisions. Localized trophy names are added to a LOC
table with available capacity. Existing domestic club memberships are not moved.
Known ball/weather and sponsor presentation links are copied from the template;
other unknown game-specific configuration remains a separate integration step.

Export project → Exporter un dossier Compdata writes a new folder containing Compdata, the full base database,
LOC, metadata and project.json. The manifest lists changes, ID mappings, the
reference season and integration notes. Open project → Ouvrir un dossier Compdata reopens that folder without
reapplying its already-serialized DB additions. A different-game project is refused.

Assets such as graphics, trophies and broadcast presentation, and registration in
specific game modes, may still require additional work with the game's tools.
Successful internal validation does not establish in-game compatibility.

## Checks

- Core: dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --competition-studio .
- UI: dotnet run --project Checks/UI/ATLink.UIChecks.csproj -c Release -- --competition-studio .
- Legacy rules: dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --features

UI captures are written to Checks/output/ui/studio at 1400x850 and 960x580.

## Football-first authoring

The Add competition flow uses five steps: Format (including Type), Teams, Schedule & venues, Rules & rewards, and Review.
Technical preparation phases and historical initial standings are not participants
or played rounds. Native SETUP objects remain in the data and Expert editor.

The supplied Create League.docx guide confirms the role of SETUP, zero-based
standings, initteams as previous-season results, qualification routes and rule
inheritance. Its older version-specific limits are not used as FC27 validation
limits. The guide and DBM definitions confirm LEAGUE + GROUP match situation for
groups, and KO2LEGS for two-legged knockout ties. The compiler emits these native
values and validates its sporting participant flow before changing any files.

Rules are selected from a typed compatible catalogue. Known head-to-head criteria
are offered, while country, coefficient and other unverified tie-break semantics
remain preserved in Expert. rule_suspension is an object reference, never a toggle.

Existing special structures keep their native data. Existing phase removal and
some cross-competition/shared-calendar structural changes remain Expert operations.
Normal rule and name changes do not regenerate the qualification graph or dates.
The compiler preserves custom native rules and requires a preview/confirmation
before replacing internal sporting qualification connections.

Current participants are read from named-team and league selection rules, not
initteams historical finish positions. Missing explicit participants are shown
as To be determined; assigning them writes the underlying known preparation
slot automatically. Removing a named participant removes its selection task
rather than emitting an undocumented FillWithTeam -1 instruction. Replacing a
named participant updates its repeated named allocations, not historical results.

Settings displays configured supported options per played phase, including inherited defaults. Add option offers compatible options not already configured. Confirmed edits in the studio are written to the shared project draft as an undoable transaction; edits inside a phase dialog remain isolated until Confirm. Unknown settings remain in Expert.

## DBM season behaviour
See [COMPETITION-PARITY.md](COMPETITION-PARITY.md) for the guided season engine, source/operation mappings, creation/duplication flow, calendar behaviour, preservation policy and verification commands.
