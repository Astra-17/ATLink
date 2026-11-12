# Créer et maintenir une compétition FC27 dans ATLink

Guide de transmission aux agents — état au **12 novembre 2026**.

## 1. Référence et niveau de preuve

Ce guide concerne les fichiers FC27 fournis dans ce dépôt. Ne pas transposer
automatiquement les conversions à FC26, à une autre mise à jour ou à une autre installation.

La référence de fichiers actuelle du système européen est :

- Projet : `files/export/SL-CL-REF-55-03/`.
- Mod : `files/export/SL-CL-REF-55-03-ATLink.fifamod`.
- Parent : REF-55-02, lui-même basé sur REF-55 puis REF-54.
- Sources natives : `files/FC27/compdata/`, DB et LOC dans `files/FC27/`.

**Trois résultats différents doivent toujours être annoncés séparément :**

1. Fidélité à une référence : mêmes structures et comportements configurés.
2. Validité des fichiers : références, capacités, export/relecture et paquet contrôlés.
3. Fonctionnement dans FC27 : essai réel en carrière, rapporté par l’utilisateur.

Le recrutement dynamique et un premier prototype de montées/descentes ont fonctionné
en jeu selon l’utilisateur. Cela ne valide pas automatiquement tout le cycle complet
de renouvellement de REF-54/55. REF-55-03 est un export contrôlé, pas une certification
de plusieurs saisons. La suppression des restrictions par association (55-02) et des
réductions de primes (55-03) doit encore être confirmée en jeu.

## 2. Méthode à employer

1. Identifier le projet de départ explicitement demandé ou la dernière référence pertinente.
2. Conserver cette référence intacte. Créer une nouvelle variante dans `files/export`.
3. Charger ensemble les Compdata, la DB complète, ses métadonnées et les LOC du même jeu.
4. Décrire le format sportif, les entrées, les sorties et le cycle de saison avant d’écrire.
5. Utiliser les ViewModels et services ATLink existants ; ne pas fabriquer un autre
   générateur parallèle ni assembler les fichiers du mod à la main.
6. Appliquer les changements dans une transaction de projet.
7. Comparer tous les fichiers à la référence, rouvrir l’export et vérifier le paquet.
8. Livrer le projet, le `.fifamod`, la DB séparée si exportée et un récapitulatif précis.
9. Tester en nouvelle carrière, une modification isolée à la fois quand une cause reste incertaine.

Pour une simple prime, un stade ou un nom, ne pas reconstruire tout le tournoi.
Ne pas modifier les compétitions natives pour masquer un défaut de la compétition ajoutée.
Préserver objets internes, valeurs inconnues, tâches, ordre sémantique et qualifications dynamiques.

## 3. Points d’entrée du code

| Élément | Rôle |
|---|---|
| `Core/CompetitionProjectContext.cs` | Chargement, brouillon, transactions, Undo/Redo, règles, export projet et mod |
| `Core/FootballCompetitionGraph.cs` | Création transactionnelle des compétitions liées, références courante/existante/nouvelle |
| `Core/FootballLinkedCompetitions.cs` | Qualifications partagées, quotas, échanges saisonniers et copie de dates |
| `Core/FootballNativeNeighbours.cs` | Liens nationaux supérieurs/inférieurs et références DB résolues |
| `Core/FootballSeason.cs` | Sources et opérations de saison typées |
| `Core/CompetitionTranslations.cs` | Noms localisés et export des langues |
| `ViewModels/FootballCreationViewModel.cs` | Parcours de création et compilation du brouillon |
| `ViewModels/FootballCreationLinksViewModel.cs` | Liens configurés avant création |
| `ViewModels/FootballLinksViewModel.cs` | Lecture/édition des liens d’une compétition existante |

Utiliser `OpenProject` pour rouvrir un projet exporté : son dossier Compdata peut n’être
qu’un ensemble de fichiers modifiés. Il faut le superposer aux sources, pas considérer
les fichiers absents comme supprimés.

Opérations disponibles : `Execute`, `SetRule`, `SetTranslation`, `Export`,
`ExportFifaMod`, `PrepareFifaModFiles`. `SetRule(id, key, [])` retire la surcharge locale ;
ce n’est pas la même chose qu’une valeur explicite zéro. Vérifier aussi l’héritage.

## 4. Identités et structure native

Ne jamais confondre :

- ID d’objet Compdata (compétition, phase ou groupe).
- ID de compétition DB, généralement porté par le code `C…`.
- ID de championnat DB utilisé pour recruter les clubs.
- ID d’équipe, de stade et clé/hash de localisation.

Exemple REF-55-03 : Community League a l’objet **2000**, mais le code DB **C3**.
Le nombre 2000 ne remplace donc pas automatiquement 3 dans un lien natif DB.

`compobj.txt` organise la hiérarchie ; il ne contient pas les primes ni les dates :

```text
2000,3,C3,European Community League,84
2001,4,S1,FCE_Setup_Stage,2000
2002,5,G1, ,2001
```

- Type 3 : compétition ; type 4 : phase ; type 5 : groupe ou confrontation.
- Les `SETUP` sont nécessaires même s’ils restent cachés dans l’interface normale.
- Un groupe de coupe peut représenter une seule confrontation, pas une poule.
- Conserver les descriptions natives vides sous leur forme ` ` lorsqu’elles sont attendues.
- Garder les IDs ordonnés et les références à jour. Une réorganisation n’est pas une
  autorisation de renuméroter les compétitions existantes.
- Trier les propriétaires numériques sans bouleverser l’ordre des tâches ou des
  critères de classement d’un même propriétaire.
- `S…` et `Q…` ne suffisent pas à définir un comportement. Copier une lettre ne
  remplace pas la reproduction du mécanisme natif complet.

Formats observés : `SETUP`, `LEAGUE`, `KO1LEG`, **`KO2LEGS`**.
Pour les poules FC27, employer le mécanisme **LEAGUE + situation GROUP** ; ne pas
inventer `match_stagetype,GROUP` ni utiliser la faute `KO2LEG` dans les fichiers natifs.
Dans le système personnalisé testé, conserver `comp_type,CUP` à la racine : le passage
de LEAGUE à CUP a supprimé un crash de début juillet rapporté par l’utilisateur.
Ce résultat n’est pas une règle universelle pour tous les championnats du jeu.

## 5. Fichiers à examiner ensemble

| Fichier | Vérification indispensable |
|---|---|
| `compobj.txt` | Hiérarchie complète, phases jouées et préparations, parents et IDs |
| `compids.txt` | Enregistrement et correspondances de la compétition |
| `settings.txt` | Format, règles héritées/locales, primes, stades, dépendances, saison |
| `standings.txt` | Capacité de chaque poule, pool et confrontation ; conserver l’indexation native |
| `advancement.txt` | Chaque place entrante, classement source, gagnants et liens entre compétitions |
| `tasks.txt` | Recrutement, remplissage, opérations de début/fin de saison dans l’ordre requis |
| `initteams.txt` | Table initiale de la première édition et nombre exact d’entrées directes |
| `objectives.txt` | Objectifs de tous les groupes/tours concernés, selon le modèle compatible |
| `schedule.txt` | Dates, journées, capacités et horaires ; sources partagées |
| DB + LOC | Identités cohérentes, noms complets/abrégés et langues réellement utilisées |

Examiner également les autres fichiers présents (`activeteams`, `weather`, etc.) et
conserver ceux qui ne nécessitent pas de modification. Cette liste n’impose pas de
réécrire tous les fichiers : exporter uniquement ceux modifiés **par rapport au jeu de base**.
Un fichier identique à REF-55 mais déjà modifié par rapport au natif reste nécessaire au mod combiné.

Les essais SL ont fini par fonctionner après complément des objectifs et des équipes
initiales. Ne pas créer seulement compobj/settings/schedule et annoncer le tournoi terminé.
Ne pas déduire pour autant qu’une ligne isolée d’objectifs explique tous les crashs.

## 6. Participants et qualifications

Écrire le bilan des places pour chaque phase : entrants directs + qualifiés = capacité.
Ne pas traiter une place vide comme un exempt implicite.

Exemple actuel : **18 clubs directs + 2 vainqueurs de barrage = 20 clubs de Community League**.
Ne pas charger 20 clubs directs puis essayer d’en ajouter deux.

Une entrée dans `initteams.txt` initialise une table ; elle ne remplace pas tous les
remplissages des groupes. Le mécanisme actuel charge la table ainsi :

```text
2000,start,FillFromCompTable,2002,2000,18,0
```

Les entrées 1–9 de l’allocation vont au groupe A, 10–18 au groupe B ; le vainqueur
1998 rejoint l’allocation en 19 puis A10 ; le vainqueur 1999 rejoint l’allocation en 20 puis B10.

`advancement.txt` contient des liens source/rang/destination/place. Le rang 0 utilisé
par les pools de tirage est conservé selon le mécanisme testé ; ne pas le transformer
en rang de classement inventé. Après une confrontation, le rang 1 transmet son vainqueur.

Pour un tableau fixe, chaque vainqueur doit alimenter exactement la place prévue.
Les tours suivants conservent des sources dynamiques, pas des noms d’équipes figés.
Un aller-retour est une confrontation native unique avec deux manches.

### Recrutement dynamique des 64 clubs

Deux pools de 32, remplis par **21 tâches `FillFromLeagueMaxFromCountry`**, A avant B.

| Pays | Ligue DB | A | B |
|---|---:|---:|---:|
| Angleterre | 13 | 6 | 0 |
| Espagne | 53 | 5 | 0 |
| Italie | 31 | 5 | 0 |
| Allemagne | 19 | 5 | 0 |
| France | 16 | 4 | 0 |
| Portugal | 308 | 3 | 0 |
| Pays-Bas | 10 | 2 | 2 |
| Belgique | 4 | 1 | 2 |
| Turquie | 68 | 1 | 2 |
| Écosse | 50 | 0 | 4 |
| Autriche | 80 | 0 | 3 |
| Suisse | 189 | 0 | 3 |
| Danemark | 1 | 0 | 3 |
| Norvège | 41 | 0 | 3 |
| Suède | 56 | 0 | 3 |
| Pologne | 66 | 0 | 3 |
| Irlande | 65 | 0 | 2 |
| Roumanie | 330 | 0 | 2 |
| Total | | 32 | 32 |

Exemple : les Pays-Bas ont un plafond cumulé de quatre, malgré deux places par branche :

```text
1926,start,FillFromLeagueMaxFromCountry,1928,10,2,4
1926,start,FillFromLeagueMaxFromCountry,1929,10,2,4
1926,advance_teamcompdependency,2011
1926,advance_teamcompdependency,2000
```

Les deux dernières lignes appartiennent à **settings.txt**, les deux premières à **tasks.txt**.
Les dépendances excluent les participants des divisions dans le montage testé ; les
quotas et plafonds, avec A avant B, ont produit une sélection sans doublons rapportée
par l’utilisateur. Ne pas prétendre que l’ordre seul prouve ce comportement.
Vérifier les 64 clubs effectifs, les classements utilisés et le renouvellement en saison suivante.
Une place vacante doit être signalée ; aucune réattribution nationale silencieuse.

## 7. Système actuel et passage de saison

| Compétition | Objet / code | Déroulement |
|---|---|---|
| Community League Qualifiers | 1926 / C5 | Deux branches indépendantes de 32, cinq tours simples partagés visuellement, deux vainqueurs |
| European Community League | 2000 / C3 | Préparation, deux poules de 10, demi-finales croisées aller-retour, finale simple |
| European Super League | 2011 / C2 | Préparation, deux poules de 10, huitièmes, quarts, demi-finales et finale simples |

Les qualifs sont désormais une compétition distincte, mais leurs deux gagnants rejoignent
Community League **dans la même édition**, via `advancement.txt`, pas par une écriture
de table destinée à la saison suivante.

Les deux premiers de chaque poule CL se croisent A1–B2 et B1–A2. Les **deux vainqueurs
des demi-finales** montent en SL ; la finale reste jouée pour le titre et la prime.

Pour la saison suivante :

- SL : les rangs 1–9 de chaque poule restent (18), les deux promus CL complètent (20).
- CL : les deux demi-finalistes perdants + les rangs 3–9 des deux poules restent (16).
- Les derniers de chaque poule SL descendent en CL (18 entrées directes).
- Les derniers de chaque poule CL sortent ; deux nouveaux vainqueurs des qualifs
  complètent les poules de l’édition concernée (20).

Exemples exacts dans `tasks.txt` :

```text
2000,end,UpdateTable,2011,2007,1,19
2000,end,UpdateTable,2011,2008,1,20
2011,end,UpdateTable,2000,2015,10,9
2011,end,UpdateTable,2000,2016,10,18
```

Lecture : propriétaire/événement/action/table destination/groupe source/rang/place destination.
`UpdateTable` prépare une table persistante utilisée par `FillFromCompTable` ; ce n’est
pas une instruction universelle qui réécrit automatiquement le fichier source `initteams.txt` sur disque.
La première édition part de `initteams`, les suivantes nécessitent des écritures de
maintien/promotion/relégation complètes et sans doublons de destination.

Ne pas confondre les liens nationaux `info_league_promo/releg` (références DB) avec ces
échanges personnalisés via tâches (objets Compdata). Utiliser les adaptateurs typés.

## 8. Calendrier et fenêtres internationales

Les dates relatives fournies ont été calibrées sur Community Shield : offset 231 =
16 août 2026, soit une origine **28 décembre 2025**, distincte du début de saison jouable.
Conserver la référence du manifeste ; ne pas extrapoler une formule universelle aux autres années.

Une ligne de `schedule.txt` indique objet, offset, journée, capacité minimale, maximale, HHmm.
Un créneau de capacité n’est pas une affiche confirmée. Plusieurs horaires d’une même
journée ne sont pas plusieurs journées pour chaque club.

Calendrier actuel :

- Qualifs : 18, 21, 24, 27, 29 juillet 2026 ; branche A 20 h, B 21 h.
- Poules SL/CL, 21 h : 19/08, 03/10, 07/10, 15/11, 18/11/2026 ;
  28/03, 31/03, 24/05, 02/06/2027.
- SL : huitièmes 07/06, quarts 10/06, demies 13/06, finale 16/06/2027.
- CL : demies aller/retour 10 et 13/06, finale 16/06/2027, à 21 h.

Les qualifs internationales peuvent reprendre les dates d’une autre compétition via
`schedule_use_dates_comp` (exemple natif `119,schedule_use_dates_comp,1901`). Ne pas
afficher leurs seuls créneaux bruts comme leurs dates effectives. Résoudre les liens
et préserver les dépendances ; leur changement peut réintroduire amicaux et qualifs
simultanés pour une même sélection. Les exemptions expliquent aussi qu’une sélection
ne joue pas à chaque date d’une journée.

La suppression de plages d’amicaux dans les premières variantes a été associée à
des régressions en carrière (internationaux ne revenant pas, coupes européennes cassées).
Ne pas reprendre ces suppressions comme solution validée.
`schedule_internationaldependency,NONE` sur la SL a permis d’afficher ses dates dans
les essais, mais ne garantit pas la disponibilité des joueurs convoqués.

Déplacer des dates en une passe depuis les dates originales ; préserver journée,
heure, capacité et liens. Distinguer masculin/féminin et amicaux/qualifications.
Ne pas altérer les calendriers de clubs natifs pour rendre une simulation possible.
La simulation ATLink n’est pas le moteur de carrière et ne prédit pas tous ses reports.

## 9. Primes, stades et discipline

Primes actuelles (`info_prize_money` sur chaque phase jouée) :

| Compétition | Poules | Huitièmes | Quarts | Demies | Finale / barrages |
|---|---:|---:|---:|---:|---:|
| European Super League | 100 M | 10 M | 20 M | 25 M | 45 M |
| European Community League | 25 M | — | — | 30 M | 45 M |
| Community League Qualifiers | — | — | — | — | Barrages 25 M |

L’utilisateur a observé **22,5 M au perdant de la finale CL** avec prime 45 M et
`info_prize_money_drop,50`. Cela établit cette distribution dans ce cas, pas une loi
vérifiée pour tous les rangs de poule, tous les formats ou un total cumulé.
Sur demande, REF-55-03 retire toutes les surcharges `info_prize_money_drop` des trois
compétitions ajoutées (trois lignes CL). Le défaut global natif vaut zéro et est conservé.
Ne pas réintroduire ces réductions en recopiant un modèle technique.

Stades : `groupeDeConfrontation,match_stadium,stadeDB` permet un stade par confrontation
de coupe. Deux demi-finales peuvent donc utiliser deux stades. Un groupe d’aller-retour
porte les deux manches : la surcharge s’applique aux deux. Ne pas inventer de colonne
de stade dans les fichiers de matchs, ni modifier les terrains habituels des clubs.
Pour une poule qui comporte plusieurs rencontres, un stade de groupe n’est pas un
stade individuel par affiche. Voir `TIE-VENUES.md`.

Effacer les jaunes en conservant les suspensions : aucune conversion fiable établie
dans les données disponibles. Ne pas convertir `rule_suspension` en simple interrupteur.

## 10. Pièges et causes à ne pas confondre

- **Limites moteur** : l’utilisateur a fait passer REF-40 avec des limites augmentées.
  Inclure le profil fourni ; compter objets/lignes/ressources selon ses catégories,
  et ne pas inventer une seule limite globale pour tous les fichiers.
- **Capacité déjà complète** : ne pas ajouter des vainqueurs à 20 places déjà remplies.
- **Phases finales et préparations** : copier un modèle complet, pas uniquement un
  libellé `PLAYOFF`, `QUALIFY`, `ROUNDX`, `GROUP` ou un nom de tour.
- **Copie native** : les essais Libertadores ont fourni une base jouable ; les variantes
  à davantage de tours ont exposé d’autres contraintes. Ne pas proclamer qu’un modèle
  Libertadores ou Conference fonctionne pour n’importe quelle transformation.
- **Restrictions d’association** : Como avait gagné sa demi-finale mais apparaissait
  comme adversaire à déterminer face à Bergamo, autre club italien. Les liens vers le
  barrage étaient présents. REF-55-02 retire `1992,advance_maxteamsassoc,1` et
  `1997,advance_maxteamsassoc,1`. Cause plausible, correction encore à confirmer en jeu.
  Ne pas confondre cette restriction de progression avec les quotas du recrutement.
- **Crash division par zéro** : un dump a montré une division par zéro ; sans symboles
  du jeu cela ne prouve pas quelle règle Compdata l’a provoquée. Éviter les diagnostics affirmatifs.
- **Référence modifiée à la main** : vérifier les vrais fichiers chargés et le paquet,
  pas seulement le nom REF d’un dossier historique.

## 11. Export reproductible

Cache demandé pour cette série :

```text
C:\Users\melvi\Documents\Melvin\UTILITAIRE\FIFA\FIFA Editor Tool v2.2.2\fc27.cache
```

Profil à intégrer à chaque mod : `files/FC27/LIVE_EDITOR_LIMITS.json`, à la ressource :

```text
dlc/dlc_FootballCompEng/dlc/FootballCompEng/data/compdata/LIVE_EDITOR_LIMITS.json
```

- Utiliser `Export` pour le projet réouvrable et `ExportFifaMod` pour le paquet.
- Exporter les fichiers modifiés par rapport au natif uniquement, sans ajouter de BOM UTF-8.
- Inclure les LOC nécessaires, **anglais et français** pour les essais actuels.
- Mettre à jour noms complets et abrégés : changer uniquement compobj ne suffit pas en jeu.
- Ne pas inclure `data/db/fifa_ng_db.db` dans le `.fifamod` ; fournir la DB mise à jour séparément.
  Cela ne signifie pas qu’une identité DB n’est jamais nécessaire à une autre installation.
- Ne pas déplacer les clubs de leur championnat domestique.
- Rouvrir le paquet, extraire ses ressources, vérifier leurs octets, les noms et le profil.
- Ne jamais écraser un export témoin ; nouveau dossier et nouveau nom de mod.

## 12. Contrôles avant livraison et test en carrière

Contrôles de fichiers :

- IDs uniques, parents existants, références DB/Compdata séparées et résolues.
- Capacité = places entrantes ; chaque place unique ; absence de place perdue ou doublée.
- 32 + 32 entrants et 31 matchs par branche ; branches non croisées ; deux sorties finales.
- 18 + 2 = 20 CL ; 20 SL ; classements, objectifs et tables initiales complets.
- Ordre des tâches, quotas/plafonds et dépendances préservés.
- Dates, conversion inverse, horaires, repos et capacités cohérents ; pas de BOM ajouté.
- Primes et exceptions de stade vérifiées ; paramètres inconnus conservés.
- Diff de tous les Compdata, DB et LOC pertinents ; sources intactes.
- Export/relecture puis, si nécessaire, réexport de contrôle.
- Historique et annulation pour toute évolution de l’application.

Suites disponibles, à choisir selon la modification :

```powershell
dotnet build ATLink.csproj --no-restore -v quiet
dotnet build Checks/ATLink.Checks.csproj --no-restore -v quiet
dotnet Checks/bin/Debug/net10.0/ATLink.Checks.dll --competition-parity .
dotnet Checks/bin/Debug/net10.0/ATLink.Checks.dll --phase-settings .
dotnet Checks/bin/Debug/net10.0/ATLink.Checks.dll --scheduling .
dotnet build Checks/UI/ATLink.UIChecks.csproj --no-restore -v quiet
dotnet Checks/UI/bin/Debug/net10.0-windows/ATLink.UIChecks.dll --linked-competitions .
```

Éviter `DesignTimeBuild=true` pour compiler l’application : ce contournement a produit
des inclusions de code généré en double. Les tests réussis ne remplacent pas FC27.
Les petits outils dans `Checks/output/ref55-tool` sont des outils d’export historiques,
pas une API de production à reproduire aveuglément.

Test en jeu : nouvelle carrière avec la variante seule, mêmes réglages que le témoin ;
vérifier recrutement, chaque tour, arrivée des qualifiés en août, finale, primes,
calendriers natifs, retour des internationaux, puis passage de saison et deuxième recrutement.
Une carrière dont les affiches sont déjà générées peut ne pas refléter une correction du mod.

Dans la livraison, écrire exactement ce qui a changé, ce qui a été vérifié, les risques
restants et ce que l’utilisateur doit relever. En cas d’échec, repartir d’un témoin
jouable et isoler une seule différence plutôt que cumuler des réglages supposés.

## 13. Lectures complémentaires

- `COMPETITION-STUDIO.md` : parcours de l’éditeur.
- `COMPETITION-PARITY.md`, `DBM-COMPETITION-AUDIT.md` : adaptations et références DBM.
- `FC27-CALENDAR-ORIGIN.md` : calibration des dates.
- `PHASE-SETTINGS.md`, `TIE-VENUES.md`, `MATCHUPS.md` : règles, stades et tableaux.
- `FIFAMOD-EXPORT.md` : format et contrôles du paquet.
- `files/export/SL-CL-REF-54/README.md` : tables de maintien/promotion/relégation.
- `files/export/SL-CL-REF-55-03/README.md` : derniers changements de primes.

Les audits anciens conservent leur valeur historique, mais peuvent présenter des
hypothèses ensuite dépassées. Ce guide distingue les observations utilisateur des
preuves de fichiers ; le mettre à jour lorsqu’un nouvel essai en jeu confirme ou réfute une piste.

## 14. Recréation guidée intégrée

Le parcours normal dispose maintenant des primes/règles des cinq tours qualificatifs,
des noms anglais/français à la création, des dates de tirage et d’un éditeur de journées
internationales. Les 18 entrants directs peuvent être sélectionnés sans deux clubs
temporaires lorsque les deux places qualificatives sont configurées dans le même brouillon.

`docs/RECREATE-REF55.md` donne les étapes exactes. Le test UI `--ref55-recreation`
construit un nouveau projet depuis le natif, exporte un FIFAMOD de contrôle et compare
tous les Compdata à REF-55-03 après normalisations explicites. Les données de référence
servent d’entrées de test, pas de fichiers importés pour fabriquer le résultat.

Le rapport distingue l’équivalence métier des différences documentées : présentation
des étiquettes, emplacement des valeurs héritées, créneaux par phase/confrontation et
écritures indépendantes de tables saisonnières. Il signale aussi huit références natives
`schedule_refcomp` altérées dans les anciens exports : la nouvelle création garde les
valeurs du jeu de base. Ne pas annoncer une identité octet par octet avec REF-55-03.

Les nouveaux modèles restent paramétrables : aucun préréglage ne recopie secrètement
le mod. Les fenêtres internationales ont leur propre brouillon et participent à
l’historique du calendrier puis à la transaction commune de création. Les consultations
des compétitions existantes ne suppriment aucune règle sans action explicite.
