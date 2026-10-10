"""Prépare les pages du wiki GitHub à partir de docs/*.md et de CONTRIBUTING.md.

Usage : python miroir_wiki.py <racine du dépôt> <dossier de sortie> <propriétaire/dépôt>

- docs/README.md devient la page d'accueil (Home.md), CONTRIBUTING.md devient Contribuer.md.
- Les liens vers une autre page deviennent des liens de wiki (sans « .md ») ; les liens vers le reste du
  dépôt (code, fichiers) pointent vers GitHub, puisque le wiki ne contient que les pages.
"""

import posixpath
import re
import sys
from pathlib import Path

LIEN = re.compile(r"(\]\()([^)\s]+)(\))")


def nom_de_page(chemin: str) -> str | None:
    """Nom de la page de wiki pour un chemin du dépôt, ou None si ce n'est pas une page."""
    if chemin == "CONTRIBUTING.md":
        return "Contribuer"
    if chemin == "docs/README.md":
        return "Home"
    dossier, fichier = posixpath.split(chemin)
    if dossier == "docs" and fichier.endswith(".md"):
        return fichier[:-3]
    return None


def reecrire(texte: str, source: str, depot: str) -> str:
    def remplacer(m: re.Match[str]) -> str:
        cible = m.group(2)
        if re.match(r"^[a-z][a-z0-9+.-]*:", cible) or cible.startswith("#"):
            return m.group(0)
        chemin, _, ancre = cible.partition("#")
        complet = posixpath.normpath(posixpath.join(posixpath.dirname(source), chemin))
        page = nom_de_page(complet)
        nouveau = page if page else f"https://github.com/{depot}/blob/main/{complet}"
        if ancre:
            nouveau += "#" + ancre
        return m.group(1) + nouveau + m.group(3)

    return LIEN.sub(remplacer, texte)


def main() -> None:
    racine, sortie, depot = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
    sources = sorted(p.relative_to(racine).as_posix() for p in (racine / "docs").glob("*.md"))
    sources.append("CONTRIBUTING.md")
    for source in sources:
        texte = (racine / source).read_text(encoding="utf-8")
        page = nom_de_page(source)
        (sortie / f"{page}.md").write_text(reecrire(texte, source, depot), encoding="utf-8")
        print(f"{source} -> {page}.md")


if __name__ == "__main__":
    main()
