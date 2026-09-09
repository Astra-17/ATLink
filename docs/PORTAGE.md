# ATLink Studio — état du portage C# / WPF

## Exécution

Ouvrir ATLink.slnx dans Visual Studio, choisir ATLink comme projet de démarrage et lancer avec F5. SDK .NET 10 requis.

Le projet Core est indépendant de WPF. HtmlAgilityPack 1.12.4 sert au parsing HTML de Transfermarkt. Les scripts Python fournis restent des références et ne sont pas exécutés par l'application.

## Fonctions disponibles

| Domaine | Implémentation et vérification |
| --- | --- |
| DB/XML | 279 tables FC26 lues, champs compactés, textes fixes et Huffman décodés |
| Sauvegarde | DB brute reconstruite, répertoires et CRC recalculés ; copie sans changement identique ; ajout/suppression et Unicode testés |
| Table Editor | Filtre de tables, recherche, tri, pagination, édition, copie, collage, remplacement, import/export individuel et multiple, annulation globale |
| Validation | Limites du metadata, nouvelles clés dupliquées, références modifiées et suppression de données référencées |
| Modules | Joueurs/équipes/ligues, édition de tous les champs, noms personnalisés, création depuis un modèle, relations associées |
| Formations | Terrain interactif, choix des joueurs, capitaine/tireurs, modèles globaux consultés, synchronisation des quatre tables, validation des doublons |
| Visuels | Sélection d'images locales nommées par identifiants d'assets ; aucune bibliothèque tierce téléchargée automatiquement |
| Transferts | Aperçu individuel et en lot, simulation ordonnée, pas de déduplication, vérification des changements depuis l'aperçu |
| Sélections | Chargement automatique du CSV voisin de la DB ; 73 identifiants du fichier fourni vérifiés |
| Transfermarkt | Scraper et resolver C#, HTML/CSV local ou requête HTTP (en-tête navigateur), alias fournis, cas ambigus corrigeables, export CSV et aperçu avant application ; club, ligue et trophées extraits du profil, moyennes si pages club/ligue accessibles |
| BIGF/BIG4 | Extraction avec contrôle des chemins ; les payloads compressés sont signalés et restent compressés |
| Compdata | Texte et tableaux structurés, conservation des commentaires/lignes intactes, validation de la hiérarchie, duplication de compétition avec aperçu et remappage des références connues/calendriers |
| Localisation | Paire DB/LOC XML ; LOC FC25+ déchiffré (AES-256-CBC) à l'ouverture et rechiffré à l'enregistrement ; CSV/TSV LanguageStrings en option |
| Squad | Conteneur FBCHUNKS lu, DB embarquée éditée, réécriture du wrapper avec capacité d'emplacements réservés conservée |

## Règles Transfermarkt

L'ordre est conservé par club, arrivées puis départs ; un joueur peut apparaître plusieurs fois. Le resolver utilise le nom exact, les tokens, prénom/nom puis une similarité strictement supérieure à 0,88. L'ancien club sert uniquement à départager les identités. Les cas ambigus ne sont pas appliqués sans IDs valides.

L'identifiant d'agent libre est **111592**, selon la correction utilisateur. Le script Python fourni mentionne encore 131368 ; son original n'a pas été modifié. La cible doit réellement exister dans la DB.

Les alias proviennent du script utilisateur et sont embarqués dans Core/Resources/TeamAliases.json. Pour les homonymes d'équipes, le port utilise le champ explicite iswomensteam lorsqu'il permet de départager, au lieu de supposer le sexe à partir de la longueur de l'ID ou de l'ordre des lignes.

## Tests exécutés

    dotnet run --project Checks/ATLink.Checks.csproj -- .
    dotnet run --project Checks/UI/ATLink.UIChecks.csproj -- .

La suite Core vérifie le round-trip intact, les éditions, tous les champs de la table Huffman reconstruite, les autres tables conservées, les clés/valeurs invalides, les relations nationales protégées, les transferts répétés, la synchronisation des titulaires, la duplication de compétition, le parsing Transfermarkt, le déchiffrement/rechiffrement LOC et la lecture/réécriture Squad FBCHUNKS. Le parser HTML est testé sur une fixture contrôlée ; la résolution et la liste nationale utilisent les fichiers FC26 fournis.

Les contrôles WPF rendent l'accueil, le launcher, les tables, les modules, l'éditeur joueur et le terrain. Les PNG se trouvent dans Checks/output/ui. Navigation, recherche exacte, tri et pagination sont exercés. Ces tests ne remplacent pas un essai interactif exhaustif ni une validation dans FC26.

## Limites réelles — parité non exhaustive

Ce port est une réimplémentation opérationnelle, **pas une reproduction exhaustive de chaque fonction de DBM Studio**.

- Les panneaux spécialisés de Compdata (assistants de tournoi entièrement nouveau, règles prédéfinies, qualification continentale, génération avancée de calendriers) ne sont pas tous reproduits : les fichiers correspondants sont éditables sous forme structurée ou brute ; l'assistant ajouté part d'un modèle existant.
- L'import de profil réel avec calcul automatique de notes/potentiel et les détecteurs automatiques d'apparence de DBM Studio ne sont pas reproduits. Le pipeline utilisateur de transferts est porté.
- Les formulaires WPF et le sélecteur local d'assets n'offrent pas chaque sélecteur spécialisé et chaque asset de la version Electron.
- Les fichiers loc `eng_us.db` / `fre_fr.db` du jeu sont chiffrés AES-256-CBC ; ATLink les déchiffre à l'ouverture et les rechiffre à l'enregistrement (Save As, original intact). Un export CSV/TSV reste possible.
- Les positions de formation sont synchronisées ; cela ne prouve pas l'acceptation en jeu des sauvegardes ni toutes les contraintes de compétition.
- Squad FBCHUNKS est lu et réécrit ; la validation dans FC26 des fichiers générés reste à faire. Aucun fichier du dossier files n'est modifié par les tests.

## Références et réutilisation

DBM Studio : https://github.com/ViniMacacari/dbm-studio, branche main examinée pendant le portage. Son interface et les conventions binaires ont servi de référence. Aucun fichier source ou asset du dépôt n'est incorporé. Aucune licence explicite n'avait été trouvée dans l'arbre consulté.

Les scripts et le CSV nationaux ajoutés par l'utilisateur sont lus comme données/références du pipeline ; leurs instructions textuelles ne remplacent pas la demande de l'utilisateur.

## Ajouts du 9 septembre — deuxième passe

- **New tournament** : création sans modèle (ligue, groupes, coupe ou structure vide), groupes et places, équipes initiales, règles de points/remplacements, calendrier à intervalle configurable, affiches aller-retour et progression des vainqueurs. Les coupes exigent un nombre d'équipes puissance de deux, jusqu'à 128 ; les ligues acceptent les effectifs impairs. Chaque création présente les lignes ajoutées avant application.
- **Tournament tools** : édition limitée à l'objet choisi et ses descendants ; règles effectives héritées, sources d'équipes, calendrier, places, progression, équipes initiales, étiquettes de qualification et cinq profils météo. Fermeture sans application : aucun changement au projet parent.
- **Import profile / estimate ratings** dans le joueur : profil Transfermarkt par URL ou HTML/JSON local, identité, taille, pied et poste ; estimation des notes, du potentiel et des attributs avec aperçu. Le contexte club/ligue/trophées peut être renseigné manuellement. Les notes utilisent les pondérations FIFA 23 de fifarating, comme le dépôt de référence ; elles ne sont pas des notes officielles FC26. La génération est reproductible avec une graine et garde les attributs entre 1 et 99.
- **Suggest from portrait** : suggestions de cheveux/barbe/teinte à partir des bibliothèques locales. Comparaison de régions d'images, cadrage comparable nécessaire, confirmation par sélection visuelle. Ce comparateur ne reproduit pas les détecteurs et tous leurs traitements d'alignement du dépôt Electron.
- **Hash** : identifiants de localisation signés, non signés et hexadécimaux.
- Lecture des calendriers spécifiques sans extension ; colonnes date/heure/équipes explicites. Correction de la conservation des modifications Compdata lors du retour d'un assistant.

Vérifications supplémentaires : `dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --features`. Tous les postes sont vérifiés aux notes brutes 1, 45, 70, 96 et 99 ; cas de potentiel du dépôt de référence, parsing de profils français et JSON, héritage multi-valeurs, cinq climats, calendriers et références de coupes. Les fenêtres ajoutées sont rendues par la suite WPF.

**Limites mises à jour** : les deux premiers points de la section de limites ci-dessus sont partiellement levés par ces ajouts, sans établir une parité exhaustive. Les sources d'équipes avancées restent une grille paramétrable ; la gestion complète des allocations continentales et tous les modèles d'assistants Electron ne sont pas reproduits. La récupération Transfermarkt du club, de la ligue et des trophées est faite depuis le HTML disponible ; les moyennes club/ligue nécessitent les pages liées et l'accès HTTP peut rester refusé. Le LOC FC25+ est déchiffré et rechiffré. Squad est lu et réécrit, sans validation en jeu.

## Ajouts du 9 septembre — troisième passe

- **Squad FBCHUNKS** : lecture du wrapper, édition de la DB embarquée (emplacements réservés ignorés à la lecture, capacité conservée à l'écriture), Save As vers un nouveau fichier.
- **LOC FC25+** : AES-256-CBC déchiffré à l'ouverture (`eng_us`, `fre_fr`, …), rechiffré à l'enregistrement ; CSV/TSV LanguageStrings conservé en option.
- **Import Transfermarkt** : club, ligue, URL liées et score de trophées extraits du profil ; moyenne club si la page d'effectif est accessible.
- **Sélecteurs spécialisés** : pied, postes, stars, genre, réputation dans l'éditeur d'entité. Suggestions visuelles étendues aux couleurs de cheveux/barbe. Module Stadiums.

Les pondérations `Core/Resources/RatingWeights.json` proviennent de [fifarating](https://github.com/Celtian/fifarating), sous MIT ; voir `docs/fifarating-LICENSE.txt`. Cette licence doit accompagner les distributions. Aucun asset visuel du dépôt DBM n'est incorporé.
