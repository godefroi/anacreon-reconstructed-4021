"""Build the player manual as linked HTML pages for the Pages site."""

from __future__ import annotations

import re
import subprocess
import tempfile
from html import unescape
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "reference" / "Anacreon Manual.docx"
OUTPUT = ROOT / "docs" / "manual"
CSS = OUTPUT / "manual.css"

PAGE_ORDER = [
    "Preface",
    "Words of Thanks",
    "Introduction",
    "Part I: Tutorial",
    "Part II: The Discourses",
    "Part III: Reference",
    "Glossary",
    "Appendix A: Scenario Files",
    "Appendix B: Questions & Answers",
    "Appendix C: Random Notes",
    "Index",
]

def slug(title: str) -> str:
    value = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    return value or "section"


PAGE_FILES = {
    title: f"{slug(title)}.html"
    for title in PAGE_ORDER
}
PAGE_FILES["Index"] = "index-entries.html"


def run_pandoc(*args: str, input_text: str | None = None) -> str:
    result = subprocess.run(
        ["pandoc", *args],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        input=input_text,
    )
    return result.stdout


def page_shell(title: str, body: str, previous: str | None, next_page: str | None) -> str:
    previous_link = f'<a href="{previous}">← Previous</a>' if previous else '<span></span>'
    next_link = f'<a href="{next_page}">Next →</a>' if next_page else '<span></span>'
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{title} · Anacreon Manual</title>
  <link rel="stylesheet" href="manual.css">
</head>
<body>
  <header class="manual-header">
    <div class="manual-header-inner">
      <a href="../">Anacreon Reconstructed 4021</a>
      <nav class="manual-links" aria-label="Site navigation">
        <a href="../">Home</a>
        <a href="../#screenshots">Screenshots</a>
        <a href="index.html">Manual</a>
        <a href="../#play">Play</a>
      </nav>
    </div>
  </header>
  <div class="manual-layout">
    <aside class="manual-nav">
      <p class="manual-nav-title">ANACREON MANUAL</p>
      <a href="index.html">Contents</a>
      {navigation_links()}
    </aside>
    <main class="manual-content">
      {body}
      <nav class="page-nav" aria-label="Manual navigation">
        {previous_link}
        {next_link}
      </nav>
    </main>
  </div>
</body>
</html>
"""


def navigation_links() -> str:
    return "\n      ".join(
        f'<a href="{PAGE_FILES[title]}">{title}</a>' for title in PAGE_ORDER
    )


def repair_tables(markdown: str) -> str:
    """Restore tables that the DOCX importer emits as line-oriented text."""

    tables = {
        "Construction Requirement Table": """Construction Requirement Table

| Construction | Tech | Chem | Met | Tri | Years to Build |
| --- | --- | ---: | ---: | ---: | ---: |
| Command Base | s | 460 | 2,300 | 180 | 6 |
| Industrial Complex | s | 590 | 2,600 | 150 | 10 |
| Disrupter | pg | 1,110 | 1,180 | 1,120 | 8 |
| Fortress | pg | 840 | 2,870 | 250 | 12 |
| Gate | g | 2,530 | 3,920 | 1,450 | 15 |
| Outpost | b | 350 | 1,120 | 150 | 3 |
| SRM | s | 110 | 500 | 80 | 2 |
| Warp Link | pg | 1,560 | 2,550 | 290 | 5 |""",
        "Fleet Composition by Type": """Fleet Composition by Type

| Type | fgt | hkr | jmp | jtn | pen | str | trn |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Warp fleet | yes | yes | yes | yes | yes | yes | yes |
| Fast warp | no | yes | yes | yes | yes | no | no |
| Jump fleet | no | yes | yes | yes | no | no | no |
| Stealth | no | yes | no | no | yes | no | no |
| HK | no | yes | no | no | no | no | no |""",
        "Fleet Detection And Scanning By Type": """Fleet Detection And Scanning By Type

| Type | Starbase Detect | Starbase Scan | World Detect | World Scan | Fleet Detect | Fleet Scan |
| --- | --- | --- | --- | --- | --- | --- |
| Warp fleet | 5(5) | 5(5) | 5(1) | 1(1) | 1(1) | 1(1) |
| Fast warp | 5(5) | 5(5) | 5(1) | 1(1) | 1(1) | 1(1) |
| Jump fleet | 5(5) | 5(5) | 5(1) | 1(1) | 1(1) | 1(1) |
| Stealth | 5(5) | 5(5) | 1(1) | 1(1) | 1(1) | 1(1) |
| HK | 5(5) | 5(5) | 0(0) | 0(0) | 0(0) | 0(0) |""",
        "Ground Defenses by Type": """Ground Defenses by Type

| Defense | Arms | Armor | Range |
| --- | --- | --- | --- |
| Def Satellites | very heavy | very heavy | high-orbit |
| GDM | medium | none | orbit |
| Ion Cannons | medium | heavy | sub-orbit |
| LAM | heavy | none | (special)* |""",
        "Ship Capabilities by Type": """Ship Capabilities by Type

| Ship | Drive | Arms | Armor |
| --- | --- | --- | --- |
| Fighter | warp | very light | none |
| Hunter-killer | jump | medium | light |
| Jumpship | jump | light | light |
| Jumptransport | jump | none | light |
| Penetrator | warp* | heavy | medium |
| Starship | warp | very heavy | very heavy |
| Transport | warp | none | none |""",
        "Technology Level Table": """Technology Level Table

| Tech level | Technologies |
| --- | --- |
| pre-tech | supplies |
| primitive | men, metals |
| pre-atomic | chemicals |
| atomic | GDMs, trillum |
| pre-warp | fighters |
| warp | transports |
| jump | ion cannons, jumpships, jumptransports |
| bio-tech | defense satellites, hunter-killers, penetrators, ambrosia, outposts |
| starship | LAMs, starships, ninjas, SRMs, industrial complexes, command bases |
| pre-gate | fortresses, warp links |
| gate | gates |""",
        "World Class Table": """World Class Table

| Symb | Class | Advantages | Disadvantages |
| --- | --- | --- | --- |
| A | Ambrosia | Bio | Met/Tri |
| a | Arid | (None) | Chem/Agrc |
| 0 | Artificial | Ships | Chem/Met/Agrc/Tri |
| B | Barren | Met/Tri | Chem/Agrc |
| j | Class J | Chem/Met | Tri |
| k | Class K | Met | Chem |
| l | Class L | Tri | Chem |
| m | Class M | Agrc | Trillum |
| D | Desert | Tri | Chem/Met/Agrc |
| E | Earth-Like | (None) | (None) |
| F | Forest | Chem/Agrc | (None) |
| G | Gas Giant | Chem/Ships | Met/Agrc/Tri |
| h | Hostile | (None) | (None) |
| I | Ice | (None) | Chem/Met/Agrc/Tri |
| J | Jungle | Chem/Agrc | (None) |
| O | Ocean | Chem/Agrc | Met/Tri |
| 1 | Paradise | Bio/Chem/Met/Agrc/Tri | (None) |
| P | Poisonous | Chem | Met/Agrc/Tri |
| 2 | Ruins | (None) | (None) |
| U | Underground | Met/Tri | Agrc |
| V | Volcanic | Chem/Met/Tri | Agrc |""",
        "World Types": """World Types

| Type | Tech | Notes |
| --- | --- | --- |
| a | pt | agricultural, food (sup) |
| A | b | ambrosia, ambrosia (amb) |
| b | pw | base planet, ships and LAMs |
| C | - | capital, the center of the empire |
| c | pa | chemical, chemicals (che) |
| i | pt | independent, ships |
| j | base | jumpship base, jumpships (hkr jmp jtn) |
| m | p | mine, metals (met) |
| N | base | ninja, creates ninjas (nnj) |
| o | - | outpost, starbase |
| r | p | raw material, materials (che met tri) |
| s | base | starship base, starships (pen str) |
| t | pw | transport base, warpships (fgt trn) |
| z | a | trillum mine, trillum (tri) |
| U | j | university, discovers new technology |""",
        "Fuel Requirements": """Fuel Requirements

| Ship type | Range | Kilotons needed for 1,000 ships |
| --- | ---: | ---: |
| Fighter | 3 | <1 |
| Hunter-killer | 8 | 30 |
| Jumpship | 5 | 15 |
| Jumptransport | 15 | 50 |
| Penetrator | 20 | 75 |
| Starship | 20 | 110 |
| Transports | 20 | 80 |""",
        "Rebellion Table": """Rebellion Table

| Population (billions) | Rebels (legions) | Troops killed per year (legions) |
| ---: | ---: | ---: |
| 0.1 | 200 | 40 |
| 0.5 | 460 | 92 |
| 1.0 | 650 | 130 |
| 5.0 | 1,450 | 290 |
| 10.0 | 2,050 | 410 |
| 15.0 | 2,520 | 504 |
| 20.0 | 2,910 | 582 |
| 25.0 | 3,250 | 650 |
| 30.0 | 3,560 | 712 |
| 35.0 | 3,850 | 770 |
| 40.0 | 4,110 | 822 |
| 45.0 | 4,360 | 872 |
| 50.0 | 4,600 | 920 |""",
        "World Type Table": """World Type Table

| Constant | Type |
| ---: | --- |
| 0 | Agricultural |
| 1 | Ambrosia |
| 2 | Base Planet |
| 3 | (reserved) |
| 4 | Capital |
| 5 | Chemical |
| 6 | Independent |
| 7 | Jumpship Base |
| 8 | (reserved) |
| 9 | Metal Mining |
| 10 | Ninja |
| 11 | (reserved) |
| 12 | Raw Material |
| 13 | (reserved) |
| 14 | Starship Base |
| 15 | (reserved) |
| 16 | Transport Base |
| 17 | (reserved) |
| 18 | University |
| 19 | (reserved) |
| 20 | Trillum Mine |""",
        "Constant Tech Level": """Technology Level Table

| Constant | Tech level |
| ---: | --- |
| 0 | pre-technological |
| 1 | primitive |
| 2 | pre-atomic |
| 3 | atomic |
| 4 | pre-warp |
| 5 | warp |
| 6 | jump |
| 7 | bio-technology |
| 8 | starship |
| 9 | pre-gate |
| 10 | gate |""",
    }

    table_ends = {
        "Construction Requirement Table": r"Tech is|###|#",
        "Fleet Composition by Type": r"###|#",
        "Fleet Detection And Scanning By Type": r"###|#",
        "Ground Defenses by Type": r"\*LAMs|###|#",
        "Ship Capabilities by Type": r"\*penetrators|###|#",
        "Technology Level Table": r"###|#",
        "World Class Table": r"What if|###|#",
        "World Types": r"\*Tech\*|Tech is|###|#",
        "Fuel Requirements": r"The above table|###|#",
        "Rebellion Table": r"To have a reasonable chance|###|#",
        "World Type Table": r"Technology Level Table|###|#",
        "Constant Tech Level": r"Technologies|###|#",
    }
    world_type_table = tables.pop("World Type Table")
    markdown, count = re.subn(
        r"(?ms)^World Type Table\n\n.*?(?=\n\nTechnology Level Table)",
        world_type_table,
        markdown,
        count=1,
    )
    if count != 1:
        raise SystemExit("Could not restore world type constants table")

    constant_tech_table = tables.pop("Constant Tech Level")
    markdown, count = re.subn(
        r"(?ms)^Constant Tech Level\n\n.*?(?=\n\nTechnologies)",
        constant_tech_table,
        markdown,
        count=1,
    )
    if count != 1:
        raise SystemExit("Could not restore technology constants table")

    replace_all = {
        "Ground Defenses by Type",
        "Ship Capabilities by Type",
        "World Class Table",
        "World Types",
        "Fuel Requirements",
        "Rebellion Table",
    }
    for marker, replacement in tables.items():
        pattern = rf"(?ms)^{re.escape(marker)}\n\n.*?(?=\n\n(?:{table_ends[marker]}))"
        markdown, count = re.subn(
            pattern,
            replacement,
            markdown,
            count=0 if marker in replace_all else 1,
        )
        if count == 0:
            raise SystemExit(f"Could not restore manual table: {marker}")

    technology_levels = """Technology Levels

| Tech level | Notes |
| --- | --- |
| pt | pre-tech, medieval |
| p | primitive, industrial revolution |
| pa | pre-atomic, US/Europe circa 1900s |
| a | atomic, atomic power |
| pw | pre-warp, interplanetary travel |
| w | warp, interstellar travel |
| j | jump, basic jumpdrive |
| b | bio-tech, genetic engineering |
| s | starship, large-scale engineering |
| pg | pre-gate, planetary engineering |
| g | gate, stargate technology |"""
    markdown, count = re.subn(
        r"(?ms)^Technology Levels\n\nTech Level Notes\n\n.*?(?=\n\n\*Pop)",
        technology_levels,
        markdown,
        count=1,
    )
    if count != 1:
        raise SystemExit("Could not restore tutorial technology table")

    markdown, count = re.subn(
        r"(?ms)(Revolution index is measured in five steps:)\n\n.*?(?=\n\n\*Transports)",
        r"\1\n\n| Level | Meaning |\n| --- | --- |\n| no | No chance of rebellion. |\n| Lo- | Dissatisfaction. |\n| Lo+ | Riots and demonstrations. |\n| Hi- | Rebellion likely. |\n| Hi+ | Rebellion imminent. |",
        markdown,
        count=1,
    )
    if count != 1:
        raise SystemExit("Could not restore revolution index table")
    return markdown.replace("\u00ad", "")


def normalized_heading(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", unescape(value).lower())


def manual_headings(rendered_bodies: dict[str, str]) -> dict[str, list[tuple[str, str]]]:
    headings: dict[str, list[tuple[str, str]]] = {}
    for title, body in rendered_bodies.items():
        headings[title] = [
            (unescape(re.sub(r"<[^>]+>", "", text)), identifier)
            for identifier, text in re.findall(
                r'<section id="([^"]+)"[^>]*>\s*<h[1-3][^>]*>(.*?)</h[1-3]>',
                body,
                re.DOTALL,
            )
        ]
    return headings


def toc_entries(markdown: str) -> list[tuple[str, int]]:
    match = re.search(
        r"(?ms)^#\s+Table of Contents\n\n.*?(?=\n\n#\s+Introduction)",
        markdown,
    )
    if not match:
        raise SystemExit("Could not find the manual table of contents")

    entries = []
    for line in match.group(0).splitlines()[2:]:
        line = re.sub(r"^>\s*", "", line).strip()
        item = re.match(r"(.+?)\s+(\d+)$", line)
        if item:
            entries.append((item.group(1).replace(r"\...", "..."), int(item.group(2))))
    return entries


def index_links(markdown: str, source_markdown: str, rendered_bodies: dict[str, str]) -> str:
    entries = toc_entries(source_markdown)
    headings = manual_headings(rendered_bodies)
    top_level = {
        "Introduction",
        "Part I: Tutorial",
        "Part II: The Discourses",
        "Part III: Reference",
        "Glossary",
        "Appendix A: Scenario Files",
        "Appendix B: Questions & Answers",
        "Appendix C: Random Notes",
    }
    section_starts = [
        (page, title)
        for title, page in entries
        if title in top_level
    ]

    page_targets: dict[int, str] = {}
    for title, page in entries:
        section = max(
            (item for item in section_starts if item[0] <= page),
            default=(1, "Introduction"),
        )[1]
        target_headings = headings[section]
        title_key = normalized_heading(title)
        candidate = next(
            (
                identifier
                for heading, identifier in target_headings
                if normalized_heading(heading) == title_key
            ),
            None,
        )
        if candidate is None:
            candidate = next(
                (
                    identifier
                    for heading, identifier in target_headings
                    if normalized_heading(heading).startswith(title_key)
                    or title_key.startswith(normalized_heading(heading))
                ),
                None,
            )
        if candidate is None:
            candidate = f"f-{slug(title)}"
        page_targets[page] = f"{PAGE_FILES[section]}#{candidate}"

    def link_page_number(match: re.Match[str]) -> str:
        page = int(match.group(1))
        target = page_targets.get(page)
        if target is None:
            nearest = max(
                (number for number in page_targets if number <= page),
                default=min(page_targets),
            )
            target = page_targets[nearest]
        return f"[{page}]({target})"

    return re.sub(r"(?<![\w])(\d{1,3})(?![\w])", link_page_number, markdown)


def main() -> None:
    if not SOURCE.exists():
        raise SystemExit(f"Manual source not found: {SOURCE}")

    with tempfile.TemporaryDirectory() as temporary:
        markdown_path = Path(temporary) / "manual.md"
        subprocess.run(
            [
                "pandoc",
                str(SOURCE),
                "--from=docx",
                "--to=markdown",
                "--wrap=none",
                "--output",
                str(markdown_path),
            ],
            cwd=ROOT,
            check=True,
        )
        markdown = markdown_path.read_text(encoding="utf-8")
    markdown = repair_tables(markdown)

    parts = re.split(r"(?m)^(?=# )", markdown)
    sections: dict[str, str] = {}
    for part in parts:
        match = re.match(r"#\s+(.+?)\s*$", part, re.MULTILINE)
        if match:
            title = re.sub(r"\s+", " ", match.group(1)).strip()
            sections[title] = part

    missing = [title for title in PAGE_ORDER if title not in sections]
    if missing:
        raise SystemExit(f"Missing expected manual sections: {', '.join(missing)}")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    for old_page in OUTPUT.glob("*.html"):
        old_page.unlink()

    rendered_bodies = {}
    for title in PAGE_ORDER:
        source = sections[title]
        if title == "Index":
            source = index_links(source, markdown, rendered_bodies)
        rendered_bodies[title] = run_pandoc(
            "--from=markdown",
            "--to=html5",
            "--section-divs",
            "--id-prefix=f-",
            input_text=source,
        )

    page_names = [PAGE_FILES[title] for title in PAGE_ORDER]
    for index, title in enumerate(PAGE_ORDER):
        body = rendered_bodies[title]
        page = page_shell(
            title,
            body,
            page_names[index - 1] if index else None,
            page_names[index + 1] if index + 1 < len(page_names) else None,
        )
        (OUTPUT / page_names[index]).write_text(page, encoding="utf-8")

    contents = "\n".join(
        f'        <li><a href="{PAGE_FILES[title]}">{title}</a></li>'
        for title in PAGE_ORDER
    )
    index = f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Anacreon Manual</title>
  <link rel="stylesheet" href="manual.css">
</head>
<body>
  <header class="manual-header">
    <div class="manual-header-inner">
      <a href="../">Anacreon Reconstructed 4021</a>
      <nav class="manual-links" aria-label="Site navigation">
        <a href="../">Home</a>
        <a href="../#screenshots">Screenshots</a>
        <a href="index.html">Manual</a>
        <a href="../#play">Play</a>
      </nav>
    </div>
  </header>
  <main class="manual-index">
    <p class="kicker">ANACREON // RECONSTRUCTED 4021</p>
    <h1>Imperial Conquest<br><em>in the Far Future</em></h1>
    <p class="lede">HTML edition of the Anacreon player manual. The original text is
    presented here with section navigation, web links, and readable tables.</p>
    <ol class="contents">
{contents}
    </ol>
    <p class="manual-credit">Originally written by George Moromisato. This edition preserves
    the original text while adding web navigation and presentation.</p>
  </main>
</body>
</html>
"""
    (OUTPUT / "index.html").write_text(index, encoding="utf-8")
    print(f"Built {len(PAGE_ORDER)} manual pages in {OUTPUT}")


if __name__ == "__main__":
    main()
