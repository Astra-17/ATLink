# Competition artwork

FC27 competition creation includes **Competition artwork** in **Format**; existing competitions use **Overview**.
The same control is available for qualifying competitions in **Linked competitions**.

## Import and edit

Choose **Import / replace logo** for a PNG of exactly **1024 × 1024** and
**Import / replace name image** for a PNG of exactly **512 × 128**. Artwork is optional,
but both inputs are required once artwork is started. Invalid dimensions, malformed
images and entirely transparent inputs are rejected without replacing previous valid images.

Images retain their transparency and are not recolored. The same generated textures
are used for **dark** and **light**. The tiny preview shows the actual placement.

Generation runs away from the UI dispatcher, with progress and cancellation. In a
creation wizard, the images stay in its draft until **Create competition**. Existing
competitions use **Apply artwork**. **Discard artwork changes** restores the saved
artwork draft; **Remove artwork** removes the custom images when subsequently applied.
It does not delete a native resource from the game. Shared graphic IDs require an
explicit confirmation naming the affected competitions.

Applied changes participate in project Undo/Redo and Apply/Cancel. Compdata, domestic
club memberships, competition names and competition rules are not rewritten by an
artwork-only change. Duplicate competitions retain the images but get their own
resolved graphic resource paths.

## Native conversion profile

The profile was verified from the supplied `Asset Super League.fifamod`:

| Folder under `data/ui/imgAssets` | Size | DDS encoding | Mip levels |
|---|---:|---|---:|
| league | 256 × 256 | BGRA32, straight alpha | 9 |
| league512x512 | 512 × 512 | DXT5 / BC3 | 1 |
| league1024x1024 | 1024 × 1024 | BGRA32, straight alpha | 1 |
| leagueLogos_tiny | 200 × 64 | BGRA32, straight alpha | 1 |
| league512x128 | 512 × 128 | BGRA32, straight alpha | 10 |

Square images keep the full source canvas and its transparent margins. Tiny first
crops the transparent margins, scales proportionally into **60 × 55**, then places
the result at **5 px from the right edge**, vertically centered on a transparent
200 × 64 canvas. Banner pixels keep the supplied 512 × 128 composition.

WPF decodes PNGs and resizes with premultiplied-alpha rendering before converting
back to straight BGRA32 for DDS output. BCnEncoder.Net 2.3.0 encodes BC3. No FIFA
Editor Tool assemblies or Oodle DLLs are required to generate or export the images.

## Save and export

The source PNGs and five DDS variants are embedded in exported project folders
under `artwork/<competition object ID>/`. `project.json` records the inventory and
checksums. The encrypted `.link` includes the same files. Moving or deleting the
original imported PNGs does not affect the saved project.

At FIFAMOD export, ATLink resolves the **asset_id**, producing
`data/ui/imgAssets/<folder>/<dark|light>/l<asset_id>.dds`. A Compdata phase ID is never
used as the graphic ID. The ten files are included automatically.

The supplied FET sample confirms added-resource marker **0x56** and the FC27 launch
collector `core/chunkfiles/launch/cfc_gm_launch`. The exporter's addition allowlist
is limited to these five artwork folders and the existing limits profile. Existing
cache entries keep their own collectors. Its reader accepts exact shared payloads
and rejects inconsistent overlaps. ATLink still writes its normal uncompressed
Frostbite blocks; FIFAMOD size can therefore exceed FET's compressed example.

English/French LOC, changed Compdata and `LIVE_EDITOR_LIMITS.json` retain their existing
export behavior. The main DB remains excluded from the package and is provided separately.
Old projects without artwork remain compatible.

## Verification

Run `ATLink.UIChecks --competition-artwork <repository root>` after building the UI
checks. It covers native DDS profiles, FET identities and aliases, malformed overlaps,
tall/wide logos, tiny positioning, invalid inputs, incomplete drafts, shared IDs,
creation of three linked competitions with different images, duplication, history,
folder/`.link` reopening, WPF bindings and byte-for-byte package extraction.

The control export is recorded in `Checks/output/artwork-delivery.json`. It uses the
provided example PNGs on European Super League. The control validates file generation
and packaging; actual logo display in FC27 still requires an in-game test.
