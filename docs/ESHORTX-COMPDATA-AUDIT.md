# Analyse Eshortx / compdata-editor-fc26

Révision examinée : def3331ea2838cd2a11a7fb1935c0815f16293eb. Analyse des sources téléchargées, avec exécution isolée de updateSettingsReferences dans Node ; aucune exécution dans FC26/FC27. ATLink et son export ne sont pas modifiés.

## Nature du projet

Éditeur HTML/JavaScript de tables Compdata. La création dans compobj.js ajoute une compétition, une phase S1 FCE_League_Stage et un groupe G1. Ce chemin ne construit pas à lui seul un tournoi complet (équipes, qualifications, calendrier, règles et saison). Les fenêtres séparées éditent ces tables. output.js exporte dix fichiers Compdata et un JSON ; pas de création de DB/LOC ni de ressources de présentation dans ce chemin.

## Comparaison utile pour ATLink

- Insertion : createNewCompObj cherche une position dans la hiérarchie du parent, décale les objets et leurs parents et met à jour les références. ATLink ajoute la Super League 1926 en fin de fichier, avec parent UEFA 84. C’est une divergence réelle de disposition et de numérotation, à tester dans FC27 ; le dépôt ne prouve pas une obligation du moteur de jeu. Les niveaux spéciaux présents dans les données natives empêchent de supposer qu’un simple tri par niveau suffit.
- Références : updateAllReferences traite advancement, objectives, schedule, settings, standings, tasks, weather et initteams. Une renumérotation ATLink doit aussi préserver les namespaces : IDs d’objets, de ligues DB et d’équipes ne sont pas interchangeables.
- Compids : export reconstruit la liste à partir des objets level===3. Ce n’est pas nécessairement une règle universelle FC27 : les données natives ATLink comportent aussi des objets particuliers de niveau 6 dans compids. Ne pas remplacer aveuglément le sérialiseur actuel.
- Classements : updateStandingsData écrit les positions depuis zéro, comme ATLink. Les tâches sont exportées sur sept colonnes et les qualifications sur quatre ; pas d’incompatibilité de forme mise en évidence sur ces points.
- Type : settings conserve les valeurs de réglages éditées ; ce code n’impose pas le mapping League→LEAGUE / tout le reste→CUP du compilateur ATLink. Il ne justifie toutefois pas à lui seul quel comp_type choisir pour la Super League.
- Calendrier : manipulation des jours numériques et des tours. Pas de preuve apportée par cet éditeur pour l’origine calendaire FC27. Il ne faut pas en déduire une correction des dates de notre export.

## Défauts du dépôt de référence

Erreur reproduite dans updateSettingsReferences(10,3) :

Entrées : propriétaire 5 / schedule_forcecomp=20 ; propriétaire 20 / schedule_forcecomp=5.
Résultat réel : propriétaire 5 / cible 20 ; propriétaire 23 / cible 8.
Résultat attendu pour un décalage des objets >=10 : propriétaire 5 / cible 23 ; propriétaire 23 / cible 5.

La fonction conditionne le déplacement de la cible au déplacement du propriétaire, au lieu de tester la cible indépendamment. Copier cette logique corromprait des références.

Autre défaut visible dans createNewCompObj : une entrée objectives avec objective="idk" est ajoutée avant updateAllReferences. Son ID est donc décalé comme s’il s’agissait d’une ancienne entrée. Aucun contrat de jeu n’est fourni pour cette valeur. Ce n’est pas un comportement à porter dans ATLink.

La suppression splice les objets avant que removeCompObjReferences ne collecte les lignes à supprimer, ce qui peut sélectionner les objets suivants. Constat de lecture, non testé dans l’interface.

## Conclusion pour le crash

Ce dépôt fournit une piste d’insertion hiérarchique et une liste de références à contrôler. Il ne fournit ni preuve de jouabilité FC27, ni générateur complet validé, ni correctif certain du crash de carrière. La piste d’ordre des objets doit être isolée dans un export de test avec remappage contrôlé, sans reprendre ses algorithmes de mise à jour défectueux. Les autres suspects déjà relevés (type métier, intégration DB/ressources, retrait de créneaux amicaux) restent distincts.

Sources :
- https://github.com/Eshortx/compdata-editor-fc26/blob/def3331ea2838cd2a11a7fb1935c0815f16293eb/compobj.js
- https://github.com/Eshortx/compdata-editor-fc26/blob/def3331ea2838cd2a11a7fb1935c0815f16293eb/helper.js
- https://github.com/Eshortx/compdata-editor-fc26/blob/def3331ea2838cd2a11a7fb1935c0815f16293eb/output.js
- https://github.com/Eshortx/compdata-editor-fc26/blob/def3331ea2838cd2a11a7fb1935c0815f16293eb/standingswindow.js
