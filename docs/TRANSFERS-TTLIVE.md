# Intégration TTLive dans ATLink — Transfers

## Parcours

Choisir parmi les 33 compétitions Transfermarkt, cliquer Verify, vérifier/modifier la liste, puis Apply. Les modifications restent dans la base ouverte, à enregistrer avec la sauvegarde habituelle ATLink. Aucun fichier Lua n’est produit.

Les mouvements répétés d’un joueur restent ordonnés. Une nouvelle vérification remplace les anciens mouvements importés, conserve les ajouts manuels, et attribue une séquence globale aux pages choisies. Un changement de sélection invalide l’import ; une réponse obsolète ou une page quittée ne remplit pas la liste.

Contrat et numéro sont facultatifs : vide conserve la valeur DB. Les noms enrichis viennent de files/players_enrich.json d’ATLink. Un ID absent de la base ouverte est signalé dans les unmatched ; il n’est jamais créé automatiquement.

## Logique métier

- Normalisation et ClubNameResolver portés depuis la version TTLive auditée.
- Noms Transfermarkt enrichis prioritaires ; fromClub sert uniquement à départager les homonymes.
- Pas de tri des équipes avant résolution, ni priorité homme/femme ajoutée par rapport à TTLive.
- Alias, core name, tokens, fallbacks parents, seuils et marges conservés.
- Source Transfermarkt native identique à TTLive : noms complets, ordre, exclusion des retours de prêt, erreurs HTTP.
- L’allowlist HTTPS du projet est conservée.
- Les noms localisés du JSON FC26 sont embarqués comme labels de référence par teamId, filtrés aux IDs de la base ouverte. Les noms affichés restent ceux d’ATLink.
- Les diagnostics de clubs conservent query, normalisation et cinq candidats.
- Équipes nationales protégées ; prévalidation du lot et protection contre les changements après prévisualisation.
- Un joueur déjà à destination est comptabilisé, sans bloquer les autres transferts.

## Annulation des prêts et contrats présignés

Autorisée explicitement par l’utilisateur : lors d’Apply, les relations playerloans et career_presignedcontract des joueurs qui changent effectivement de club sont annulées avant application du nouveau club. La prévisualisation ne supprime rien. Un joueur déjà à destination conserve ses relations si aucun autre mouvement du lot ne le déplace.

Le lot vérifie que les relations n’ont pas changé depuis la prévisualisation. En cas d’échec d’écriture, les relations et champs modifiés sont restaurés, y compris les modifications non sauvegardées présentes avant Apply. Les autres joueurs et les relations nationales ne sont pas touchés. La sauvegarde du document reste explicite.

ATLink édite une DB/Squad : il ne lance pas l’API carrière Live Editor et ne simule pas sa comptabilité de frais/salaires. Les valeurs contrat/numéro restent éditables dans la liste.

## Validation

- Parité exacte de 3 593 cas TTLive (Pays-Bas + cinq grands championnats) : playerId, teamId, méthodes et reason.
- Replay distinct sur la DB native et le fichier enrichi propres à ATLink.
- Application A → B → C, joueur déjà présent, conservation des champs facultatifs.
- Prévalidation atomique, refus des ID invalides, prévisualisation obsolète, protection nationale.
- Annulation des prêts/présignatures : joueurs non concernés préservés, cas déjà à destination, prévisualisation obsolète, restauration des lignes ajoutées/modifiées/inchangées en cas d’échec, sauvegarde/réouverture.
- Sauvegarde/réouverture d’une copie de DB validée.
- Contrôles existants ATLink passés, dont lecture/écriture DB et Squad.
- Contrôles WPF dédiés : 33 choix, deux mouvements du même joueur, sélection modifiée, réponse obsolète, Apply et rendu sans fenêtre visible.

Commandes depuis la racine ATLink :

    dotnet build ATLink.csproj
    dotnet run --project Checks/ATLink.Checks.csproj -- --transfers .
    dotnet run --project Checks/UI/ATLink.UIChecks.csproj -- --transfers .
    dotnet run --project Checks/ATLink.Checks.csproj -- .

Les fixtures TTLive sont conservées dans Checks/Fixtures et ne nécessitent pas le projet TTLive à l’exécution.
Les sauvegardes des fichiers de code initiaux sont dans artifacts/ttlive-integration/before (*.bak).

## Replay sur les données ATLink

| Ligue | Mouvements | Résolus | Clubs non trouvés |
|---|---:|---:|---:|
| netherlands | 541 | 287 | 38 |
| GB1 | 608 | 529 | 1 |
| ES1 | 532 | 374 | 7 |
| L1 | 591 | 483 | 14 |
| IT1 | 745 | 514 | 15 |
| FR1 | 576 | 421 | 10 |
