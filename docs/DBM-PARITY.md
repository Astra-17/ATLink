# Référence métier DBM Studio

Révision auditée : 1828c34e9650875a5b6befdf493854f8e4fbcfe1 (branche main récupérée le 12 septembre 2026).
Source : https://github.com/ViniMacacari/dbm-studio
Auteur du projet de référence : Vinícius Macacari.
Le port C# conserve le design WPF ATLink et son adaptateur Squad FBCHUNKS.

## Comportements vérifiés et portés dans cette passe

| Fonction DBM | Source de référence | ATLink | Vérification |
| --- | --- | --- | --- |
| Transfert individuel : modification du seul teamid du lien sélectionné | src/renderer/services/transfer.service.ts, transferPlayer | Core/FootballCatalog.cs | Aucun autre champ/table modifié ; Base DB et Squad |
| Brouillon d'effectif ; ajout, retrait, réutilisation du lien existant | transfer.service.ts, addPlayerToTeamDraft / applyTeamPlayers | Core/TeamRosterDraft.cs ; Views/TeamWorkspace.xaml.cs | Pas de mutation avant Apply ; pas de doublon du lien club |
| Valeurs des nouveaux liens : maillot 99, position 0, forme 3, blessure/statistiques/cartons 0 | transfer.service.ts | Core/TeamRosterDraft.cs | Assertions sur données réelles |
| Suppression des références hors effectif dans le brouillon de formation | team-formation-editor.service.ts, syncSquadPlayers | Core/FormationEditor.cs, SyncSquad | Emplacements, capitaine et tireurs désaffectés ; emplacements -1 autorisés comme DBM |
| Écriture des quatre noms personnalisés et activation iscustomized/usercaneditname | player-editor.service.ts, applyDraft | Core/PlayerEditing.cs ; Views/EntityEditor.xaml.cs | Validation avant mutation ; flags et noms sauvegardés |
| Dates civiles birthdate / playerjointeamdate | fifa-date.ts ; player-editor.service.ts | Core/FifaDate.cs ; Views/EntityEditor.xaml.cs | Date bissextile et rejet de date impossible ; aller-retour fichier |

Tests : dotnet run --project Checks/ATLink.Checks.csproj -c Release -- --dbm .
Les tests utilisent des copies ; les fichiers source de files ne sont pas remplacés.
La réouverture par ATLink est validée, pas l'acceptation par FC26.

## Différences conservées explicitement

- Les identifiants nationaux fournis pour FC26 restent prioritaires. DBM utilise une heuristique sur clubworth/youthdevelopment/profitability/popularity/opponentweakthreshold.
- Lors d'un ajout à un club, ATLink réutilise le lien de club et protège le lien national. Le findPreferredPlayerLinkRow de DBM peut prendre le premier lien du joueur sans distinguer club/sélection.
- L'interface de transfert ATLink sélectionne un joueur et exige un seul lien club ; DBM sélectionne directement une ligne de lien.
- Le pipeline Transfermarkt ordonné fourni par l'utilisateur reste une extension.
- defaultteamdata est facultative dans le Squad ; elle est requise par le service de formation DBM.
- Le conteneur Squad et les dictionnaires de référence sont des adaptations ATLink.
- Les clés joueur restent protégées dans le formulaire ATLink.
- Les validations binaires sont faites avant écriture pour éviter des modifications partielles.

## Parité complète restant à établir

Cette passe ne constitue pas un portage exhaustif. Les fonctions ci-dessous existent partiellement dans ATLink mais n'ont pas encore toutes été comparées et alignées méthode par méthode :

- Création complète de joueurs, équipes et ligues, valeurs initiales, allocation d'identifiants et lignes liées.
- Éditeur d'équipe : brouillons des rivaux, pays, stades, kits et champs de localisation ; annulation intégrale de ces relations.
- Éditeur de ligue : brouillon d'équipes, positions, historique, groupes et génération des liens.
- Localisation générée des équipes/ligues et synchronisation des champs calculés.
- Toutes les règles des services Compdata : qualifications continentales, sources d'équipes, héritage, tâches, calendrier, météo, assistants et validations.
- Calcul des notes/potentiel, enrichissement de profils, détection de cheveux/barbe/teinte : comparaison complète avec les algorithmes DBM.
- Parité détaillée de l'import/export, du presse-papiers et des formats projet.

Ne pas présenter ATLink comme une conversion métier exhaustive tant que cette liste n'est pas close par des tests.
