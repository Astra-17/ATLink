# Données intégrées et installation

Les builds et publications incluent `Data/fifa_ng_db.db`, son metadata, les LOC anglais/français et leurs metadata, ainsi que la liste des sélections. Aucun Squad utilisateur n'est distribué. Les originaux de `files` servent uniquement de sources de packaging.

L'accueil présente Editing et Competition editing. Editing ouvre Base DB (chargement intégré) ou Selected DB (sélection d'un Squad, metadata intégré). Le menu System choisit la langue de la DB, distincte de la langue de l'interface. Le choix est conservé par utilisateur dans `%LOCALAPPDATA%/ATLink/settings.json`. Un changement de LOC préserve les modifications de la DB principale ; les éditions du LOC précédent doivent être abandonnées explicitement.

Le lancement portable propose la langue au premier démarrage. L'installateur Inno Setup propose English/Français et écrit son choix initial dans `Data/default-language.txt`. Les préférences déjà enregistrées d'un utilisateur sont conservées lors d'une réinstallation.

Pour créer un installateur autonome (SDK .NET 10 et Inno Setup requis) :

```powershell
dotnet publish ATLink.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
ISCC.exe installer/ATLink.iss
```

Sortie : `artifacts/installer/ATLink-Setup.exe`. Pour ajouter une langue, ajouter sa DB et son XML dans les contenus Data du projet ; les paramètres découvrent les paires installées automatiquement. Ajouter aussi le choix dans l'installateur.
