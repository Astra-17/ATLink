# FC27 Playstyles artwork

Source catalog: https://www.futbin.com/27/playstyles (retrieved 2026-11-14).

36 styles across the six source categories. catalog.json preserves each source
name, stable asset identifier, category, and original white/gold download URL.
The white.png and gold.png files are the unmodified original FUTBIN CDN assets.
The gray.png files are locally derived inactive states from the corresponding
white images, preserving dimensions and alpha and darkening luminance to 42%.

Artwork remains the property of its respective rights holders (EA/FUTBIN).
The editor loads player bitfields into a draft and commits changed styles through Apply.

## Game-code mapping

Each catalog entry has a gameCode object: normalField, plusField, zero-based
bitIndex, and mask. Normal outfield styles use trait1, goalkeeper styles trait2;
the matching plus fields are icontrait1 and icontrait2, with the same bit.

Source: https://github.com/IanRae-17/le-26-scripts/blob/main/Adding_Playstyles.lua
The mapping is sourced from FC26. All 36 masks fit the FC27 players schema's
declared ranges. This is structural verification, not FC27 in-game validation.
The image mapping is used by PlayerPlaystylesCatalog and PlayerPlaystylesEditor.
Do not replace entire bitfields: preserve unrelated and unknown bits.
