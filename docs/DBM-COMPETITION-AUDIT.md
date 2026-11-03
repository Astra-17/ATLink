# Audit de la création de compétitions — ATLink / DBM Studio

> Rapport initial, avant correction. Le moteur actuel et ses tests sont décrits dans [COMPETITION-PARITY.md](COMPETITION-PARITY.md). Le tableau ci-dessous conserve les constats initiaux ; le JSON et les extraits peuvent être régénérés avec la version actuelle.

La création ATLink n’est pas équivalente à celle de DBM Studio. L’interface simplifiée s’appuie sur un compilateur propre à ATLink. La structure des fichiers est commune, mais le parcours ne reprend pas toutes les fonctions métier de DBM.

Référence vérifiée : `main` DBM Studio, commit `1828c34e9650875a5b6befdf493854f8e4fbcfe1`. La copie locale correspondait au résultat de `git ls-remote`. L’audit compare les sources et produit trois compétitions ATLink en mémoire ; il ne lance pas l’application DBM et ne constitue pas un test dans FC27.

## Résultats reproduits

Commande : `dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --dbm-competition-audit .`

Résultats détaillés : [JSON de l’audit](../Checks/output/dbm-competition-audit/results.json). Des extraits des fichiers générés, limités aux nouveaux objets, sont rangés à côté du JSON.

| Cas ATLink | Phases jouées | Tâches de début | Tâches de fin | Entrées initteams | Erreurs internes |
|---|---:|---:|---:|---:|---:|
| Championnat de 8 équipes, aller-retour | 1 | 8 | 0 | 0 | 0 |
| Coupe de 16 équipes, match simple | 4 | 16 | 0 | 0 | 0 |
| 4 groupes de 4 puis élimination, 2 qualifiés par groupe | 4 | 16 | 0 | 0 | 0 |

Les fichiers sources FC27 ont été comparés octet par octet avant et après : aucune modification. La DB et le LOC d’origine restent inchangés. Les chiffres de ce tableau décrivent ATLink ; ils ne signifient pas que DBM crée automatiquement des tâches de fin sans configuration de l’utilisateur.

## Écarts à traiter

### 1. Tableau à élimination directe différent — divergence confirmée

Dans la coupe à 16 équipes, les quarts ATLink reçoivent les vainqueurs des huitièmes 1–8, 2–7, 3–6, 4–5. Le générateur automatique DBM associe les rencontres adjacentes : 1–2, 3–4, 5–6, 7–8.

Cause : `FootballCompetitionCompiler.Compile` applique une inversion des têtes de série pour toute transition vers une phase à élimination, y compris entre deux tours à élimination. DBM utilise `Math.floor(slotIndex / 2)` dans `autoGenerateKnockoutRules`.

Les deux tableaux peuvent être cohérents sportivement. L’écart prouve une absence de parité, pas à lui seul un fichier invalide. Pour retrouver DBM, traiter séparément la distribution après groupes et la progression entre tours à élimination ; préserver toute qualification personnalisée.

### 2. Cycle des saisons non repris dans le parcours normal — couverture incomplète

DBM permet d’ajouter des sources dynamiques et des tâches de fin, notamment `UpdateTable` et `UpdateLeagueTable`. Ses services traitent aussi `ClearLeagueStats` et `UpdateLeagueStats`.

Le créateur football ATLink reçoit seulement une liste de clubs et génère des `start,FillWithTeam`. Il ne reçoit pas de modèle de tâches de début/fin. Le moteur bas niveau/Expert possède plusieurs de ces actions, mais elles ne sont pas raccordées au nouveau parcours de création.

Exemples des fichiers FC27 fournis : la Premier League contient `ClearLeagueStats`, plusieurs `end,UpdateTable` et `end,UpdateLeagueStats` ; la FA Cup contient des mises à jour du classement final. Les nouvelles compétitions testées n’en contiennent aucune.

Conséquence possible : une compétition affichée correctement peut ne pas alimenter les classements persistants, qualifications ou mises à jour de saison attendus. Le besoin exact doit être défini par format ; copier aveuglément les tâches d’une autre compétition déplacerait des références vers des ligues/groupes incorrects.

### 3. Classement initial distinct des participants — adaptation non explicitée

DBM écrit la sélection initiale dans `initteams.txt` et propose séparément les règles de remplissage de `tasks.txt`. ATLink crée les participants via les tâches de remplissage, mais ne prépare pas de classement initial dans le nouveau compilateur.

La séparation actuelle évite de traiter des résultats historiques comme une liste de participants. Elle doit rester. En revanche, une création destinée à alimenter des qualifications basées sur le classement antérieur doit pouvoir définir explicitement son état initial, avec des positions à base zéro. L’absence d’initteams n’est pas déclarée invalide pour tous les formats.

### 4. Promotions, relégations et paramètres de saison — fonctions DBM manquantes dans le créateur simplifié

DBM expose le mois de début de saison et les liens de promotion, relégation et barrages. Le nouveau catalogue ATLink n’expose pas ces liens dans le parcours normal.

Le test de championnat utilise la Premier League comme profil DB. Le nouveau championnat n’a pas son paramètre local `info_league_releg=14`. Ce n’est pas une raison de recopier automatiquement cette valeur : elle le relierait à une compétition existante sans choix de l’utilisateur. Il manque une configuration sportive explicite et une vérification des références.

Les paramètres effectifs observés sont détaillés dans `EffectiveValueDifferences` du JSON. Le changement d’`asset_id` correspond à la nouvelle identité DB ; les ressources graphiques associées ne sont pas créées par cette affectation.

### 5. Calendrier et phase de préparation — fonctionnement différent, validation à compléter

DBM propose génération, édition manuelle et matchs attribués ; le calendrier peut être ignoré et la phase de préparation incluse ou exclue. Son service utilise une date de référence modifiable, avec une valeur par défaut historique `2011-12-25`.

Le compilateur football ATLink recrée actuellement des créneaux à 15:00 espacés de sept jours. La référence illustrative de l’application est le 01/07/2026. Une nouvelle phase de préparation est nommée S0, sans créneau de calendrier ; DBM commence son modèle de coupe par S1. Les fichiers natifs FC27 examinés n’emploient pas S0 pour leurs SETUP.

Ces constats ne démontrent pas que S0 ou l’absence de créneau bloque le jeu. Ils doivent être validés explicitement. La date historique de DBM ne doit pas être reprise comme vérité FC27. Il manque surtout une conversion de saison vérifiée et des tests sur les calendriers natifs.

### 6. « Profil compatible » ne signifie pas modèle complet validé en jeu

Les profils ATLink sont sélectionnés d’après la présence de l’identité DB, les formats natifs connus et la lisibilité structurelle des phases. Le compilateur reconstruit ensuite la structure sportive. Il ne clone pas l’ensemble des réglages, tâches et intégrations du modèle.

L’appellation ne doit donc pas être interprétée comme une certification de fonctionnement FC27. Les contrôles actuels valident les références et les fichiers, sans couvrir le cycle complet d’une saison.

## Mécanismes communs ou adaptations justifiées

- Hiérarchie objets compétition → phases → groupes et inscription dans compids.
- Classements de groupes indexés à partir de zéro ; positions de qualification à partir de un.
- Réglages multi-valeurs, ordre des départages et héritage des règles.
- Tâches `FillWithTeam` et références vers les groupes d’allocation.
- ATLink prépare en plus des insertions DB/LOC ; le créateur Compdata DBM étudié ne prépare pas ces insertions dans `createTournament`. Cette extension nécessite ses propres tests d’intégration.
- ATLink conserve les sources et exporte un nouveau dossier ; le sérialiseur DBM reconstruit plusieurs fichiers du projet. La différence de sérialisation n’est pas un défaut de logique métier.

DBM ne doit pas être recopié sans adaptation FC27. Son créateur émet notamment `match_stagetype=GROUP`, tandis que les types observés dans nos fichiers FC27 utilisent LEAGUE avec une situation GROUP. De même, son modèle de coupe étudié est limité aux paliers allant jusqu’à seize équipes dans sa construction des tours. ATLink doit conserver les adaptations justifiées par les données natives et les documenter.

## Suite recommandée

1. Fixer un contrat de comportement : parité DBM pour les mécanismes communs et exceptions FC27 documentées.
2. Raccorder au modèle sportif les sources dynamiques, l’état initial et le cycle de fin de saison ; réutiliser les services bas niveau existants plutôt que multiplier les générateurs.
3. Aligner les tableaux à élimination, tout en conservant les qualifications personnalisées.
4. Ajouter les liens sportifs promotion/relégation/barrages et les paramètres de saison vérifiés.
5. Comparer des fichiers attendus après normalisation des IDs : championnat, coupe, aller-retour, groupes puis élimination, sources dynamiques, passage à la saison suivante.
6. Valider ensuite un projet exporté dans FC27. La validation interne seule ne suffit pas.

Le moteur de l’application n’a pas été modifié pendant cet audit. Les ajouts sont le contrôle reproductible `Checks/DbmCompetitionAudit.cs`, son point d’entrée, ce rapport et les résultats sous Checks/output.

## Sources

- [DBM — création, règles et classement initial](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/pages/compdata-editor/compdata-editor-page.component.ts#L172)
- [DBM — qualification automatique](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/services/compdata/advancement.service.ts#L19)
- [DBM — assistant, sources et fin de saison](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/pages/compdata-editor/create-tournament-wizard.component.ts#L1015)
- [DBM — dates](https://github.com/ViniMacacari/dbm-studio/blob/1828c34e9650875a5b6befdf493854f8e4fbcfe1/src/renderer/services/compdata/schedule-date.service.ts)
- [ATLink — compilateur sportif](../Core/FootballCompetition.cs)
- [ATLink — création DB/LOC](../Core/CompetitionProjectContext.cs)
- [ATLink — sources de participants](../Core/TeamSourceRules.cs)
