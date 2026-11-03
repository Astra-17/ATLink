# Phase rewards, venues and discipline

The FC27 football editor exposes phase essentials directly in the phase popup and through Settings. A collapsed knockout card contains an independent settings draft for each played round, including its final. Cards and the creation Review summarize these values. Creation and existing-competition editing use the same adapter and project history.

## Guided creation journey

Creation follows five steps: **Format → Teams → Schedule & venues → Rules & rewards → Review**. Type, identity and played rounds share the first step. Its phase popup is limited to format controls. Team sources and optional season behaviour remain in Teams; the duplicated calendar generator is hidden there.

Schedule & venues contains Dates & matches and Venues. Venue controls share the phase draft with Rules & rewards, which shows phase prizes and discipline alongside competition defaults and per-phase match rules. Navigating backwards or refreshing inherited rules preserves phase identities, dates, fixtures, rewards and venues. Direct tab navigation uses the same prerequisite validation as Next; Create appears only on Review.

In manual-date mode, **Adjust a whole phase** can shift all dates by a common offset or apply one kick-off to its slots and assigned fixtures. The full candidate schedule is validated before applying changes. Dates retain their spacing across month/year boundaries; invalid times, duplicate slots and fixture conflicts leave the draft unchanged. These remain illustrative reference-season dates. Unsupported per-match stadium choices remain explicitly unavailable.

## Established mappings

| Feature | Native mapping | Evidence and limits |
|---|---|---|
| Phase reward amount | `info_prize_money` on the played stage | Present throughout FC27; DBM's prize editor writes the scalar. The supplied Create League guide describes a winning-stage amount. Currency, beneficiaries for every format and cumulative distribution are not established by the files alone. |
| Fixed stadium for a phase | `match_stadium` on the played stage | Native FC27 stage 622 contains stadium 155. DBM exposes the stadium setting; the historical guide describes stage/group stadium selection. Stadium IDs are resolved against the selected game's full DB. |
| Inheritance | Remove only the selected local override | Never write zero to mean “inherit”. A zero prize is an explicit local amount. Keeping existing values performs no write, including malformed, out-of-range or multi-value native data. |

Amounts accept non-negative 32-bit integers, with grouped input and K/M summaries. This is an editor serialization bound, not a proven gameplay ceiling. Historical guide limits are not imposed on FC27: the supplied file already contains larger amounts. `info_prize_money_drop` and `info_prize_money_promo` remain untouched in Expert.

New FC27 competitions start with zero rewards and no forced stadium inherited from their technical template. Existing competitions retain their values and discipline ownership. A phase venue change is blocked when a different group-level stadium override would prevent it from applying to the whole phase. This avoids silently discarding group-specific venues.

## Explicitly unavailable capabilities

- Multiple host stadiums and venue assignment to individual dated fixtures: the supplied four-column fixture format has date, time, home and away only. No additional column is invented. The typed model represents these requests but the adapter rejects them, and the interface explains their unavailability.
- Clearing accumulated yellow cards while preserving acquired suspensions: no verified task or setting conversion was found in the supplied FC27 files, DBM sources or historical guide. `rule_suspension` is an object reference, not a reset flag. The clear option is disabled; existing references and suspensions are preserved.
- FC26 writes through these new controls: mappings remain unverified. Its existing workflow is unchanged and imported native values are retained.

## Automatic place labels

The format page displays a positional preview with title, qualification, promotion, playoff and relegation labels. It is not a live results table. Labels are projected from native sporting positions, existing visual markers and advancement routes, without changing files on consultation.

After explicit sporting edits, the native adapter derives supported visual markers from champion/promotion/relegation places and table-to-phase routes. It updates obsolete markers associated with changed places, while preserving unrelated and unrecognized markers. Cross-competition qualification previews identify existing labels affected by confirmation; reducing a known source's qualifier count also removes its obsolete selected labels. Native qualification rules and visual indicators remain separate operations in the same project transaction.

## Verification

- `--phase-settings`: 93 read-only competitions, unchanged export compared byte for byte, independent round prizes, final-only stadium, zero/inheritance, invalid values, unknown/multi-value preservation, unavailable-feature rejection, positional labels, history and export/reopening.
- `--football-editor` UI: guided creation, popup draft isolation, persistence after refreshing defaults, final prize/stadium, render sizes, inline errors and binding checks.
- The UI suite writes `Checks/output/competition-recreation.json` from native league, cup and groups-plus-knockout references entered through the guided ViewModels. It compares sporting format, phase prizes, venues, normalized qualifications and calendar slots, and explicitly records the remaining differences. It does not assert complete equivalence when tasks, special settings or routes differ.
- Existing competition parity and FC26/general feature checks remain required.

File validation, fidelity to a reference and game validation are different results. No FC27 in-game validation has been performed. Relative calendars still use the explicitly illustrative reference season.

Sources: supplied FC27 `settings.txt`, `compobj.txt` and `tasks.txt`; supplied `Create League.docx`; [DBM prize/venue editor](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/pages/compdata-editor/tournament-rules-settings.component.ts), [DBM validation](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/services/compdata/settings-validation.service.ts).

## Interactive matchday calendar and availability simulation

Dates & matches now has a six-week monthly grid with team crests, overflow counts, phase/team display filters and a vertical day popup. The popup lists the participating teams' other engagements, editable match counts and per-match quarter-hour kick-off selectors. Existing off-grid times remain selectable without rounding. Stadiums are inherited and cannot be overridden per fixture.

Use **Availability** to build the external index in the background; opening a day or simulating also loads it. The index is cached per project and native-file/reference fingerprint, with progress and cancellation. It resolves named clubs, league memberships, initial historical qualifiers and available backup sources. Unresolved sources produce coverage notes. Multiple kickoff alternatives are grouped by native round/day rather than counted as multiple matches per club. Shared physical slots are deduplicated. Native relative dates keep the illustrative reference and absolute fixtures are never moved to another year.

Statuses distinguish explicit assigned fixtures, deterministic round-robin proposals, and possible matchdays/qualifications. Later-round participants remain qualifier placeholders; group allocations and results are not asserted as facts. The team filter changes only the display: availability and placement still consider all resolved participants. Missing participant coverage is displayed explicitly.

**Simulate schedule** accepts a period, weekdays, preferred quarter-hour kickoff and 0–7 recovery days (default three). Three days means 72 hours, not merely three date labels. Known same-day conflicts remain forbidden even with zero recovery days. An unknown external kickoff is treated conservatively as a whole-day interval. Fixed matches and round dependencies are preserved; uncertain external engagements are risks to minimize. The search is deterministic and bounded, not a guarantee of a globally optimal solution. Unplaced matches and blocking conflicts prevent applying a proposal and include an explanation.

The monthly grid can display a simulation preview without changing the draft. Preview day popups are read-only. Applying dates creates capacity slots, not assigned team fixtures. **Use these fixtures** presents all named pairings in a separate confirmation window before freezing them. Unknown qualifiers remain unassigned. Day edits and proposal application have local Undo/Redo; final creation is one project transaction. Optional scheduling preferences are saved in the project manifest and participate in project history; derived availability caches are not persisted. Existing manifests without these fields still open.

Tentative initial suggestions can be corrected incrementally: conflicts involving unfixed suggested dates are shown as risks during day editing. Fixed dates and known external fixtures remain blocking; the full final calendar is checked again before competition creation. Unplaced matches must be rescheduled. No simulation predicts career-mode postponements or certifies the resulting dates in FC27.

Verification: `--scheduling` covers deterministic fixtures, native availability indexing, 71 h 45 versus 72 h recovery, same-day clashes, fixed matches, impossible periods, cancellation, unknown future qualifiers, read-only source data and scheduling-option export/reopening. The WPF `--football-editor` suite covers the Barcelona overlay, 42 cells, quarter-hour choices, popup cancellation/edit/removal, history, simulation preview without draft mutation, separate fixture confirmation and large/small renders.
