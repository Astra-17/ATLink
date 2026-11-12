# Données intégrées et installation

Les builds et publications incluent `Data/fifa_ng_db.db`, son metadata, les LOC anglais/français et leurs metadata, ainsi que la liste des sélections. Aucun Squad utilisateur n'est distribué. Les originaux de `files` servent uniquement de sources de packaging.

L'accueil présente Editing et Competition editing. Editing ouvre Base DB (données intégrées) ou Selected DB (sélection d'un Squad). Au premier lancement, l'assistant permet de choisir le jeu, la future langue commune et l'auteur des mods. La langue proposée est mémorisée mais ne change pas encore l'interface ni la DB : la mention « Fonctionnalité à venir » le précise. Toutes les langues découvertes dans les données intégrées sont proposées. Les fichiers ponctuels restent choisis à leur utilisation.

**System** conserve ces préférences dans `%LOCALAPPDATA%/ATLink/configuration.json` (schéma 2), avec le dernier jeu sélectionné. Les ressources sont automatiquement résolues depuis `Data` près de l'exécutable : images, enrichissements, bases, métadonnées, langues, Compdata FC27, cache FC27 et limites Live Editor. Aucun chemin ne se configure. Les anciens chemins personnalisés sont ignorés puis retirés du JSON au prochain enregistrement réussi ; le jeu, l'auteur et la langue DB actuelle sont préservés. Une erreur d'écriture conserve la configuration précédente ; un JSON invalide est conservé en sauvegarde lors d'une récupération validée.

Une ressource manquante demande de réparer ou mettre à jour ATLink et bloque uniquement la fonction concernée. Le cache FIFAMOD absent ne bloque pas les bases. Aucun Compdata FC26 n'est fourni actuellement : les fonctions de compétition FC26 le signalent sans utiliser les données d'un autre jeu.

L'installateur Inno Setup propose English/Français et écrit son choix initial dans `Data/default-language.txt`. Ce choix est proposé par l'assistant. Les préférences déjà enregistrées d'un utilisateur sont conservées lors d'une réinstallation.

Pour créer un installateur autonome (SDK .NET 10 et Inno Setup requis) :

```powershell
dotnet publish ATLink.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
ISCC.exe installer/ATLink.iss
```

Sortie : `artifacts/installer/ATLink-Setup.exe`. Pour ajouter une langue, ajouter sa DB et son XML dans les contenus Data du projet ; les paramètres découvrent les paires installées automatiquement. Ajouter aussi le choix dans l'installateur.
