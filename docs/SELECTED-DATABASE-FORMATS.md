# Fichiers chargés avec Selected DB

Contrôle local du 17 novembre 2026, avec les métadonnées FC27 installées.

- Une base brute `.db` est ouverte directement.
- Un Squad FBCHUNKS est ouvert en conservant son enveloppe. Les dictionnaires
  de noms et pays manquants sont lus dans la base intégrée, sans être ajoutés
  aux tables exportées du Squad.
- Une sauvegarde carrière `SaveType_Career` est détectée par son contenu.
  Les buffers Zstandard sont reconnus par leur signature et leurs deux tailles
  UInt64 (décompressée/compressée). La lecture impose une limite de 128 Mio.
  Une seule base contenant `players`, `teams` et `teamplayerlinks` doit être
  lisible avec les métadonnées du jeu sélectionné.

## Carrière : périmètre

Seule la base football est exposée. Les autres bases et sections de progression
carrière ne sont pas fusionnées. L’export produit une base `.db` autonome ;
il ne reconstruit pas une sauvegarde carrière jouable. Le rappel apparaît dans
l’interface. Les fichiers d’origine sont préservés.

## Fichiers contrôlés

| Fichier FC27 | Tables exposées | Joueurs | Équipes |
| --- | ---: | ---: | ---: |
| Squads20261107005754706 | 82 | 22 099 | 870 |
| CmMgrC20261112203530317 | 27 | 22 099 | 870 |

Contrôles : `--selected-database` dans Checks et Checks/UI. Ils vérifient la
lecture via StudioData.OpenSelected, les noms de référence, les éditeurs joueur
et équipe, l’affichage français/anglais, la modification d’une note, l’export et
la réouverture. Le Squad non modifié reste identique octet pour octet.
La base brute intégrée reste lisible par le même parcours.

Ces contrôles ne constituent pas une validation de chargement dans le jeu.
