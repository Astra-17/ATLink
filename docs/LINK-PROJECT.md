# Projets ATLink .link

Dans le studio de compétitions, **Export project → Exporter un fichier .link**
enregistre le projet sans demander de mot de passe. **Open project → Ouvrir un
fichier .link** le rouvre directement. Les anciens fichiers protégés v1 ne sont
plus ouverts par l'interface. Les menus proposent aussi l'ouverture et l'export
en dossier Compdata. Le FIFAMOD reste l'export destiné au jeu.

## Version 2 automatique

| Champ | Taille |
| --- | --- |
| Signature ASCII `ATLK` | 4 octets |
| Version `0x02` | 1 octet |
| Sel aléatoire | 16 octets |
| Nonce aléatoire | 12 octets |
| Données chiffrées | Taille du fichier moins 49 octets |
| Tag AES-GCM | 16 octets |

La clé de 32 octets est SHA-256 des octets UTF-8 `ATLink.link.v2.automatic-key`
suivis des 16 octets du sel. Elle est calculable automatiquement : ce format
ne protège pas les données par un secret utilisateur. AES-256-GCM authentifie les
33 octets de l'en-tête comme données associées. Le tag est vérifié avant toute
décompression.

Le flux Deflate interne commence par un nombre de fichiers sur 32 bits. Chaque
fichier contient la longueur du chemin sur 32 bits, le chemin UTF-8 séparé par
des barres obliques, la taille du contenu sur 64 bits et les octets du fichier.
Les entiers sont non signés et écrits en petit-boutiste. La v2 accepte au plus
10 000 fichiers, des chemins de 4 096 octets, 512 Mio chiffrés et 2 Gio décompressés,
en comptant les champs du flux dans la taille décompressée.

`project.json` inclut le jeu et `LinkReference`. Son empreinte SHA-256 couvre
l'inventaire JSON compact trié ordinalement des chemins logiques et des
empreintes SHA-256 hexadécimales majuscules des fichiers requis. Cet inventaire
couvre les Compdata d'origine, la base principale et la langue principale
lorsqu'elles ne sont pas dans le projet, et les sources des traductions
secondaires à réappliquer. L'emplacement physique des références peut varier.

Le fichier chiffré est écrit et vidé sur disque à côté de la destination
avant son déplacement ou son remplacement. Le projet est marqué comme exporté
après cette opération. Pendant l'édition, ATLink utilise un dossier temporaire
pour les données extraites et le supprime à la fermeture normale du projet.
