# Appearance code labels

Source: FC 27 Live Editor v27.1.9, loc/eng_us/localize.json, inspected locally.
PlayerAppearanceChoices contains 344 explicit code labels across 30 player fields.

Mappings use exact field-name prefixes where present. The explicit aliases are:
bodytypecode -> bodytype; haircolorcode -> haircolor; facialhaircolorcode ->
facialhaircolor; eyecolorcode -> eyecolor; accessorycode1..4 -> accessorycode;
accessorycolourcode1..4 -> accessorycolourcode; the seven tattoo locations ->
tattoo (only 0 = None); hashighqualityhead -> boolean.

Placeholder strings (for example socklengthcode_4 or Unnamed_1) are excluded.
The two shoe-color palettes retain their separate explicit source entries.
Generic visual assets, brands and goalkeeper animations have no named mapping
in this source and remain numeric. No animation names are inferred.

Names are interface labels translated through UiText; the underlying numeric
codes and selected database schema determine what can be saved. Unknown current
values remain visible, unknown permitted codes can be entered, and invalid codes
block application without partially updating player data.
