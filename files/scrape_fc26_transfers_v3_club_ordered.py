#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Transfermarkt -> CSV transferts FC26

Compatible avec une page de championnat du type :
https://www.transfermarkt.fr/ligue-1/transfers/wettbewerb/FR1

ORDRE GARANTI :
Pour chaque club :
    1. Arrivées
    2. Départs
Puis club suivant.

CSV :
sequence;club_sequence;phase;page_club;player_name;...
"""

import csv
import re
import sys
import unicodedata

import requests
from bs4 import BeautifulSoup


HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/152.0.0.0 Safari/537.36"
    ),
    "Accept-Language": "fr-FR,fr;q=0.9,en;q=0.8",
}


def clean(value):
    return re.sub(r"\s+", " ", value or "").strip()


def collapse_duplicate_text(value):
    """
    Corrige par ex. 'Sans clubSans club' -> 'Sans club'.
    """
    value = clean(value)
    if not value:
        return value

    n = len(value)
    if n % 2 == 0:
        half = n // 2
        if value[:half].strip().lower() == value[half:].strip().lower():
            return value[:half].strip()

    return value


def norm(value):
    value = clean(value).lower()
    value = unicodedata.normalize("NFKD", value)
    value = "".join(c for c in value if not unicodedata.combining(c))
    value = re.sub(r"[^a-z0-9 ]+", " ", value)
    return re.sub(r"\s+", " ", value).strip()


def headers_of(table):
    return [
        clean(th.get_text(" ", strip=True)).lower()
        for th in table.select("thead th")
    ]


def index_of(headers, *needles):
    for i, header in enumerate(headers):
        if any(needle in header for needle in needles):
            return i
    return None


def detect_movement(table):
    headers = headers_of(table)
    joined = " | ".join(headers)

    if "venant de" in joined:
        return "arrival"

    if "allant à" in joined or "allant a" in joined:
        return "departure"

    return None


def extract_player_name(row):
    # Le lien profil joueur est le signal le plus fiable.
    for a in row.select('a[href*="/profil/spieler/"]'):
        text = clean(a.get_text(" ", strip=True))
        if text:
            return text

        title = clean(a.get("title", ""))
        if title:
            return title

    cells = row.find_all("td", recursive=False)
    if cells:
        return clean(cells[0].get_text(" ", strip=True))

    return ""


def extract_team_from_cell(cell):
    if cell is None:
        return ""

    # 1. Lien explicite vers une équipe.
    for a in cell.select(
        'a[href*="/startseite/verein/"], '
        'a[href*="/verein/"]'
    ):
        title = clean(a.get("title", ""))
        if title:
            return collapse_duplicate_text(title)

        text = clean(a.get_text(" ", strip=True))
        if text:
            return collapse_duplicate_text(text)

    # 2. Logo / image : souvent le nom du club est dans alt/title.
    for img in cell.select("img"):
        title = clean(img.get("title", ""))
        alt = clean(img.get("alt", ""))

        if title:
            return collapse_duplicate_text(title)
        if alt:
            return collapse_duplicate_text(alt)

    # 3. Cas "Sans club", "Fin de carrière", etc.
    return collapse_duplicate_text(cell.get_text(" ", strip=True))


def valid_club_heading(text):
    text = clean(text)
    lower = text.lower()

    if not text:
        return False

    forbidden = (
        "transferts 26/27",
        "transferts",
        "arrivées",
        "arrivees",
        "départs",
        "departs",
        "arrivals",
        "departures",
    )

    if lower in forbidden:
        return False

    if lower.startswith("transferts "):
        return False

    return True


def extract_club_from_box(box):
    """
    Sur la page championnat Transfermarkt, chaque équipe est normalement
    contenue dans son propre bloc (.box).

    On privilégie le titre/lien du header du bloc.
    """
    if box is None:
        return ""

    # Header principal du bloc
    headings = box.select(
        ":scope > h2.content-box-headline, "
        ":scope > h2, "
        ":scope > div > h2.content-box-headline"
    )

    for h in headings:
        # Un lien d'équipe dans le heading est idéal.
        for a in h.select("a"):
            title = clean(a.get("title", ""))
            text = clean(a.get_text(" ", strip=True))

            for candidate in (title, text):
                if valid_club_heading(candidate):
                    return candidate

        text = clean(h.get_text(" ", strip=True))
        if valid_club_heading(text):
            return text

    # Fallback : chercher le premier lien équipe dans le début du bloc.
    for a in box.select(
        'a[href*="/startseite/verein/"], '
        'a[href*="/verein/"]'
    ):
        candidate = clean(a.get("title", "")) or clean(a.get_text(" ", strip=True))
        if valid_club_heading(candidate):
            return candidate

    return ""


def find_club_for_table(table):
    """
    Détermine le club auquel appartient un tableau Arrivées/Départs.
    """
    # 1. Parent .box : méthode principale.
    box = table.find_parent("div", class_=lambda x: x and "box" in x.split())
    club = extract_club_from_box(box)

    if club:
        return club

    # 2. Fallback : remonter les parents et chercher un heading pertinent.
    parent = table.parent
    depth = 0

    while parent is not None and depth < 8:
        heading = parent.find("h2")
        if heading:
            candidate = clean(heading.get_text(" ", strip=True))
            if valid_club_heading(candidate):
                return candidate

        parent = parent.parent
        depth += 1

    # 3. Dernier fallback : heading précédent pertinent.
    for h in table.find_all_previous(["h1", "h2", "h3"], limit=10):
        candidate = clean(h.get_text(" ", strip=True))
        if valid_club_heading(candidate):
            return candidate

    return ""


def parse_table(table, movement, current_club, url):
    headers = headers_of(table)

    i_age = index_of(headers, "âge", "age")
    i_pos = index_of(headers, "position")
    i_from = index_of(headers, "venant de")
    i_to = index_of(headers, "allant à", "allant a")
    i_fee = index_of(headers, "montant")

    rows = []

    for tr in table.select("tbody tr"):
        cells = tr.find_all("td", recursive=False)

        if not cells:
            continue

        player_name = extract_player_name(tr)
        if not player_name:
            continue

        age = (
            clean(cells[i_age].get_text(" ", strip=True))
            if i_age is not None and i_age < len(cells)
            else ""
        )

        position = (
            clean(cells[i_pos].get_text(" ", strip=True))
            if i_pos is not None and i_pos < len(cells)
            else ""
        )

        fee = (
            clean(cells[i_fee].get_text(" ", strip=True))
            if i_fee is not None and i_fee < len(cells)
            else ""
        )

        if movement == "arrival":
            old_club = (
                extract_team_from_cell(cells[i_from])
                if i_from is not None and i_from < len(cells)
                else ""
            )
            new_club = current_club

        else:
            old_club = current_club
            new_club = (
                extract_team_from_cell(cells[i_to])
                if i_to is not None and i_to < len(cells)
                else ""
            )

        rows.append({
            "page_club": current_club,
            "player_name": player_name,
            "player_name_normalized": norm(player_name),
            "old_club": old_club,
            "old_club_normalized": norm(old_club),
            "new_club": new_club,
            "new_club_normalized": norm(new_club),
            "movement": movement,
            "age": age,
            "position": position,
            "fee": fee,
            "source_url": url,
        })

    return rows


def scrape(url):
    response = requests.get(url, headers=HEADERS, timeout=30)
    response.raise_for_status()

    soup = BeautifulSoup(response.text, "html.parser")

    # Recueillir les tableaux avec le club auquel chacun appartient.
    detected_tables = []

    for table in soup.select("table"):
        movement = detect_movement(table)

        if movement is None:
            continue

        club = find_club_for_table(table)

        if not club:
            print(
                "ATTENTION : tableau ignoré car le club correspondant "
                "n'a pas pu être déterminé."
            )
            continue

        detected_tables.append({
            "table": table,
            "movement": movement,
            "club": clean(club),
        })

    if not detected_tables:
        raise RuntimeError(
            "Aucun tableau Arrivées/Départs reconnu sur la page."
        )

    # Regrouper par club tout en conservant l'ordre d'apparition des clubs.
    club_order = []
    by_club = {}

    for item in detected_tables:
        club = item["club"]

        if club not in by_club:
            by_club[club] = {
                "arrival": [],
                "departure": [],
            }
            club_order.append(club)

        by_club[club][item["movement"]].append(item["table"])

    transfers = []
    global_sequence = 1

    for club_number, club in enumerate(club_order, start=1):
        club_sequence = 1

        # 1. TOUJOURS LES ARRIVÉES DU CLUB
        for table in by_club[club]["arrival"]:
            rows = parse_table(table, "arrival", club, url)

            for row in rows:
                row["sequence"] = global_sequence
                row["club_sequence"] = club_sequence
                row["phase"] = 1

                transfers.append(row)

                global_sequence += 1
                club_sequence += 1

        # 2. PUIS LES DÉPARTS DU MÊME CLUB
        for table in by_club[club]["departure"]:
            rows = parse_table(table, "departure", club, url)

            for row in rows:
                row["sequence"] = global_sequence
                row["club_sequence"] = club_sequence
                row["phase"] = 2

                transfers.append(row)

                global_sequence += 1
                club_sequence += 1

    return club_order, transfers


def save_csv(rows, filename="transfers.csv"):
    fields = [
        "sequence",
        "club_sequence",
        "phase",
        "page_club",
        "player_name",
        "player_name_normalized",
        "old_club",
        "old_club_normalized",
        "new_club",
        "new_club_normalized",
        "movement",
        "age",
        "position",
        "fee",
        "source_url",
    ]

    with open(filename, "w", newline="", encoding="utf-8-sig") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=fields,
            delimiter=";"
        )
        writer.writeheader()
        writer.writerows(rows)


def main():
    if len(sys.argv) != 2:
        print(
            'Usage : py scrape_fc26_transfers_v3.py '
            '"URL_DE_LA_PAGE"'
        )
        raise SystemExit(1)

    url = sys.argv[1]

    try:
        clubs, rows = scrape(url)
    except requests.HTTPError as exc:
        print(f"Erreur HTTP : {exc}")
        print(
            "Si Transfermarkt renvoie 403, on pourra passer "
            "à une version Playwright/Chrome."
        )
        raise SystemExit(2)
    except Exception as exc:
        print(f"Erreur : {exc}")
        raise SystemExit(3)

    if not rows:
        print("Aucun transfert trouvé.")
        raise SystemExit(4)

    output = "transfers.csv"
    save_csv(rows, output)

    print()
    print(f"{len(clubs)} clubs détectés")
    print(f"{len(rows)} mouvements exportés vers {output}")
    print()

    current = None

    for row in rows:
        if row["page_club"] != current:
            current = row["page_club"]
            print()
            print("=" * 70)
            print(current)
            print("=" * 70)

        phase = "ARRIVÉE" if row["phase"] == 1 else "DÉPART"

        print(
            f"{row['sequence']:03d} "
            f"[{phase}] "
            f"{row['player_name']} : "
            f"{row['old_club']} -> {row['new_club']}"
        )


if __name__ == "__main__":
    main()
