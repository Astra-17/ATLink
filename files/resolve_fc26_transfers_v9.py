#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
FC26 - Resolve Transfers V9

Joueurs
-------
- unique_exact_name          -> accepté
- unique_name_subset         -> accepté
- unique_first_last          -> accepté
- unique_fuzzy               -> accepté uniquement si score STRICTEMENT > 0.88
- ancien club utilisé uniquement pour départager plusieurs identités plausibles

Equipes
-------
- nettoyage accents / espaces invisibles
- "FC Liverpool" peut correspondre à "Liverpool"
- alias Transfermarkt -> nom FC26 pour clubs non licenciés
- "Sans club" -> Agents libres, teamid 131368
- gestion ambiguïtés équipes masculines/féminines selon les règles observées

Sorties
-------
- transfers_resolved.csv
- transfers_unresolved.csv
"""

import csv
import re
import sys
import unicodedata
from difflib import SequenceMatcher
from pathlib import Path

DEFAULT_TRANSFERS = "transfers.csv"
DEFAULT_PLAYERS = "FC26_PLAYER_IDS.csv"
DEFAULT_TEAMS = "FC26_TEAM_IDS.csv"
DEFAULT_RESOLVED = "transfers_resolved.csv"
DEFAULT_UNRESOLVED = "transfers_unresolved.csv"

FREE_AGENT_TEAM_ID = "131368"
FUZZY_THRESHOLD = 0.88

INVISIBLE_CHARS = {
    "\u00a0": " ",
    "\u2007": " ",
    "\u202f": " ",
    "\u200b": "",
    "\u200c": "",
    "\u200d": "",
    "\ufeff": "",
}


def clean(value):
    if value is None:
        return ""
    value = str(value)
    for src, dst in INVISIBLE_CHARS.items():
        value = value.replace(src, dst)
    value = unicodedata.normalize("NFC", value)
    return re.sub(r"\s+", " ", value).strip()


def normalize(value):
    value = clean(value).lower()
    value = unicodedata.normalize("NFKD", value)
    value = "".join(c for c in value if not unicodedata.combining(c))
    value = value.replace("’", "'")
    value = value.replace("&", " and ")
    value = value.replace("-", " ")
    value = value.replace(".", " ")
    value = value.replace("'", " ")
    value = re.sub(r"[^a-z0-9 ]+", " ", value)
    return re.sub(r"\s+", " ", value).strip()


def tokens(value):
    return [x for x in normalize(value).split() if x]


# Termes juridiques/de club qui peuvent changer de position ou être absents
# entre Transfermarkt et la DB FC26.
TEAM_NOISE_TOKENS = {
    "fc", "afc", "cf", "ac", "sc", "ssc", "ss", "bc",
    "club", "football", "futbol", "fussball"
}


def team_key(value):
    """
    Clé secondaire pour :
      FC Liverpool -> Liverpool
      Liverpool FC -> Liverpool
      AFC Bournemouth -> Bournemouth

    On ne l'utilise qu'après l'échec du nom exact / alias.
    """
    ts = [t for t in tokens(value) if t not in TEAM_NOISE_TOKENS]
    return " ".join(ts)


# Alias explicitement utiles pour Transfermarkt -> FC26.
# Clés ET valeurs passent ensuite par normalize().
TEAM_ALIASES_RAW = {
    # --------------------------------------------------------------
    # SPECIAL
    # --------------------------------------------------------------
    "Sans club": ["__FREE_AGENT__"],
    "Sans équipe": ["__FREE_AGENT__"],
    "Libre": ["__FREE_AGENT__"],
    "Agents libres": ["__FREE_AGENT__"],
    "Agent libre": ["__FREE_AGENT__"],
    "Free Agent": ["__FREE_AGENT__"],
    "Free Agents": ["__FREE_AGENT__"],

    # --------------------------------------------------------------
    # VARIANTES TRANSFERMARKT -> NOMS FC26 / NOMS RESTAURES PAR MOD
    #
    # Chaque alias peut avoir plusieurs noms cibles.
    # Le resolver teste les cibles dans l'ordre et prend la première
    # qui existe réellement dans FC26_TEAM_IDS.csv.
    # --------------------------------------------------------------

    "Juventus Turin": ["Juventus"],

    "Espanyol Barcelona": ["RCD Espanyol de Barcelona"],

    "Palerme FC": ["Palermo"],

    "Union Saint-Gilloise": ["Royale Union Saint-Gilloise"],

    "Jagiellonia Bialystok": ["Jagiellonia Białystok"],

    "Le Havre AC": ["Havre AC"],

    "Samsunspor": ["Samsunspor A.Ş"],

    "UD Levante": ["Levante UD"],

    "Séville FC": ["Sevilla FC"],
    "Seville FC": ["Sevilla FC"],

    "US Sassuolo": ["Sassuolo"],

    "AC Sparta Prague": ["AC Sparta Praha"],

    "Olympiakós Le Pirée": ["Olympiacos FC"],
    "Olympiakos Le Piree": ["Olympiacos FC"],

    "Fenerbahce": ["Fenerbahçe SK"],
    "Fenerbahçe": ["Fenerbahçe SK"],

    "Genoa CFC": ["Genoa"],

    "Deportivo La Corogne": ["RC Deportivo"],
    "Deportivo La Coruña": ["RC Deportivo"],

    "Eintracht Francfort": ["Eintracht Frankfurt"],

    "Racing Santander": ["Real Racing Club"],

    "Ajax Amsterdam": ["Ajax"],

    "Cercle Bruges": ["Cercle Brugge KSV"],

    "Go Ahead Eagles Deventer": ["Go Ahead Eagles"],

    "Rodez AF": ["Rodez Aveyron Football"],

    "US Lecce": ["Lecce"],

    "EA Guingamp": ["En Avant Guingamp"],

    "Vitória Guimarães SC": ["Vitória SC"],
    "Vitoria Guimaraes SC": ["Vitória SC"],

    "GD Estoril Praia": ["Estoril Praia"],

    "SV Werder Brême": ["SV Werder Bremen"],
    "SV Werder Breme": ["SV Werder Bremen"],

    "Red Bull Salzbourg": ["FC Red Bull Salzburg"],

    "Feyenoord Rotterdam": ["Feyenoord"],

    "FC Saint-Gall 1879": ["FC St. Gallen 1879"],

    "PAOK Thessaloniki": ["PAOK FC"],

    "UC Sampdoria": ["Sampdoria"],

    "Mantova 1911": ["Mantova"],

    "Cagliari Calcio": ["Cagliari"],

    "Genclerbirligi Ankara": ["Gençlerbirliği SK"],
    "Gençlerbirliği Ankara": ["Gençlerbirliği SK"],

    "KAA La Gantoise": ["KAA Gent"],

    "ACSC FC Arges": ["FC Argeş"],
    "ACSC FC Argeș": ["FC Argeş"],

    # --------------------------------------------------------------
    # CLUBS ITALIENS NON LICENCIES EN FC26 VANILLA
    #
    # On teste d'abord le nom générique FC26, puis le vrai nom restauré
    # par un éventuel mod de licences.
    # --------------------------------------------------------------

    "AC Milan": ["Milano FC", "AC Milan", "Milan"],
    "Milan AC": ["Milano FC", "AC Milan", "Milan"],
    "Milan": ["Milano FC", "AC Milan", "Milan"],

    "Inter Milan": ["Lombardia FC", "Inter Milan", "Inter", "Internazionale"],
    "Inter Milano": ["Lombardia FC", "Inter Milan", "Inter", "Internazionale"],
    "Internazionale": ["Lombardia FC", "Inter Milan", "Inter", "Internazionale"],
    "Internazionale Milano": ["Lombardia FC", "Inter Milan", "Inter", "Internazionale"],
    "Inter": ["Lombardia FC", "Inter Milan", "Inter", "Internazionale"],

    "Lazio Rome": ["Latium", "Lazio", "Lazio Roma", "SS Lazio"],
    "Lazio Roma": ["Latium", "Lazio", "Lazio Roma", "SS Lazio"],
    "SS Lazio": ["Latium", "Lazio", "Lazio Roma", "SS Lazio"],
    "Lazio": ["Latium", "Lazio", "Lazio Roma", "SS Lazio"],

    "Atalanta Bergame": ["Bergamo Calcio", "Atalanta", "Atalanta BC"],
    "Atalanta Bergamo": ["Bergamo Calcio", "Atalanta", "Atalanta BC"],
    "Atalanta BC": ["Bergamo Calcio", "Atalanta", "Atalanta BC"],
    "Atalanta": ["Bergamo Calcio", "Atalanta", "Atalanta BC"],

    # Napoli est licencié mais on accepte plusieurs variantes.
    "Naple": ["SSC Napoli", "Napoli"],
    "Naples": ["SSC Napoli", "Napoli"],
    "Napoli": ["SSC Napoli", "Napoli"],
    "SSC Napoli": ["SSC Napoli", "Napoli"],
    "Napoli SSC": ["SSC Napoli", "Napoli"],
}

TEAM_ALIASES = {
    normalize(k): v for k, v in TEAM_ALIASES_RAW.items()
}



def read_csv_auto(path):
    path = Path(path)
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        sample = f.read(4096)
        f.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=";,")
            delimiter = dialect.delimiter
        except csv.Error:
            delimiter = ";"
        return list(csv.DictReader(f, delimiter=delimiter))


def get_first(row, *keys):
    for key in keys:
        if key in row and clean(row[key]) != "":
            return clean(row[key])
    return ""


# ----------------------------------------------------------------------
# PLAYERS
# ----------------------------------------------------------------------

def build_players(rows):
    players = []

    for row in rows:
        playerid = get_first(row, "playerid", "PlayerID", "player_id")
        playername = get_first(row, "playername", "player_name", "name")
        teamid = get_first(row, "current_teamid", "teamid", "current_team_id")
        teamname = get_first(row, "current_teamname", "teamname", "current_team_name")

        if not playerid or not playername:
            continue

        players.append({
            "playerid": playerid,
            "playername": playername,
            "teamid": teamid,
            "teamname": teamname,
            "player_norm": normalize(playername),
            "team_norm": normalize(teamname),
            "player_tokens": tokens(playername),
        })

    return players


def sequence_similarity(a, b):
    return SequenceMatcher(None, normalize(a), normalize(b)).ratio()


def choose_by_old_club(candidates, old_club):
    """
    L'ancien club n'est qu'un DEPARTAGEUR.
    Jamais une condition obligatoire pour identifier un joueur unique.
    """
    target_norm = normalize(old_club)
    target_key = team_key(old_club)

    exact = [p for p in candidates if p["team_norm"] == target_norm]
    if len(exact) == 1:
        return exact[0]

    key_matches = [
        p for p in candidates
        if target_key and team_key(p["teamname"]) == target_key
    ]
    if len(key_matches) == 1:
        return key_matches[0]

    return None


def resolve_player(player_name, old_club, players):
    target_name = normalize(player_name)
    target_tokens = tokens(player_name)

    # 1) Exact global
    exact = [p for p in players if p["player_norm"] == target_name]

    if len(exact) == 1:
        return exact[0], "unique_exact_name"

    if len(exact) > 1:
        by_club = choose_by_old_club(exact, old_club)
        if by_club:
            return by_club, "ambiguous_exact_name_resolved_by_old_club"
        return None, "ambiguous_exact_name"

    # 2) Nom TM inclus dans nom FC26
    subset_matches = []

    if len(target_tokens) >= 2:
        target_set = set(target_tokens)

        subset_matches = [
            p for p in players
            if target_set.issubset(set(p["player_tokens"]))
        ]

    if len(subset_matches) == 1:
        return subset_matches[0], "unique_name_subset"

    if len(subset_matches) > 1:
        by_club = choose_by_old_club(subset_matches, old_club)
        if by_club:
            return by_club, "ambiguous_name_subset_resolved_by_old_club"

        fl = [
            p for p in subset_matches
            if len(p["player_tokens"]) >= 2
            and p["player_tokens"][0] == target_tokens[0]
            and p["player_tokens"][-1] == target_tokens[-1]
        ]

        if len(fl) == 1:
            return fl[0], "subset_unique_first_last"

    # 3) Premier + dernier
    if len(target_tokens) >= 2:
        first_last = [
            p for p in players
            if len(p["player_tokens"]) >= 2
            and p["player_tokens"][0] == target_tokens[0]
            and p["player_tokens"][-1] == target_tokens[-1]
        ]

        if len(first_last) == 1:
            return first_last[0], "unique_first_last"

        if len(first_last) > 1:
            by_club = choose_by_old_club(first_last, old_club)
            if by_club:
                return by_club, "ambiguous_first_last_resolved_by_old_club"

    # 4) Fuzzy : STRICTEMENT > 0.88
    scored = [
        (sequence_similarity(player_name, p["playername"]), p)
        for p in players
    ]
    scored.sort(key=lambda x: x[0], reverse=True)

    fuzzy_candidates = [
        (score, p) for score, p in scored
        if score > FUZZY_THRESHOLD
    ]

    if len(fuzzy_candidates) == 1:
        score, player = fuzzy_candidates[0]
        return player, f"unique_fuzzy_{score:.3f}"

    if len(fuzzy_candidates) > 1:
        plausible_players = [p for _, p in fuzzy_candidates]
        by_club = choose_by_old_club(plausible_players, old_club)

        if by_club:
            score = next(
                score for score, p in fuzzy_candidates
                if p["playerid"] == by_club["playerid"]
            )
            return by_club, f"ambiguous_fuzzy_resolved_by_old_club_{score:.3f}"

    return None, "player_not_found_or_ambiguous"


# ----------------------------------------------------------------------
# TEAMS
# ----------------------------------------------------------------------

def build_team_indexes(rows):
    """
    Conserve l'ordre original du CSV, important pour le cas homme/femme
    où les deux teamid ont 6 chiffres.
    """
    by_name = {}
    by_key = {}
    by_id = {}

    for row in rows:
        teamid = get_first(row, "teamid", "TeamID", "team_id")
        teamname = get_first(row, "teamname", "team_name", "name")

        if not teamid or not teamname:
            continue

        item = {
            "teamid": clean(teamid),
            "teamname": clean(teamname),
            "team_norm": normalize(teamname),
            "team_key": team_key(teamname),
        }

        # Premier enregistrement d'un ID conservé.
        if item["teamid"] in by_id:
            continue

        by_id[item["teamid"]] = item
        by_name.setdefault(item["team_norm"], []).append(item)

        if item["team_key"]:
            by_key.setdefault(item["team_key"], []).append(item)

    return {
        "by_name": by_name,
        "by_key": by_key,
        "by_id": by_id,
    }


def unique_by_teamid(matches):
    out = []
    seen = set()

    for m in matches:
        if m["teamid"] not in seen:
            seen.add(m["teamid"])
            out.append(m)

    return out


def format_team_candidates(matches):
    return " | ".join(
        f'{m["teamid"]}:{m["teamname"]}'
        for m in matches
    )


def parent_team_name(value):
    """
    Si Transfermarkt pointe vers une équipe réserve / jeunes non présente
    dans FC26, on peut retomber sur l'équipe première UNIQUEMENT si cette
    équipe première existe sans ambiguïté dans la DB.

    Exemples :
      Newcastle United U21 -> Newcastle United
      SL Benfica B         -> SL Benfica
    """
    s = clean(value)

    patterns = [
        r"\s+U21$",
        r"\s+U23$",
        r"\s+U19$",
        r"\s+U18$",
        r"\s+B$",
        r"\s+II$",
        r"\s+2$",
    ]

    for pattern in patterns:
        candidate = re.sub(pattern, "", s, flags=re.IGNORECASE).strip()
        if candidate != s:
            return candidate

    return ""


def choose_male_team(matches):
    """
    Règles observées par l'utilisateur :
    - si un seul candidat n'a pas 6 chiffres -> masculin
    - si tous les candidats ont 6 chiffres -> le masculin est le premier
      dans l'ordre de la DB/export
    """
    matches = unique_by_teamid(matches)

    if len(matches) == 1:
        return matches[0], "unique_team_candidate"

    non_six_digit = [
        m for m in matches
        if not (m["teamid"].isdigit() and len(m["teamid"]) == 6)
    ]

    if len(non_six_digit) == 1:
        return non_six_digit[0], "male_team_preferred_non_6_digit"

    if len(non_six_digit) == 0 and len(matches) >= 2:
        return matches[0], "male_team_preferred_first_db_occurrence"

    return None, "ambiguous_team_name"


def resolve_team(team_name, indexes):
    source_norm = normalize(team_name)

    # ----------------------------------------------------------
    # 0) Alias spécial : Sans club -> Agents libres
    # ----------------------------------------------------------
    aliases = TEAM_ALIASES.get(source_norm)

    if aliases and "__FREE_AGENT__" in aliases:
        by_id = indexes["by_id"]

        if FREE_AGENT_TEAM_ID in by_id:
            item = by_id[FREE_AGENT_TEAM_ID]
        else:
            item = {
                "teamid": FREE_AGENT_TEAM_ID,
                "teamname": "Agents libres",
                "team_norm": normalize("Agents libres"),
                "team_key": team_key("Agents libres"),
            }

        return item, "free_agent_alias", ""

    # ----------------------------------------------------------
    # 1) Alias de licence / nom connu
    #
    # Un alias peut avoir plusieurs cibles :
    # ex. AC Milan -> Milano FC -> AC Milan -> Milan.
    #
    # Ceci permet de fonctionner avec FC26 vanilla ET avec un mod qui
    # restaure les vrais noms de clubs.
    # ----------------------------------------------------------
    if aliases:
        for alias_target in aliases:
            alias_norm = normalize(alias_target)
            matches = indexes["by_name"].get(alias_norm, [])

            if matches:
                team, reason = choose_male_team(matches)

                if team:
                    return team, "forced_alias_" + reason, ""

                return None, reason, format_team_candidates(matches)

            # Deuxième chance avec la clé sans FC/AC/etc.
            alias_key = team_key(alias_target)

            if alias_key:
                matches = indexes["by_key"].get(alias_key, [])

                if matches:
                    team, reason = choose_male_team(matches)

                    if team:
                        return team, "forced_alias_key_" + reason, ""

                    return None, reason, format_team_candidates(matches)

    # ----------------------------------------------------------
    # 2) Nom Transfermarkt direct
    # ----------------------------------------------------------
    lookup_name = team_name
    lookup_norm = normalize(lookup_name)

    matches = indexes["by_name"].get(lookup_norm, [])

    if matches:
        team, reason = choose_male_team(matches)

        if team:
            return team, reason, ""

        return None, reason, format_team_candidates(matches)

    # ----------------------------------------------------------
    # 3) Clé sans FC/AFC/AC/etc.
    # Exemple : FC Liverpool -> Liverpool
    # ----------------------------------------------------------
    key = team_key(lookup_name)

    if key:
        matches = indexes["by_key"].get(key, [])

        if matches:
            team, reason = choose_male_team(matches)

            if team:
                return team, "club_designator_normalized_" + reason, ""

            return None, reason, format_team_candidates(matches)

    # ----------------------------------------------------------
    # 4) Equipes réserves / U21 / B
    #
    # Si l'équipe exacte n'existe pas dans le jeu mais que son équipe
    # première existe de façon non ambiguë, on utilise l'équipe première.
    # ----------------------------------------------------------
    parent = parent_team_name(team_name)

    if parent:
        parent_norm = normalize(parent)
        parent_matches = indexes["by_name"].get(parent_norm, [])

        if not parent_matches:
            parent_key = team_key(parent)
            if parent_key:
                parent_matches = indexes["by_key"].get(parent_key, [])

        if parent_matches:
            team, reason = choose_male_team(parent_matches)

            if team:
                return team, "reserve_or_youth_parent_team_" + reason, ""

    # ----------------------------------------------------------
    # 5) "Fin de carrière" n'est pas une équipe.
    # On le garde volontairement non résolu pour éviter d'envoyer le
    # joueur dans un club arbitraire.
    # ----------------------------------------------------------
    if source_norm in {
        normalize("Fin de carrière"),
        normalize("Retraite"),
        normalize("Retired"),
    }:
        return None, "retirement_not_team", ""

    # ----------------------------------------------------------
    # 6) Aucun fuzzy d'équipe automatique.
    # Les noms restants sont réellement absents ou nécessitent une
    # correspondance métier explicite.
    # ----------------------------------------------------------
    return None, "team_not_found", ""


# ----------------------------------------------------------------------
# MAIN
# ----------------------------------------------------------------------

def main():
    transfers_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(DEFAULT_TRANSFERS)
    players_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(DEFAULT_PLAYERS)
    teams_path = Path(sys.argv[3]) if len(sys.argv) > 3 else Path(DEFAULT_TEAMS)

    for p in (transfers_path, players_path, teams_path):
        if not p.exists():
            print(f"ERREUR : fichier introuvable : {p}")
            raise SystemExit(1)

    transfers = read_csv_auto(transfers_path)
    players = build_players(read_csv_auto(players_path))
    team_indexes = build_team_indexes(read_csv_auto(teams_path))

    resolved = []
    unresolved = []

    for row in transfers:
        sequence = get_first(row, "sequence")
        player_name = get_first(row, "player_name", "playername")
        old_club = get_first(row, "old_club")
        new_club = get_first(row, "new_club")
        movement = get_first(row, "movement")

        player, player_reason = resolve_player(
            player_name,
            old_club,
            players
        )

        if not player:
            unresolved.append({
                "sequence": sequence,
                "player_name": player_name,
                "old_club": old_club,
                "new_club": new_club,
                "movement": movement,
                "reason": player_reason,
                "team_candidates": "",
            })
            continue

        new_team, team_reason, candidates = resolve_team(
            new_club,
            team_indexes
        )

        if not new_team:
            unresolved.append({
                "sequence": sequence,
                "player_name": player_name,
                "old_club": old_club,
                "new_club": new_club,
                "movement": movement,
                "reason": team_reason,
                "team_candidates": candidates,
            })
            continue

        resolved.append({
            "sequence": sequence,
            "playerid": player["playerid"],
            "player_name": player_name,
            "fc26_player_name": player["playername"],
            "current_teamid_at_export": player["teamid"],
            "current_club_at_export": player["teamname"],
            "new_teamid": new_team["teamid"],
            "old_club_transfermarkt": old_club,
            "new_club": new_club,
            "fc26_new_club": new_team["teamname"],
            "player_match": player_reason,
            "team_match": team_reason,
        })

    def seq_key(r):
        try:
            return int(r["sequence"])
        except Exception:
            return 999999999

    resolved.sort(key=seq_key)
    unresolved.sort(key=seq_key)

    with open(DEFAULT_RESOLVED, "w", newline="", encoding="utf-8-sig") as f:
        fields = [
            "sequence",
            "playerid",
            "player_name",
            "fc26_player_name",
            "current_teamid_at_export",
            "current_club_at_export",
            "new_teamid",
            "old_club_transfermarkt",
            "new_club",
            "fc26_new_club",
            "player_match",
            "team_match",
        ]
        w = csv.DictWriter(f, fieldnames=fields, delimiter=";")
        w.writeheader()
        w.writerows(resolved)

    with open(DEFAULT_UNRESOLVED, "w", newline="", encoding="utf-8-sig") as f:
        fields = [
            "sequence",
            "player_name",
            "old_club",
            "new_club",
            "movement",
            "reason",
            "team_candidates",
        ]
        w = csv.DictWriter(f, fieldnames=fields, delimiter=";")
        w.writeheader()
        w.writerows(unresolved)

    print()
    print("==========================================")
    print("FC26 TRANSFER RESOLVER V9")
    print("==========================================")
    print(f"Transferts total : {len(transfers)}")
    print(f"Résolus          : {len(resolved)}")
    print(f"Non résolus      : {len(unresolved)}")
    print(f"Fuzzy auto       : score > {FUZZY_THRESHOLD:.2f}")
    print()

    adapted_players = [
        r for r in resolved
        if r["player_match"] != "unique_exact_name"
    ]
    if adapted_players:
        print("MATCHES JOUEURS ADAPTES :")
        for r in adapted_players[:50]:
            print(
                f"[{r['player_match']}] "
                f"{r['player_name']} -> {r['fc26_player_name']}"
            )
        print()

    adapted_teams = [
        r for r in resolved
        if r["team_match"] not in ("unique_team_candidate", "exact_team_name")
    ]
    if adapted_teams:
        print("MATCHES EQUIPES ADAPTES :")
        for r in adapted_teams[:50]:
            print(
                f"[{r['team_match']}] "
                f"{r['new_club']} -> {r['fc26_new_club']} "
                f"(ID {r['new_teamid']})"
            )
        print()

    if unresolved:
        print("NON RESOLUS :")
        for r in unresolved[:50]:
            extra = (
                " | candidats: " + r["team_candidates"]
                if r["team_candidates"]
                else ""
            )
            print(
                f"[{r['reason']}] "
                f"{r['player_name']} | "
                f"{r['old_club']} -> {r['new_club']}"
                f"{extra}"
            )

    print()
    print(f"Créé : {DEFAULT_RESOLVED}")
    print(f"Créé : {DEFAULT_UNRESOLVED}")


if __name__ == "__main__":
    main()
