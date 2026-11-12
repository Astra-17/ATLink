# FIFAMOD export (FC27)

In Competition studio, choose **Export FIFAMOD**, enter the mod name, author and
version, and select `fc27.cache` from the FIFA Editor Tool installation folder.
Open the current game installation in FIFA Editor Tool first to create/update
that cache. Choose a **new** output filename.

The export includes changed Compdata files and the modified localization
databases. The main game database is always excluded from the FIFAMOD.
Localization retains its existing encryption and packaging.

Open **Translations** in Competition studio to edit the selected competition's
full or abbreviated name in any installed language. Clear **Selected competition
names** to search the other localized texts by key, hash or current text. Save
each text to the project before switching languages. **Export project** preserves
these edits for reopening and writes additional language databases under `loc`.

The `.fifamod` does not contain ATLink editing metadata. Continue using **Export
project** to save a project that ATLink can reopen. Exporting a mod intentionally
does not dismiss the unsaved-project warning.

## Compatibility and limitations

- Experimental FC27 support for the FETM v3 legacy-file layout observed in the
  supplied `test sl.fifamod` (FIFA Editor Tool 2.2.1).
- Uses a FETC v4 cache for the game build, existing resource names and collector
  manifest references. The cache must match the installed game update; ATLink
  cannot independently establish that the selected cache is current.
- Every FC27 FIFAMOD export includes `LIVE_EDITOR_LIMITS.json`. The export
  uses the profile next to the source `compdata` folder, then the bundled
  FC27 profile, then the project's own profile. If the resource is absent from
  the game cache, the export adds it through the FC27 launch collector.
  FC27 Live Editor applies the profile in game.
- Competition artwork adds the ten supported legacy DDS resources through the launch collector. See COMPETITION-ARTWORK.md for the PNG imports and generated formats. Other Compdata and localization paths must already exist in the cache.
- No EBX/RES graphics, mod pictures, scripting, or other ancillary sections.
- Data uses uncompressed Frostbite blocks, so exports are larger than exports
  using FIFA Editor Tool's native compression. No native codec or proprietary
  editor/manager DLL is included or required at runtime.
- Every generated file is reopened, all resource SHA-1 hashes are verified,
  and each payload is extracted and compared byte for byte with the intended
  export before the final filename becomes visible.
- Import into FIFA Mod Manager and playability in game have **not** been tested.
  The user requested completing this change without controlling Mod Manager.

## Binary layout notes

The supported subset has `FETM`, a one-byte version `3`, .NET length-prefixed UTF-8
strings, the game code and seven game-build bytes. Metadata is followed by empty
ancillary sections, header/data bounds, and legacy chunk records. Records contain
a deterministic chunk GUID, SHA-1 of stored blocks, flags `0x16` (existing resource) or `0x56` (supported added resource), data offsets and
sizes (7-bit encoded), the lower-case FNV-1 64-bit path hash, path and target
superbundle. Collector manifest GUIDs and superbundles come from the cache.

Frostbite blocks have two big-endian 32-bit header words: original size, then
method/guard bits/stored size. ATLink emits method 0, guard bits 7 and blocks of
up to 65,536 bytes. This avoids an external compression dependency for payloads.
The cache itself is streamed using the ZstdSharp.Port package.

## Checks

Run the reference, corruption, real-cache, database/localization, non-overwrite,
missing-resource and block-boundary checks:

```powershell
dotnet run --project Checks/ATLink.Checks.csproj -- --fifamod . "--fet-cache=C:\path\to\FIFA Editor Tool\fc27.cache"
```

The full integration checks use the local `SuperLeague-FC27-J-November` export
and `test sl.fifamod` fixtures. Without a cache argument, the real-cache tests are
explicitly reported as skipped.
