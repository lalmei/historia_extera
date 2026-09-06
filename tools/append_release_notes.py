#!/usr/bin/env python3
"""Add newly merged pull requests to the draft release without rewriting it.

Release Drafter regenerates the whole body on every run, which discards any
hand-editing of the draft. This script only ever inserts: it reads the draft
that is already there, works out which merged pull requests it does not yet
mention, and appends one bullet per missing pull request under the heading its
category belongs to. Everything else in the body is left byte-for-byte alone.

Categories come from the pull request's labels first, then the conventional
type in its title, then the gitmoji the title leads with. The bullet reuses the
title verbatim, so the domain emoji the author chose is what shows up in the
notes.

Environment: GITHUB_TOKEN, GITHUB_REPOSITORY, VERSION. Pass --dry-run to
print what would be added without touching the release.
"""

from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

API = "https://api.github.com"

# The headings this repository uses, in the order they should appear in a draft
# that has to be built from nothing. An existing draft keeps its own order.
SECTIONS = [
    "✨ Features",
    "🐛 Bug Fixes",
    "💄 Visual",
    "⚡️ Performance",
    "♻️ Refactors",
    "🧪 Tests",
    "📝 Documentation",
    "🔧 Maintenance",
    "🧩 Other",
]

LABEL_SECTIONS = {
    "enhancement": "✨ Features",
    "feature": "✨ Features",
    "bug": "🐛 Bug Fixes",
    "style": "💄 Visual",
    "performance": "⚡️ Performance",
    "refactor": "♻️ Refactors",
    "test": "🧪 Tests",
    "documentation": "📝 Documentation",
    "chore": "🔧 Maintenance",
    "ci": "🔧 Maintenance",
}

TYPE_SECTIONS = {
    "feat": "✨ Features",
    "fix": "🐛 Bug Fixes",
    "style": "💄 Visual",
    "perf": "⚡️ Performance",
    "refactor": "♻️ Refactors",
    "test": "🧪 Tests",
    "docs": "📝 Documentation",
    "chore": "🔧 Maintenance",
    "ci": "🔧 Maintenance",
    "build": "🔧 Maintenance",
}

# Last resort: the gitmoji the title leads with. Only the unambiguous ones —
# a domain emoji like 🌌 or 🤝 says what the change is about, not what kind of
# change it is, so those fall through to the conventional type instead.
EMOJI_SECTIONS = {
    "✨": "✨ Features",
    "🔭": "✨ Features",
    "🎉": "✨ Features",
    "🐛": "🐛 Bug Fixes",
    "🚑": "🐛 Bug Fixes",
    "🏚️": "🐛 Bug Fixes",
    "🧹": "🐛 Bug Fixes",
    "💄": "💄 Visual",
    "🎨": "💄 Visual",
    "⚡": "⚡️ Performance",
    "⚡️": "⚡️ Performance",
    "♻️": "♻️ Refactors",
    "🧪": "🧪 Tests",
    "✅": "🧪 Tests",
    "📝": "📝 Documentation",
    "📖": "📝 Documentation",
    "📚": "📝 Documentation",
    "🔧": "🔧 Maintenance",
    "👷": "🔧 Maintenance",
    "💚": "🔧 Maintenance",
    "⬆️": "🔧 Maintenance",
    "📦": "🔧 Maintenance",
    "🚧": "🔧 Maintenance",
    "⚖️": "🔧 Maintenance",
    "🚀": "🔧 Maintenance",
}

SKIP_LABELS = {"skip-changelog", "duplicate", "invalid"}

FOOTER_HEADING = "## License"

# A conventional prefix, after any leading emoji or punctuation has been peeled
# off: "feat:", "fix(viewer):", "docs :".
CONVENTIONAL = re.compile(r"^([a-z]+)\s*(?:\([^)]*\))?\s*!?\s*:", re.IGNORECASE)


def api(path: str, token: str, method: str = "GET", payload: dict | None = None):
    url = path if path.startswith("http") else f"{API}{path}"
    data = json.dumps(payload).encode() if payload is not None else None
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Authorization", f"Bearer {token}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("X-GitHub-Api-Version", "2022-11-28")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request) as response:
        return json.load(response)


def strip_leading_emoji(title: str) -> tuple[str, str]:
    """Split a title into the gitmoji it leads with and the rest."""
    match = re.match(r"^\s*([^\w\s\[(]+)\s*", title)
    if not match:
        return "", title.strip()
    return match.group(1).strip(), title[match.end():].strip()


def section_for(pull: dict) -> str | None:
    labels = {label["name"].lower() for label in pull.get("labels", [])}
    if labels & SKIP_LABELS:
        return None
    # Walk the map, not the labels: a pull request can carry more than one
    # (a "🧹 fix:" title earns both `chore` and `bug`), and LABEL_SECTIONS is
    # written in priority order.
    for label, section in LABEL_SECTIONS.items():
        if label in labels:
            return section

    emoji, rest = strip_leading_emoji(pull["title"])
    conventional = CONVENTIONAL.match(rest)
    if conventional:
        kind = conventional.group(1).lower()
        if kind in TYPE_SECTIONS:
            return TYPE_SECTIONS[kind]

    # Compare on the emoji's base codepoint so a title that omits the variation
    # selector (⚡ vs ⚡️) still lands in the same place.
    base = emoji.replace("️", "")
    for known, section in EMOJI_SECTIONS.items():
        if known.replace("️", "") == base:
            return section

    return "🧩 Other"


def merged_since(repo: str, token: str, since: str | None) -> list[dict]:
    """Merged pull requests against the default branch, oldest merge first."""
    query = f"repo:{repo} is:pr is:merged base:main"
    if since:
        query += f" merged:>{since}"
    found: list[dict] = []
    page = 1
    while True:
        encoded = urllib.parse.urlencode(
            {"q": query, "per_page": 100, "page": page, "sort": "created", "order": "asc"}
        )
        result = api(f"/search/issues?{encoded}", token)
        found.extend(result["items"])
        if len(found) >= result["total_count"] or not result["items"]:
            break
        page += 1
    # The search API does not return labels reliably for every item, and never
    # returns merged_at, so re-read each pull request.
    detailed = [api(f"/repos/{repo}/pulls/{item['number']}", token) for item in found]
    detailed.sort(key=lambda pull: pull["merged_at"] or "")
    return detailed


def bullet(pull: dict) -> str:
    author = pull["user"]["login"]
    if pull["user"]["type"] == "Bot":
        byline = f"[@{author}](https://github.com/apps/{author.removesuffix('[bot]')})"
    else:
        byline = f"@{author}"
    return f"- {pull['title'].strip()} (#{pull['number']}) {byline}"


def insert(body: str, section: str, line: str) -> str:
    """Put one bullet at the end of a section, adding the section if needed."""
    lines = body.splitlines()
    heading = f"### {section}"

    start = next((i for i, text in enumerate(lines) if text.strip() == heading), None)
    if start is None:
        return add_section(lines, section, line)

    # Walk to the next heading of the same or higher level; back up over the
    # blank lines that separate sections so the bullet joins the last list.
    end = len(lines)
    for i in range(start + 1, len(lines)):
        if re.match(r"^#{1,3} ", lines[i]):
            end = i
            break
    while end > start + 1 and not lines[end - 1].strip():
        end -= 1

    lines.insert(end, line)
    return "\n".join(lines) + "\n"


def add_section(lines: list[str], section: str, line: str) -> str:
    """Create a missing section in the canonical order, above the footer."""
    order = SECTIONS.index(section) if section in SECTIONS else len(SECTIONS)
    positions = {}
    for i, text in enumerate(lines):
        if text.startswith("### "):
            positions[text[4:].strip()] = i

    at = len(lines)
    for i, text in enumerate(lines):
        if text.strip() == FOOTER_HEADING:
            at = i
            break
    for name, index in positions.items():
        rank = SECTIONS.index(name) if name in SECTIONS else len(SECTIONS)
        if rank > order:
            at = min(at, index)

    while at > 0 and not lines[at - 1].strip():
        at -= 1

    block = ["", f"### {section}", "", line]
    lines[at:at] = block
    return "\n".join(lines) + "\n"


def main() -> int:
    dry_run = "--dry-run" in sys.argv
    token = os.environ["GITHUB_TOKEN"]
    repo = os.environ["GITHUB_REPOSITORY"]
    version = os.environ["VERSION"]
    tag = f"v{version}"

    releases = api(f"/repos/{repo}/releases?per_page=100", token)
    # A draft with no tag pushed yet reports a synthetic "untagged-..." slug as
    # its tag_name, so the name is what actually identifies it.
    drafts = [r for r in releases if r["draft"]]
    draft = next(
        (r for r in drafts if r["tag_name"] == tag),
        next((r for r in drafts if (r["name"] or "") == tag), None),
    )
    published = [r for r in releases if not r["draft"] and not r["prerelease"]]
    published.sort(key=lambda r: r["published_at"] or "", reverse=True)
    since = published[0]["published_at"] if published else None

    if draft is None and dry_run:
        print(f"No draft for {tag}; a real run would create one.")
        draft = {"id": 0, "body": ""}
    elif draft is None:
        body = f"## What's Changed in {tag}\n"
        draft = api(
            f"/repos/{repo}/releases",
            token,
            method="POST",
            payload={
                "tag_name": tag,
                "name": tag,
                "draft": True,
                "body": body,
            },
        )
        print(f"Created a draft for {tag}.")

    body = draft["body"] or f"## What's Changed in {tag}\n"
    mentioned = set(re.findall(r"\(#(\d+)\)", body))

    added = 0
    for pull in merged_since(repo, token, since):
        number = str(pull["number"])
        if number in mentioned:
            continue
        section = section_for(pull)
        if section is None:
            print(f"  skip  #{number} {pull['title']}")
            continue
        body = insert(body, section, bullet(pull))
        mentioned.add(number)
        added += 1
        print(f"  add   #{number} -> {section}")

    if not added:
        print("Draft already mentions every merged pull request; left untouched.")
        return 0

    if dry_run:
        print(f"Dry run: {added} pull request(s) would be appended to the {tag} draft.")
        return 0

    api(
        f"/repos/{repo}/releases/{draft['id']}",
        token,
        method="PATCH",
        payload={"body": body},
    )
    print(f"Appended {added} pull request(s) to the {tag} draft.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"GitHub API error {error.code}: {error.read().decode()}", file=sys.stderr)
        sys.exit(1)
