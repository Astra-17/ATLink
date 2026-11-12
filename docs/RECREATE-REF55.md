# Recréer REF-55-03 avec l’interface

Ce parcours utilise les commandes normales d’ATLink, sans Expert. Les identifiants
créés peuvent différer de ceux du mod historique. L’interface reste en anglais.

## Préparer le projet

Choisir **FC27**, ouvrir **Competition studio** depuis les Compdata natifs, avec la
DB complète et les langues **eng_us** et **fre_fr** du même jeu. Ne pas ouvrir
REF-55-03 comme point de départ si le but est de vérifier une création indépendante.

Les équipes et leur ordre initial sont documentés dans le fichier
`guided-entrants.csv` du projet de contrôle généré par le test de recréation.
Les clubs restent inscrits dans leur championnat domestique.

## European Super League

1. **Add competition → Groups + knockout**, région UEFA, nom **European Super League**.
2. Dans **English & French names**, renseigner si nécessaire les noms abrégés et français.
   Une case vide reprend le nom de compétition. Garder **Include reaching the group stage
   in club objectives** activé. Laisser **Regional cup registration** décoché pour la SL.
3. Construire deux groupes de dix, matchs simples, huit qualifiés par groupe, suivis
   des huitièmes, quarts, demi-finales et finale sur un match.
4. Dans **Teams**, ajouter les 20 clubs dans l’ordre de la table initiale : les dix
   premiers constituent A, les dix suivants B. **Matchups → Crossed seeds → Apply**
   produit le tableau A1–B8, A4–B5, A2–B7, A3–B6, B1–A8, B4–A5, B2–A7, B3–A6.
5. Dans **Schedule & venues → Choose matchday dates**, saisir les dates ci-dessous,
   toutes à 21:00. Pour reproduire les créneaux historiques, le minimum est zéro,
   sauf pour la finale où minimum et maximum valent un. La capacité maximale suit
   le nombre de rencontres de la journée.
6. Dans **Rules & rewards**, choisir 3/1/0, départages Points → Goal difference →
   Goals scored → Wins. Activer **Allow matches during men's international windows**,
   sans confondre ce réglage avec une garantie de disponibilité des joueurs.
7. Donner les primes : poules 100 000 000 ; huitièmes 10 000 000 ; quarts 20 000 000 ;
   demies 25 000 000 ; finale 45 000 000. Les nouvelles phases ne recopient plus les
   réductions de prime du modèle technique.

## European Community League

Elle peut être créée depuis **Teams → Linked competitions → Add linked competition**
ou ensuite comme compétition indépendante, puis reliée via le même onglet.

1. Nom **European Community League**, région UEFA, **Groups + knockout**.
   Activer **Regional cup registration** ; désactiver l’objectif d’accès aux poules
   pour reproduire les objectifs de cette référence.
2. Construire trois cartes : deux groupes de dix avec deux qualifiés par groupe ;
   **Semi-finals** à quatre équipes en aller-retour ; **Final** à deux équipes sur un match.
   Des cartes séparées permettent des règles indépendantes pour la finale.
3. Ajouter les 18 clubs directs, répartis neuf par groupe dans la table initiale.
   Configurer les qualifications liées avant de terminer : les deux places restantes
   sont réservées aux gagnants. Aucun club fictif supplémentaire n’est nécessaire.
4. **Matchups → Crossed seeds** : A1–B2 et B1–A2, puis vainqueurs des deux demies.
5. **Linked competitions → Higher division** : European Super League ; promotions
   **Winners of the semi-finals**. Les deux finalistes montent, quel que soit le résultat
   de la finale. Les derniers de chaque poule SL descendent ; les derniers de CL sortent.
6. **Calendar source** : European Super League, mêmes dates et heures des poules.
   Demies les 10 et 13 juin 2027, finale le 16 juin, à 21:00.
7. Dans les réglages de phase : prime de poules 25 000 000, demies 30 000 000,
   finale 45 000 000. **Set a draw date** : 10 août 2026 pour les poules,
   7 juin 2027 pour les demi-finales.
8. Points 3/1/0. Pour la phase de poules : Points → Goal difference → Goals scored →
   Away goals scored → Team rating. Importance générale 80. Aller-retour : score cumulé
   puis tirs au but, sans prolongation pour reproduire ce réglage de référence.
   Activer **Additional substitute** pour la finale seulement. Autoriser les dates
   pendant les fenêtres internationales comme pour SL.

Le récapitulatif des liens doit afficher : 18 maintenus SL + deux promus ;
16 maintenus CL + deux relégués SL = 18 directs, puis deux gagnants des qualifications.

## Community League Qualifiers

Dans **Teams → Linked competitions**, activer les qualifications pour la Community League.

1. Nom **Community League Qualifiers** ; destination European Community League ;
   autre compétition à exclure European Super League. La destination est elle-même exclue.
2. **Use European quotas** remplit les 21 sources nationales : 32 places dans chaque branche.
   Vérifier les 18 entrants directs sélectionnés dans **Direct entrants for the first edition**.
3. Les deux branches partagent les cinq dates du tableau. Première branche à 20:00,
   seconde à 21:00 ; les deux gagnants rejoignent les poules de la même édition.
4. Sous chaque date, **Rules & rewards** donne accès aux règles du tour et à sa prime.
   Garder zéro pour les quatre premiers tours ; sur **Playoffs**, choisir **Set prize amount**
   et saisir 25 000 000. Les restrictions d’association des anciens barrages ne sont pas générées.
5. **Preview / Review**, confirmer et **Apply** dans l’édition existante ; lors de la création
   liée, les choix restent dans le brouillon jusqu’à **Create competition** du parcours principal.

## Dates sportives

| Phase | Dates | Heure |
|---|---|---|
| Qualifs, cinq tours | 18, 21, 24, 27 et 29 juillet 2026 | A 20:00 ; B 21:00 |
| Poules SL et CL | 19/08, 03/10, 07/10, 15/11, 18/11/2026 ; 28/03, 31/03, 24/05, 02/06/2027 | 21:00 |
| SL huitièmes, quarts, demies, finale | 07, 10, 13, 16 juin 2027 | 21:00 |
| CL demies aller/retour, finale | 10, 13, 16 juin 2027 | 21:00 |

## Déplacer les internationaux

Dans le calendrier, choisir **International calendar**. La recherche filtre par nom,
type ou date. Les lignes indiquent les dates natives, les dates effectives liées et les
horaires/capacités conservés. Pour chacune des journées des amicaux masculins et des
qualifications européennes concernées, cocher **Move this matchday / merge its dates** :

| Journée native | Nouvelle date commune |
|---|---|
| 1 | 23 septembre 2026 |
| 2 | 25 septembre 2026 |
| 3 | 27 septembre 2026 |
| 4 | 29 septembre 2026 |
| 5 | 10 novembre 2026 |
| 6 | 12 novembre 2026 |
| 7 | 23 mars 2027 |
| 8 | 25 mars 2027 |

**Preview changes** affiche les journées et compétitions affectées, puis **Apply** accepte.
Les créneaux sont déplacés ensemble, pas supprimés ; ni les numéros de journée, ni les
heures, ni les capacités, ni les calendriers féminins ne changent.
Pendant une création, **Cancel** de la fenêtre ou du parcours ne modifie pas les sources.
Undo/Redo est disponible pour le brouillon international et, après application, dans le projet.

## Créer, exporter et contrôler

**Review** rassemble noms, primes, participants, liens et déplacements internationaux.
**Create competition** applique l’ensemble dans une transaction. Après création,
**Settings**, **Linked competitions**, **Translations** et **International calendar**
permettent de modifier les valeurs sans Expert.

Utiliser **Export project folder** pour le projet réouvrable et la DB séparée, puis
**Export FIFAMOD** avec le cache FC27 de FIFA Editor Tool v2.2.2. Le paquet contient
les fichiers modifiés par rapport au jeu natif, les LOC anglais/français et le profil
de limites ; il exclut la DB principale et n’ajoute pas de BOM aux Compdata.

## Ce que prouve la comparaison

Le contrôle `--ref55-recreation` construit les compétitions avec les ViewModels du
parcours normal, sans Expert ni copie de lignes natives depuis la référence. Il compare
tous les Compdata et vérifie relecture, noms, ressources du paquet et historique.

Le rapport `ref55-comparison.json` accompagne l’export de contrôle. Il explique :

- les identifiants et clés de noms différents ;
- les valeurs identiques par héritage plutôt que par surcharge locale ;
- les créneaux identiques décrits au niveau de la phase ou de chaque confrontation ;
- les marqueurs visuels de qualification/champion et les étiquettes de sortie CL déduites ;
- l’interclassement différent d’écritures dans des cellules saisonnières distinctes ;
- huit références `schedule_refcomp` de compétitions natives altérées par les anciennes
  générations du mod : la nouvelle création conserve leurs valeurs natives. Cette
  divergence est signalée, pas recopiée comme une nouvelle règle du système européen.

Les recrutements et opérations qui dépendent de l’ordre restent comparés dans cet ordre.
La validation dans FC27, notamment les convocations et le passage de saison, reste
une étape séparée. Les observations de primes ou restrictions retirées ne deviennent
pas des garanties universelles sur le comportement du moteur.
