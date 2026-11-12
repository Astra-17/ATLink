# Audit d’exécution — Super League FC27

Export examiné : files/export/SuperLeague-2026-2027. Symptôme confirmé par l’utilisateur : crash au lancement de carrière. Référence DBM : 1828c34e9650875a5b6befdf493854f8e4fbcfe1.

## Conclusion
Le générateur ATLink est une réimplémentation, pas un portage équivalent du créateur Compdata DBM. Les divergences ci-dessous sont vérifiées dans le code et/ou l’export. Aucune n’est à ce stade une cause de crash démontrée en jeu. Aucun fichier du mod ni code applicatif n’a été modifié par cet audit.

## Constats reproductibles

1. Type métier écrasé. FootballCompetitionCompiler.Create affecte LEAGUE si le format est League, CUP dans tous les autres cas. Le modèle sportif ne transporte donc pas le choix distinct INTERCUP. DBM applyRulesRequest écrit rules.competitionType. L’export européen (parent UEFA 84) contient 1926,comp_type,CUP ; la compétition européenne native C980 contient comp_type,INTERCUP. Cela démontre une perte de sémantique, sans prouver que toute compétition UEFA doit être INTERCUP. Le correctif doit dissocier format sportif et type métier, plutôt que remplacer CUP globalement.

2. Allocation reconstruite. L’export ajoute une préparation S1 (1949) et son groupe (1950) après les phases jouées S2–S6 (1927–1947). DBM construit la préparation avant les tours pour son modèle cup. Les 20 tâches FillWithTeam pointent sur 1950 ; 20 qualifications alimentent les deux groupes de dix. L’impact de l’ordre des objets sur le moteur FC27 reste inconnu.

3. Nouvelle identité et présentation. L’export crée la compétition DB 2 et asset_id=2. CompetitionProjectContext.Create copie les champs DB du modèle, les ballons et sponsors, puis ajoute les noms localisés. Il ne crée pas les ressources graphiques correspondantes et ne prouve pas l’inscription en carrière. Le créateur Compdata DBM étudié ne réalise pas ces insertions DB. Cette partie doit être validée séparément du portage Compdata.

4. Saison. Deux tâches UpdateTable enregistrent le vainqueur et le finaliste ; initteams reste identique à la source. ATLink propose ces tâches automatiquement selon un modèle FA Cup. DBM écrit les tâches demandées et le classement initial fourni séparément. L’absence de classement initial n’est pas en soi une preuve de défaut pour cette coupe à participants fixes.

5. Modification extérieure à la compétition. Quatre lignes du calendrier des amicaux masculins (objet 1902) ont été supprimées : jours 279/282/324/455, tours 3/4/6/8. La suppression du jour 282 retire la seule ligne native du tour 4. Cela élargit le périmètre du crash potentiel : ce mod ne fait pas qu’ajouter une compétition. Le besoin de continuité des tours n’a pas été démontré ; ne pas le présenter comme une erreur certaine.

## Comparaison des fichiers Compdata avec files/FC27/compdata

- advancement : 50 ajouts, aucun retrait.
- compids : 1 ajout, aucun retrait.
- compobj : 25 ajouts, aucun retrait.
- schedule : 13 ajouts, 4 retraits.
- settings : 64 ajouts, aucun retrait.
- standings : 70 ajouts, aucun retrait.
- tasks : 22 ajouts, aucun retrait.

Les autres fichiers texte à la racine comparés sont inchangés. Cette comparaison n’est pas un audit binaire de toutes les tables DB.

## Pistes écartées ou non démontrées

- Pas de calendrier SETUP : les 148 SETUP natifs examinés n’en ont pas non plus ; ne pas ajouter une date pour cette seule raison.
- Prolongation/tirs au but : présents par héritage de l’objet racine 0 ; leur absence locale ne signifie pas leur absence effective.
- Les qualifications et les tâches nominatives observées ne présentent pas à elles seules une référence manquante évidente.
- Les tests de parité ATLink réussis précédemment sont des tests internes ; ils n’exécutent pas DBM et FC27 sur les mêmes entrées.

## Validation suivante

Conserver le mod qui plante comme témoin. Tester séparément la restauration des quatre créneaux amicaux, puis une variante portant un type métier explicitement choisi et une variante réordonnant la préparation avant les phases jouées. Ne pas cumuler ces changements dans un correctif annoncé comme certain. Pour isoler la nouvelle identité, comparer ensuite ajout de compétition et remplacement d’une compétition existante dans des copies séparées. Chaque variante nécessite un lancement de nouvelle carrière avec exactement la même installation de référence ; ce contrôle n’est pas réalisable par la seule validation des fichiers.
