# Football matchup editor

Competition creation keeps five steps. Teams now displays club IDs and includes
a Matchups section before Season behaviour. Next visits Matchups for knockout
formats; pure tables skip it unless a removed customized bracket needs review.

Models are Current automatic, Crossed seeds and Reproducible draw. Crossed seeds
supports two groups and power-of-two qualifier counts. For eight per group its
bracket is A1–B8, A4–B5, A2–B7, A3–B6, B1–A8, B4–A5, B2–A7, B3–A6.
Other rounds default to adjacent winners. Manual source selection swaps the two
occupied places atomically. Both legs share a tie and mirror home/away.

Typed sources identify entry clubs/slots, group ranks or prior-round winners.
Phase draft keys and tie ordinals are independent of match dates. The compiler
maps these sources to its generated groups and calls SetProgression in the
creation transaction. Compatible pools must be complete and unique; later-round
sources cannot point backwards incorrectly or create cycles. The simulator uses
the same sources for labels, known entry pairings and possible participants,
without choosing results. Previously confirmed fixtures must be reviewed if
their entry pairings change.

Model names and draw seeds are optional MatchupSettings metadata in project.json,
keyed by competition and native phase. Native advancement rows remain the source
of truth for the draw result. Recognized native rows are reconstructed without
writing; unrecognized sources remain marked for Expert. Ordinary phase-rule edits
do not rewrite imported matchups. Existing manifests without metadata still open.

Creation customization and model replacement have local Undo/Redo. Applying a
model over customization first previews its resulting pairings. Missing sources
and removed custom rounds stay visible as errors rather than being silently
replaced. Format changes clear incompatible local history, not the custom choices.

Checks: --matchups covers Super League, fixed entries, dynamic qualifiers,
determinism, uniqueness, mirrored legs, native compilation/import, seed metadata,
history and export/reopen. --football-editor adds source swaps, guided navigation,
replacement cancellation, stale entries, removed rounds, labels and WPF renders.
No existing exported Super League project is migrated or rewritten automatically.
