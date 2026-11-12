# Extraire les FIFAMOD

Double-cliquez sur **Extraire.cmd** pour choisir plusieurs mods, ou glissez des
fichiers `.fifamod` sur ce lanceur. Chaque mod produit son propre dossier à côté
du fichier source. Un suffixe protège les extractions déjà présentes.

Les dossiers `ebx`, `res`, `chunk` ou `legacy` contiennent les ressources natives.
`inventory.json` indique les chemins, empreintes, tailles et textures converties
ou ignorées. Les images intégrées au mod sont placées sous `images`.

Les textures FC26 2D BC1 dont les chunks et les mipmaps sont disponibles sont
reconstruites sous `textures/dds` et `textures/png`. Les DDS conservent les mipmaps.
Les autres formats restent disponibles sous forme de ressources natives avec
une raison dans `texturesSkipped`.

## Prérequis

- Python 3, version 64 bits pour Oodle.
- Pillow pour les PNG, à installer dans le Python utilisé avec
  `python -m pip install Pillow`.
- Pour les blocs Oodle, `oo2core_9_win64.dll` de votre propre installation du jeu.
  L'outil cherche les dossiers EA SPORTS FC dans les emplacements habituels EA
  Games et GAMES. L'option `--oodle` permet de sélectionner une autre installation.

## Ligne de commande

```powershell
python extract.py "premier.fifamod" "second.fifamod" --output "C:\Exports" --oodle "C:\Jeux\EA SPORTS FC 26\oo2core_9_win64.dll"
```

`--no-textures` extrait uniquement les ressources natives.

## Compatibilité vérifiée

- FETM v1 FC26, avec blocs Oodle, sur les mods Wesley Frana et Tattoos by pcb.
- FETM v3 FC27, ressources legacy Compdata/LOC, blocs bruts ou Oodle.
- Compression des blocs prise en charge sous les codes 0 et 25.

Le lecteur v1 récupère l'index par concordance des limites de blocs, des tailles
et des SHA-1. Il exige que toutes les données soient couvertes avant de publier
le résultat. Une structure ou une compression inconnue provoque une erreur pour
le mod concerné, puis le traitement continue avec les autres fichiers.

Les limites sont de 512 Mio pour le mod et pour une ressource décompressée, et
de 8 Gio pour le total. L'outil charge le mod en mémoire. Il ne prend pas en
charge toutes les variantes FIFAMOD et ne convertit pas les modèles en FBX.
