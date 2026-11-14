# Langues de l’interface

Le bouton ⚙ de la barre de titre ouvre les paramètres depuis tous les écrans.
Le choix **Français / English** prend effet après enregistrement, sans redémarrage.
Il sélectionne aussi `fre_fr` / `eng_us` pour les noms du jeu actif (FC26 ou FC27).
Le choix est global et mémorisé dans `configuration.json`, champ `InterfaceLanguage`.

Une ancienne préférence `PreferredLanguage` française ou anglaise est reprise.
Sinon, l’application utilise le français sur un système français, l’anglais ailleurs.
Les autres langues LOC restent disponibles pour les traductions des compétitions.

## Conservation du travail

Une bascule ne recharge pas la DB principale et ne recrée pas les éditeurs.
Les modifications LOC de chaque langue restent distinctes pendant la session.
Les fichiers LOC doivent pouvoir être chargés avant l’enregistrement du choix ;
un échec laisse le choix et l’affichage précédents en place.

Le contexte des compétitions distingue le LOC affiché du LOC principal d’export.
La bascule ne change ni les règles, ni l’historique, ni les traductions du projet,
ni les langues sélectionnées pour l’export. Elle n’écrit pas les fichiers du jeu.

## Ressources et maintenance

`Core/Resources/Interface.translations.tsv` est la source des deux catalogues JSON
et de la table d’identité `Interface.keys.json`.
Chaque ligne contient une clé source stable, le français et, si nécessaire,
une traduction anglaise pour les anciennes clés françaises. Les clés JSON sont
calculées de façon déterministe ; ne pas modifier une clé source pour reformuler
une traduction.

Génération : `node tools/generate-interface-resources.cjs`.
Audit des clés, paramètres et références : `node tools/check-interface-resources.cjs`.

`UiText.Get`, `Format`, `Plural`, `Concat` et `Join` servent aux textes C#.
`UiText.Source` conserve l’identité des anciens choix qui utilisaient un libellé
comme valeur : les comparaisons métier restent indépendantes du texte affiché.
En XAML, utiliser `{loc:Text T…}` avec l’espace de noms `ATLink.Services`.
Les chaînes C# traduites portent leur identité de ressource ; seules ces chaînes
sont actualisées par la couche WPF. Les textes saisis par l’utilisateur ne sont
pas reconnus par leur contenu et ne sont pas traduits automatiquement.
Conserver les codes, clés, chemins, noms de propriétés et valeurs métier hors
des traductions. Les paramètres, nombres et dates conservent leurs conventions
de lecture/écriture ; les mois et jours affichés suivent la langue de l’interface.

## Contrôles

- `dotnet run --project Checks -- --interface-language .`
- `dotnet run --project Checks/UI -- --interface-language .`
- `dotnet run --project Checks -- --configuration .`
- `dotnet run --project Checks/UI -- --configuration .`

Les contrôles d’interface produisent des rendus dans
`Checks/output/interface-language`. Ils ne constituent pas un essai dans FC27.
