# FC27 calendar origin

The supplied `schedule.txt` stores the Community Shield (C228, stage 509) as
`509,231,1,1,1,1700`. The user observed this match on **16 August 2026** in game.
The former illustrative origin, 1 July 2026, produced 17 February 2027 instead.
Subtracting the native offset from the observed date gives **28 December 2025**.

New FC27 contexts use that calibrated day-zero origin for reading and writing
relative slots, availability and international-friendly markers. Offset zero is
not the start of the playable season: creation and placement still start in July
2026. Absolute fixture dates are unchanged. No native source rows are rewritten.

This is one in-game calibration point for the supplied 2026/27 files, not proof
of every competition's season-year rules or career postponements. No universal
epoch formula is inferred for other years or games. FC26 keeps its prior origin.
Existing project manifests retain their explicit reference date so authored
relative slots are not silently reinterpreted. A project saved with the former
illustrative origin must be reviewed before changing its origin, especially if
it contains newly authored slots or absolute fixtures.

Scheduling checks assert the date, Sunday weekday, inverse date-to-offset
conversion, source preservation and FC26 isolation.
