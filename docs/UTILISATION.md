# ATLink Studio

Application C# / WPF pour éditer les bases brutes EA FC avec leur metadata XML.

1. Lancer ATLink.exe (Windows, .NET Desktop Runtime 10 requis).
2. Ouvrir fifa_ng_db.db (metadata voisin détecté) ou un fichier Squad via Squad Editor.
3. Utiliser Table Editor ou Modules. Dans Players, l'éditeur propose Import profile / estimate ratings (club, ligue et trophées remplis depuis le profil). Dans Teams, ouvrir la formation. Stadiums est disponible.
4. Save crée une nouvelle DB et refuse d'écraser la source.

Compdata Editor ouvre un dossier contenant compobj.txt. New tournament crée une structure ; Tournament tools édite règles, sources, progression, météo et qualifications. Les assistants appliquent en mémoire, puis Save As écrit un nouveau dossier.

Pour les transferts : laisser FC26_NATIONAL_TEAM_IDS.csv à côté de la DB. Les transferts répétés sont traités dans l'ordre. L'accès direct à Transfermarkt peut être refusé par le site ; les imports locaux restent disponibles.

Les notes générées sont des estimations, pas les valeurs officielles FC26. Les sauvegardes et compétitions doivent encore être validées dans le jeu.

Les fichiers loc du jeu (`eng_us.db`, `fre_fr.db`, …) s'ouvrent via DB ou Full DB Editor ; ATLink déchiffre à la lecture et rechiffre à l'enregistrement (Save As, original intact). Localization CSV reste un import/export optionnel. Les fichiers Squad FBCHUNKS s'ouvrent dans Squad Editor. État détaillé : PORTAGE.md.

Pondérations fifarating sous licence MIT : fifarating-LICENSE.txt.
