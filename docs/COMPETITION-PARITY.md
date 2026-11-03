# Competition authoring parity with DBM Studio

The competition engine now exposes DBM-style sources, season automation, historical standings, qualification destinations and configurable schedules in the ATLink WPF interface. The reference is DBM Studio commit `1828c34e9650875a5b6befdf493854f8e4fbcfe1`. FC27 adaptations are explicit; this is file-level behaviour coverage, not a claim of game certification.

## User flow

Creation uses five steps: Format, Teams, Schedule & venues, Rules & rewards, and Review. `Teams / Season behaviour` configures dynamic or named entrants, optional previous-season standings, automatic final-result updates and competition links. Dates and venues have their own step. The final Review includes these choices and the illustrative reference date.

Existing competitions use `Settings / Phase options` and `Settings / Season behaviour`. Read/import does not rewrite their files. `Suggest missing season updates` proposes a reviewed change; it does not expand a deliberately partial native historical classification. Rule order is editable. Unknown native tasks remain in Expert.

A pending season draft must be reviewed and confirmed before application or before another action can refresh it. A changed format must be previewed/applied before changing sections. Confirmed operations use the shared project transaction history. Cancel discards unapplied form data. Applied changes support Undo/Redo and the existing Apply checkpoint.

## Common interfaces and mappings

- `FootballSeasonTask` describes an action, a typed Compdata target, parameters with a typed schema and an optional original row index. The common catalogue supplies the legacy task editor too.
- `CompdataObjectId`, `DatabaseCompetitionId` and `DatabaseLeagueId` keep the ID namespaces separate. `FillFromCompTable` refers to a Compdata competition; league sources/statistics refer to DB leagues. Native FC27 promotion/relegation settings use DB competition IDs; `schedule_forcecomp` uses a Compdata object.
- `FootballBehaviourPlan` joins sources, initial standings, season policy, links and `FootballCalendarPlan`. Creation, edits and duplication use the same compiler/context and native adapters.
- The fourteen task actions include named teams, leagues, ordered league entries, special pools, country-limited league entries, coefficient-country entries, tournament qualifiers and both backup types; clear statistics and the three result/statistics update actions are also available.

Imported row positions and opaque actions are retained. A no-op save preserves original rows. Reordering managed rules changes their native order without dropping opaque rows. Special/unsupported automation blocks structural regeneration instead of guessing how to repair it; its phase rules can still be edited.

## FC27 evidence and deliberate differences

Automatic season operations are based on the supplied native C13 / Premier League and C201 / FA Cup mappings: target stages, source result groups, one-based task positions and DB league references are checked against the source data. A single final table stores its finishing positions; a final match stores winner/runner-up. League statistics target the new competition's own DB league when one exists. Champion markers are copied only where the native profile provides that mapping.

Known generated entry pools and complete generated result mappings follow reviewed format changes. New unfilled places remain `To be determined`; no team is silently invented and no missing participant becomes an automatic bye. Custom partial historical mappings and unknown native operations are preserved.

Knockout-to-knockout paths pair adjacent match winners like DBM. Group-to-knockout distribution remains a separate operation. Setup starts at S1 for a new native structure, followed by the played phases. FC27 writes LEAGUE + GROUP situation and KO2LEGS; GROUP and KO2LEG are accepted only as legacy input aliases.

Promotion/relegation destinations, playoff dependencies and observed qualification labels use named selectors. Connecting top-ranked teams to another competition writes an actual dynamic entry task in the destination, not only a display label. Existing title markers are preserved when adding continental qualification labels. Existing full/ambiguous destination pools require an explicit correction rather than deleting their sources.

FC26 keeps the legacy creation path and manual operations. FC27 automatic proposals are disabled for FC26 until its native profile coverage is verified. Unverified country allocation/coefficient settings and special rules remain available in Expert.

## Calendar and duplication

Native profiles supply default time/spacing; users can change them. Creation supports relative slots, no generated calendar, or assigned first-stage round-robin fixtures when all clubs are named. Existing slots can be regenerated after reviewing their replacement. Shared calendars and assigned fixture sets are protected from bulk replacement. Individual assigned matches use a phase/date/time/home/away form and can be edited in the existing Calendar view.

Dates remain based on an explicitly illustrative reference. No conversion to the real FC27 season epoch is asserted. Short native HHmm values are padded only for display/interpretation; reading does not normalize their stored bytes.

Duplication preserves the full native structure and stage codes, tasks, historical standings, fixtures and unknown settings. Known internal Compdata references are remapped; own-league statistics links use the new DB league. External promotion/relegation links keep their DB meaning. Newly staged competitions can be duplicated before export. Uninterpreted references remain literal and need Expert review.

## Export and remaining game integration

Exports write a new folder containing DB, LOC, Compdata and a manifest. Source files and existing domestic club memberships are preserved. Identity mappings survive export/reopening/re-export. The manifest separates the DBM reference, successful file validation and `InGame: Not performed`.

Review notes identify missing entrants, calendar data, missing first-season historical qualifiers, preserved special tasks and new league identities without club memberships. Assets, career registration, intentional DB memberships and the real game's season conversion still require game-specific integration. A successful serializer or comparison test does not establish playability.

## Verification

- `dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --competition-parity .`
- `dotnet run --project Checks/UI/ATLink.UIChecks.csproj -c Release -- --football-editor .`
- Existing `--features`, `--fc27-compdata` and `--competition-studio` checks remain applicable.

Core coverage includes all fourteen task schemas, adjacent knockout paths, all four sporting formats, mixed fixed/dynamic sources, backups, historical standings, reviewed entry-pool/result-map resizing, adding a final, typed links, qualification entry tasks, ordered edits, unknown preservation, duplication and staged duplication, configurable cross-year calendars, leap-day fixtures, manual fixtures, rollback/history and DB/LOC/Compdata export/reopen.

UI coverage includes all 93 native competition models without pending edits, direct format changes without selection recursion, five creation steps, named rule dialogs, season editing, dynamic creation, duplication, cancellation/confirmation, 1400×850 and 960×580 renders and WPF binding-error checks. Captures are under `Checks/output/ui/football`.
## Individual dates during creation

The five-step wizard includes `Schedule & venues / Dates & matches`:

- `Automatic schedule` keeps the existing first-date/interval settings.
- `Choose matchday dates` builds editable rows for every played phase and matchday. Users choose each date and kick-off, and may add extra time slots. Updating the format preserves compatible dates by phase/round identity; a reset is explicit.
- `Exact fixtures` stores the entered phase, full date, time and named home/away clubs immediately when the competition draft is created. It does not add an unrelated automatic schedule. Unlisted matches remain unscheduled and this is stated in Review.
- Known fixtures may also be entered alongside matchday slots. Their phase/date/time and capacity must match an available slot. Later knockout rounds can keep dated slots without guessing their qualified teams.

Creation validates missing matchdays, capacities, duplicate slots, invalid times, phase references and club double-bookings. The final Review includes every chosen date and entered fixture. Export/reopening preserves the relative slots and the absolute fixture dates. Relative matchday dates still use the explicitly illustrative project reference; successful tests do not certify the game's real season conversion.