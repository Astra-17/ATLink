# Travail sur les compétitions FC27

Avant de créer, modifier ou exporter une compétition FC27, lire
[le guide de création et de validation](docs/FC27-COMPETITION-GUIDE.md).
Il décrit la méthode utilisée pour les références SL/CL, les fichiers à synchroniser,
les réglages à préserver et les limites de ce qui a été confirmé en jeu.

Les anciennes notes d’audit décrivent parfois des hypothèses ensuite corrigées.
Pour le système personnalisé SL/CL, distinguer les exports historiques, la référence
actuelle de fichiers et la validation en carrière rapportée par l’utilisateur.
Ne jamais annoncer une validation en jeu à partir des seuls contrôles internes.

# Traductions obligatoires à chaque ajout d’interface

Pour toute création ou modification d’un texte visible dans l’application, lire
[le guide des langues de l’interface](docs/INTERFACE-LANGUAGES.md) et mettre à jour
les traductions dans la même intervention, sans attendre une demande distincte.
Cette règle s’applique aux écrans, boutons, menus, infobulles, dialogues,
messages de validation, erreurs, états de chargement et textes d’accessibilité.

- Réutiliser une clé existante lorsque le sens est identique. Sinon, ajouter une
  entrée dans `Core/Resources/Interface.translations.tsv` avec une traduction
  française et une traduction anglaise complètes. Ne pas laisser de traduction
  vide ou provisoire. Ne pas changer une clé source existante pour reformuler
  uniquement son texte affiché.
- Utiliser le service `UiText` en C# et les ressources `{loc:Text T…}` en XAML.
  Aucun nouveau texte d’interface ne doit être codé en dur. Utiliser les messages
  paramétrés et les pluriels plutôt que des fragments dépendant de la langue.
- Préserver les identifiants, codes, chemins, noms de propriétés, valeurs métier
  et textes saisis par l’utilisateur. Les traductions sont des libellés affichés,
  jamais des valeurs de contrôle de la logique métier.
- Après chaque ajout ou modification de traduction, exécuter automatiquement
  dans le cadre du travail `node tools/generate-interface-resources.cjs`, puis
  `node tools/check-interface-resources.cjs`. Inclure les catalogues JSON et la
  table de clés régénérés avec le changement ; ne pas les modifier à la main.
- Corriger les erreurs de l’audit avant de considérer le travail terminé.
  Vérifier l’affichage des éléments concernés dans les deux langues, notamment
  les débordements de texte. Si le mécanisme de traduction, les choix métier ou
  le changement de langue sont modifiés, exécuter aussi les contrôles
  `--interface-language` de `Checks` et de `Checks/UI`.

Un ajout d’interface n’est pas terminé tant que ses deux traductions et les
catalogues générés ne sont pas à jour.
