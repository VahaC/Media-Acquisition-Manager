r"""MAM-29 samples of Jackett's Torznab feed of Toloka.

What it is for: MAM-29 lets Jackett stand in for Prowlarr through the same Torznab adapter. This records what Jackett
answers to the very queries the committed prowlarr-toloka-*.xml samples were recorded with, so the two compare 1:1,
plus searches that show the title shapes MAM reads (a film's Ukrainian and original title, a series with seasons, a
Ukrainian-only show, a cartoon series), the newest releases, a wrong key, a refused download and one real download.

Reads:  Jackett's Torznab API, <address>/api/v2.0/indexers/<id>/results/torznab/api, with GET only: caps, 12 searches
        (each one goes on to Toloka with the Toloka login Jackett holds), three requests with a wrong key and one
        download link with its token mangled (both refused by Jackett before Toloka is asked), and, only after you type
        y, ONE real .torrent download through Jackett from Toloka. That download is looked at in memory only: its
        status line and headers, its size, whether it is bencoded with an info dictionary, its v1 info hash, how many
        files it lists and the tracker's HOST name. The .torrent itself is never saved, and no passkey is printed or kept.
Writes: nothing to Jackett or Toloka (Toloka sees your searches and the one .torrent download).
        On this PC: D:\Sources\Media-Acquisition-Manager\.claude\mam29-check\jackett-<version>\ (gitignored):
          raw\              every request and answer; API key, passkeys and download tokens removed
          samples-pending\  REDACTED candidates for VendorSamples\Torznab\jackett-toloka-*, named like the future files
          INDEX.txt         one line per file: what it shows and the Jackett version
          console.txt       everything printed
Asks:   the Jackett address, the indexer id (toloka), the API key (hidden), the Jackett version from the bottom of its
        web page, whether "Strip Cyrillic Letters" is ticked in the Toloka indexer, which listed release to download,
        and y before that one download.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam29\jackett_samples.py

Python 3.10+, standard library only.
"""

from __future__ import annotations

import argparse
import re
import sys
import time
import traceback
import urllib.parse
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass, field
from pathlib import Path

import common
from common import HttpFailure, Reply, Stop, ToolError, box, heading, say

DEFAULT_INDEXER = "toloka"  # Jackett's id of Toloka.to: src/Jackett.Common/Indexers/Definitions/Toloka.cs, Id => "toloka"
WRONG_KEY = "wrong"
TORZNAB_SAMPLES = common.VENDOR_SAMPLES / "Torznab"
CYRILLIC = re.compile(r"[\u0400-\u04FF]")
CANDIDATES_SHOWN = 10


@dataclass(frozen=True)
class Query:
    step: str
    slug: str
    params: tuple[tuple[str, str], ...]   # t first; apikey is put second, as MAM's adapter orders it
    sample: str                           # the future VendorSamples/Torznab file
    prowlarr: str | None                  # the committed Prowlarr sample of the same query
    what: str


# The first block repeats the queries of the committed Prowlarr samples (VendorSamples/README.md, their rows); the
# second asks the title shapes MAM reads by titles from those samples; the third the newest releases.
QUERIES = [
    Query("01", "caps", (("t", "caps"),), "jackett-toloka-caps.xml", "prowlarr-toloka-caps.xml",
          "Toloka's categories and search modes as Jackett lists them"),
    Query("02", "search-interstellar", (("t", "search"), ("q", "Interstellar")), "jackett-toloka-search.xml",
          "prowlarr-toloka-search.xml", "a general search for a film with a Ukrainian and an original title"),
    Query("03", "movie-interstellar", (("t", "movie"), ("q", "Interstellar")), "jackett-toloka-movie-search.xml",
          "prowlarr-toloka-movie-search.xml", "the movie search for the same film"),
    Query("04", "tv-yellowstone", (("t", "tvsearch"), ("q", "Yellowstone")), "jackett-toloka-tv-search.xml",
          "prowlarr-toloka-tv-search.xml", "the TV search for a series with seasons"),
    Query("05", "tv-yellowstone-season-1", (("t", "tvsearch"), ("q", "Yellowstone"), ("season", "1")),
          "jackett-toloka-tv-season-search.xml", "prowlarr-toloka-tv-season-search.xml",
          "the same TV search narrowed to season 1"),
    Query("06", "tv-mickey-mouse-clubhouse", (("t", "tvsearch"), ("q", "Mickey Mouse Clubhouse")),
          "jackett-toloka-tv-cartoon-search.xml", "prowlarr-toloka-tv-cartoon-search.xml",
          "the TV search for a cartoon series"),
    Query("07", "movie-schumacher-94", (("t", "movie"), ("q", "Schumacher '94 - Die Geburt einer Legende 2026"),
                                        ("cat", "2000,2020,2040,2070,5070,5080")),
          "jackett-toloka-movie-search-film-in-tv-category.xml", "prowlarr-toloka-movie-search-film-in-tv-category.xml",
          "a movie search with TV/Anime and TV/Documentary among its categories, for a film Toloka files there"),
    Query("08", "movie-ukrainian-title", (("t", "movie"), ("q", "Інтерстеллар")),
          "jackett-toloka-movie-search-ukrainian-title.xml", None,
          "the movie search by the film's Ukrainian title"),
    Query("09", "tv-ukrainian-only", (("t", "tvsearch"), ("q", "Розслідування з Андрієм Данілевичем")),
          "jackett-toloka-tv-search-ukrainian-only.xml", None,
          "the TV search for a Ukrainian-only show (Prowlarr cut its title to '(2026) WEB-DL 1080p (S1E1-25 of ?)')"),
    Query("10", "tv-cartoon-ukrainian-title", (("t", "tvsearch"), ("q", "Клуб Міккі Мауса")),
          "jackett-toloka-tv-cartoon-search-ukrainian-title.xml", None,
          "the TV search for the cartoon series by its Ukrainian title"),
    Query("11", "latest", (("t", "search"),), "jackett-toloka-latest.xml", None,
          "the general search with no q and no category: the newest releases"),
    Query("12", "latest-movies", (("t", "search"), ("cat", "2000"), ("limit", "100")),
          "jackett-toloka-latest-movies.xml", "prowlarr-toloka-latest-movies.xml",
          "the newest releases in the movie categories"),
    Query("13", "latest-tv", (("t", "search"), ("cat", "5000"), ("limit", "100")),
          "jackett-toloka-latest-tv.xml", "prowlarr-toloka-latest-tv.xml", "the newest releases in the TV categories"),
]


@dataclass
class Exchange:
    request_text: str
    reply: Reply | None = None
    error: str = ""


@dataclass
class Feed:
    root: str = ""              # rss, caps, error; empty when the body is not XML
    items: list[dict] = field(default_factory=list)
    error_code: str = ""
    error_text: str = ""
    categories: int = 0
    parse_error: str = ""


@dataclass
class Release:
    title: str
    size: str
    link: str                   # Jackett's download link as it listed it (holds the key: never printed or written)
    found_by: str


class Jackett:
    """GET requests to Jackett's Torznab API. The key travels as apikey= (Jackett also takes passkey=), the way MAM's
    adapter sends it; Jackett's download links carry it again as jackett_apikey=. Neither is ever printed or written."""

    def __init__(self, base_url: str, indexer: str, api_key: str, timeout: float) -> None:
        parts = common.check_address(base_url, "The Jackett address")
        self.origin = f"{parts.scheme}://{parts.netloc}"
        self.base_path = parts.path.rstrip("/")
        self.indexer = indexer
        self.api_key = api_key
        self.timeout = timeout
        # src/Jackett.Server/Controllers/ResultsController.cs: [Route("api/v2.0/indexers/{indexerId}/results")] and
        # the Torznab action's [Route("[action]/{ignored?}")], so .../results/torznab/api.
        self.endpoint = (f"{self.origin}{self.base_path}/api/v2.0/indexers/"
                         f"{urllib.parse.quote(indexer, safe='')}/results/torznab/api")

    def __repr__(self) -> str:  # never the key
        return f"Jackett({self.origin!r}, indexer={self.indexer!r})"

    def torznab(self, params: tuple[tuple[str, str], ...], key: str | None = None) -> Exchange:
        pairs = [params[0], ("apikey", self.api_key if key is None else key), *params[1:]]
        # Every value escaped as MAM's adapter does (Uri.EscapeDataString), commas of a category list left as they are.
        query = "&".join(f"{urllib.parse.quote(k, safe='')}={urllib.parse.quote(v, safe=',')}" for k, v in pairs)
        return self.get(f"{self.endpoint}?{query}")

    def get(self, url: str, timeout: float | None = None) -> Exchange:
        headers = {"Accept": "application/rss+xml, application/xml;q=0.9, */*;q=0.8"}
        exchange = Exchange(common.describe_request("GET", url, headers))
        try:
            exchange.reply = common.http_request("GET", url, headers=headers, timeout=timeout or self.timeout)
        except HttpFailure as failure:
            exchange.error = str(failure)
        return exchange

    def on_typed_address(self, link: str) -> str | None:
        """A download link of Jackett's own (/dl/<indexer>/?...), moved onto the address the owner typed: the key goes
        only to the Jackett he named, never to a host an answer suggests."""
        parts = urllib.parse.urlsplit(link)
        if parts.scheme not in ("http", "https") or "/dl/" not in parts.path:
            return None
        return f"{self.origin}{parts.path}" + (f"?{parts.query}" if parts.query else "")


class Run:
    def __init__(self, args: argparse.Namespace, jackett: Jackett, rec: common.Recorder) -> None:
        self.args = args
        self.jackett = jackett
        self.rec = rec
        self.releases: list[Release] = []
        self.outcomes: list[tuple[str, str]] = []


# ---------------------------------------------------------------- reading answers

def parse_feed(body: bytes) -> Feed:
    try:
        root = ElementTree.fromstring(body)
    except ElementTree.ParseError as error:
        return Feed(parse_error=str(error))
    tag = root.tag.rsplit("}", 1)[-1]
    feed = Feed(root=tag)
    if tag == "error":
        feed.error_code = root.get("code", "")
        feed.error_text = root.get("description", "")
    elif tag == "caps":
        feed.categories = sum(1 for _ in root.iter("category")) + sum(1 for _ in root.iter("subcat"))
    elif tag == "rss":
        for item in root.iter("item"):
            enclosure = item.find("enclosure")
            feed.items.append({
                "title": item.findtext("title") or "",
                "description": item.findtext("description"),
                "size": item.findtext("size") or "",
                "link": item.findtext("link") or "",
                "enclosure": enclosure.get("url", "") if enclosure is not None else "",
            })
    return feed


def prowlarr_count(name: str | None) -> int | None:
    if not name:
        return None
    path = TORZNAB_SAMPLES / name
    try:
        return path.read_text(encoding="utf-8").count("<item>")
    except OSError:
        return None


def describe_feed(ex: Exchange, feed: Feed, query: Query | None = None) -> str:
    if ex.reply is None:
        return f"NO ANSWER: {ex.error}"
    reply = ex.reply
    text = f"HTTP {reply.status} {reply.reason} in {reply.seconds:.2f} s"
    if not reply.body.strip():
        return text + "; empty body"
    if feed.parse_error:
        # Redacted before it is cut: an error page that quotes the request URL would otherwise leave half the API key
        # in the excerpt, where it no longer matches the key and say() prints it.
        return text + f"; not XML ({' '.join(common.redact_text(reply.text).split())[:120]})"
    if feed.root == "error":
        return text + f"; Torznab error {feed.error_code}: {feed.error_text!r}"
    if feed.root == "caps":
        return text + f"; caps with {feed.categories} categories and subcategories"
    if feed.root == "rss":
        items = feed.items
        cyrillic = sum(1 for item in items if CYRILLIC.search(item["title"]))
        described = sum(1 for item in items if item["description"])
        differs = sum(1 for item in items if item["description"] and item["description"] != item["title"])
        text += (f"; {len(items)} release(s), {cyrillic} with Cyrillic in <title>, {described} with a <description>, "
                 f"{differs} whose <description> differs from <title>")
        known = prowlarr_count(query.prowlarr if query else None)
        if known is not None:
            text += f"; Prowlarr's {query.prowlarr} holds {known}"
        return text
    return text + f"; XML whose root is <{feed.root}>"


def is_xml_sample(ex: Exchange, feed: Feed) -> bool:
    return ex.reply is not None and ex.reply.status == 200 and feed.root in ("rss", "caps")


def record(run: Run, raw_name: str, ex: Exchange, what: str, pending: str | None = None, *, as_xml: bool = False,
           include_body: bool = True, extra: str = "") -> None:
    """raw/<raw_name>.txt holds the request as sent and the whole answer (or, for the download, its head only); the
    candidate is the XML body itself, or the status line, headers and body as -response.txt."""
    head = [f"# MAM-29 {raw_name}: {what}", f"# {common.now_iso()}, {run.rec.version_label}"]
    text = "\n".join(head) + "\n\n" + ex.request_text
    if ex.reply is not None:
        text += f"\n--- answer after {ex.reply.seconds:.3f} s\n" + ex.reply.as_sample(include_body)
    else:
        text += f"\n--- no answer: {ex.error}\n"
    if extra:
        text += "\n" + extra.rstrip("\n") + "\n"
    run.rec.raw(raw_name + ".txt", text, what)
    if pending and ex.reply is not None:
        if as_xml:
            run.rec.pending(pending, ex.reply.text.lstrip("﻿").replace("\r\n", "\n").rstrip("\n") + "\n", what)
        else:
            run.rec.pending(pending, ex.reply.as_sample(include_body), what)


def response_name(sample: str) -> str:
    return sample.rsplit(".", 1)[0] + "-response.txt"


def shown_query(query: Query) -> str:
    return "&".join(f"{k}={v}" for k, v in query.params)


# ---------------------------------------------------------------- the steps

def check_caps(run: Run, ex: Exchange, feed: Feed) -> None:
    """The first answer decides whether anything else can work; each refusal is one sentence with its fix."""
    if ex.reply is None:
        raise Stop(f"Jackett did not answer: {ex.error}.")
    status = ex.reply.status
    # Jackett answers a wrong key on its Torznab API with HTTP 200 and <error code="100">, not with a 401
    # (ResultsController.GetErrorActionResult); both mean the same.
    if status == 401 or feed.error_code == "100":
        raise Stop("Jackett refused the API key. Copy it again from the top right of Jackett's web page (API Key) and "
                   "run again.")
    if feed.error_code in ("200", "201"):
        raise Stop(f"Jackett answered {feed.error_text!r}: it has no configured indexer {run.jackett.indexer!r}. Add "
                   "Toloka.to in Jackett (+ Add indexer) with your Toloka login, or type the id its Torznab feed link "
                   "shows (.../indexers/<id>/results/torznab/).")
    if status == 404:
        raise Stop("Nothing at that address answers as Jackett's Torznab API (HTTP 404). Type Jackett's own address, "
                   f"e.g. {common.DEFAULT_JACKETT_URL}.")
    if 300 <= status < 400:
        raise Stop(f"Jackett answered with a redirect (HTTP {status}): type the address its web page is served at.")
    if feed.root != "caps":
        raise Stop(f"Jackett's answer to t=caps is not a Torznab caps document ({describe_feed(ex, feed)}).")


def run_query(run: Run, query: Query) -> None:
    say(f"  ?{shown_query(query)}")
    ex = run.jackett.torznab(query.params)
    feed = parse_feed(ex.reply.body) if ex.reply is not None and ex.reply.body else Feed()
    text = describe_feed(ex, feed, query)
    say(f"  -> {text}")
    what = f"GET .../torznab/api?{shown_query(query)} ({query.what}): {text}"
    if query.step == "01":
        if not is_xml_sample(ex, feed):
            # The answer that stops the run is kept in raw/ (it is how the owner sees what Jackett said); no candidate,
            # because it is not the caps document.
            record(run, f"{query.step}-{query.slug}", ex, what)
        check_caps(run, ex, feed)
    if is_xml_sample(ex, feed):
        record(run, f"{query.step}-{query.slug}", ex, what, query.sample, as_xml=True)
    else:
        record(run, f"{query.step}-{query.slug}", ex, what, response_name(query.sample))
    for item in feed.items:
        link = item["enclosure"] or item["link"]
        if link and run.jackett.on_typed_address(link):
            run.releases.append(Release(item["title"], item["size"], link, query.slug))
    run.outcomes.append((query.step, text))


def param_value(url: str, name: str) -> str | None:
    match = re.search(r"[?&]%s=([^&]*)" % re.escape(name), url)
    return match.group(1) if match else None


def with_param(url: str, name: str, value: str) -> str:
    return re.sub(r"([?&]%s=)[^&]*" % re.escape(name), lambda m: m.group(1) + value, url, count=1)


def first_release(run: Run) -> Release | None:
    return next((r for r in run.releases if param_value(r.link, "path")), None)


def step_14(run: Run) -> None:
    for letter, slug, params in (("a", "caps", (("t", "caps"),)),
                                 ("b", "search", (("t", "search"), ("q", "Interstellar")))):
        shown = "&".join(f"{k}={v}" for k, v in params)
        say(f"  ?{shown} with apikey={WRONG_KEY}")
        ex = run.jackett.torznab(params, key=WRONG_KEY)
        feed = parse_feed(ex.reply.body) if ex.reply is not None and ex.reply.body else Feed()
        text = describe_feed(ex, feed)
        say(f"  -> {text}")
        record(run, f"14{letter}-wrong-key-{slug}", ex, f"GET .../torznab/api?{shown} with apikey={WRONG_KEY} "
               f"(REDACTED in the request): {text}", f"jackett-wrong-key-{slug}-response.txt")
        run.outcomes.append((f"14{letter}", text))
    release = first_release(run)
    if release is None:
        say("  SKIPPED the wrong-key download: no search listed a Jackett download link.")
        run.outcomes.append(("14c", "skipped: no Jackett download link was listed"))
        return
    url = with_param(run.jackett.on_typed_address(release.link), "jackett_apikey", WRONG_KEY)
    say(f"  a download link of {release.title!r} with jackett_apikey={WRONG_KEY}")
    ex = run.jackett.get(url)
    text = describe_feed(ex, Feed()) if ex.reply is None else (
        f"HTTP {ex.reply.status} {ex.reply.reason} in {ex.reply.seconds:.2f} s, {len(ex.reply.body)} byte(s) of body")
    say(f"  -> {text}")
    record(run, "14c-wrong-key-download", ex, f"GET /dl/<indexer>/?jackett_apikey={WRONG_KEY}&path=...&file=... of a "
           f"real release (Jackett checks the key before it asks Toloka): {text}", "jackett-wrong-key-download-response.txt")
    run.outcomes.append(("14c", text))


def step_15(run: Run) -> None:
    release = first_release(run)
    if release is None:
        say("  SKIPPED: no search listed a Jackett download link.")
        run.outcomes.append(("15", "skipped: no Jackett download link was listed"))
        return
    url = run.jackett.on_typed_address(release.link)
    token = param_value(url, "path") or ""
    # Reversed, the token is still base64url of the same length, but no longer data Jackett protected: it fails to
    # unprotect it and refuses before Toloka is asked (DownloadController).
    mangled = token[::-1] if len(token) > 1 and token[::-1] != token else "mam29-mangled"
    say(f"  the download link of {release.title!r} with its path token mangled")
    ex = run.jackett.get(with_param(url, "path", mangled))
    text = describe_feed(ex, Feed()) if ex.reply is None else (
        f"HTTP {ex.reply.status} {ex.reply.reason} in {ex.reply.seconds:.2f} s, {len(ex.reply.body)} byte(s) of body")
    say(f"  -> {text}")
    record(run, "15-download-bad-path", ex, f"GET /dl/<indexer>/?jackett_apikey=...&path=<mangled>&file=... with the "
           f"right key: {text}", "jackett-download-bad-path-response.txt")
    run.outcomes.append(("15", text))


def pick_release(run: Run) -> Release | None:
    seen: set[str] = set()
    candidates = []
    # The movie search's releases first: the films of the comparison.
    for release in sorted(run.releases, key=lambda r: r.found_by != "movie-interstellar"):
        if release.link not in seen:
            seen.add(release.link)
            candidates.append(release)
    candidates = candidates[:CANDIDATES_SHOWN]
    if not candidates:
        return None
    for number, release in enumerate(candidates, 1):
        size = common.human_size(int(release.size)) if release.size.isdigit() else "size unknown"
        say(f"  {number:>2}. {release.title}  ({size}; found by {release.found_by})")
    while True:
        answer = common.ask("Which release's .torrent to download? Number, or Enter for 1", "1")
        if answer.isdigit() and 1 <= int(answer) <= len(candidates):
            return candidates[int(answer) - 1]
        say(f"  {answer!r} is not a number from 1 to {len(candidates)}.")


def describe_body(reply: Reply) -> tuple[str, list[str]]:
    """What the downloaded body is, read in memory: never its bytes, never an announce URL past its host."""
    body = reply.body
    lines = [f"body: {len(body)} bytes" + (" (cut at the size limit)" if reply.truncated else "") + "; not kept"]
    location = reply.header("Location") or ""
    if 300 <= reply.status < 400 and location.lower().startswith("magnet:"):
        topic = re.search(r"xt=urn:btih:([0-9A-Za-z]+)", location)
        trackers = len(re.findall(r"[?&](?:tr|ws|xs|as)(?:\.\d+)?=", location))
        lines.append(f"a redirect to a magnet: btih {topic.group(1) if topic else '?'}, {trackers} tracker parameter(s) "
                     "(removed from the recorded Location)")
        return "a redirect to a magnet", lines
    if reply.status != 200:
        return f"HTTP {reply.status}", lines
    try:
        facts = common.read_torrent(body)
    except common.BencodeError as error:
        start = body[:200].lower()
        kind = "HTML" if b"<html" in start or b"<!doctype" in start else "not a .torrent"
        lines.append(f"bencoded with an info dictionary: NO ({error}); it looks like {kind}")
        return f"not a .torrent ({kind})", lines
    lines += [
        "bencoded with an info dictionary: yes",
        f"v1 info hash: {facts.v1_hash}" + (" (a hybrid v1+v2 torrent)" if facts.hybrid else ""),
        f"name: {facts.name}",
        f"files: {facts.file_count}, {facts.total_length} bytes ({common.human_size(facts.total_length)})",
        f"tracker host(s): {', '.join(facts.announce_hosts) or '(none)'}; the announce URLs themselves are not kept",
        f"private flag: {'set' if facts.private else 'not set'}",
    ]
    return (f"a .torrent of {facts.file_count} file(s), v1 info hash {facts.v1_hash}, tracker host(s) "
            f"{', '.join(facts.announce_hosts) or 'none'}"), lines


def step_16(run: Run) -> None:
    say("  ONE real download: Jackett fetches the .torrent from Toloka with your Toloka login.")
    release = pick_release(run)
    if release is None:
        say("  SKIPPED: no search listed a Jackett download link.")
        run.outcomes.append(("16", "skipped: no Jackett download link was listed"))
        return
    say()
    box(["DOWNLOAD through Jackett from Toloka (read only; the .torrent is looked at in memory and never saved)",
         f"  release  {release.title}",
         f"  request  GET {run.jackett.origin}/dl/{run.jackett.indexer}/?jackett_apikey=(your key)&path=(token)&file=...",
         "Toloka sees one .torrent download on your account."])
    if not common.confirm("Download it? Type y to download, anything else skips this step"):
        say("  SKIPPED: you did not type y; nothing was downloaded.")
        run.outcomes.append(("16", "skipped: you did not type y"))
        return
    ex = run.jackett.get(run.jackett.on_typed_address(release.link), timeout=max(run.jackett.timeout, 120.0))
    if ex.reply is None:
        text, lines = f"NO ANSWER: {ex.error}", []
    else:
        kind, lines = describe_body(ex.reply)
        text = (f"HTTP {ex.reply.status} {ex.reply.reason} in {ex.reply.seconds:.2f} s, "
                f"{ex.reply.header('Content-Type') or 'no Content-Type'}, {len(ex.reply.body)} bytes: {kind}")
    say(f"  -> {text}")
    for line in lines:
        say(f"     {line}")
    record(run, "16-download", ex, f"GET /dl/<indexer>/?jackett_apikey=...&path=...&file=... of {release.title!r}: "
           f"{text}; the .torrent was not kept", "jackett-download-response.txt", include_body=False,
           extra="\n".join(lines))
    if ex.reply is not None:
        ex.reply.body = b""  # nothing of the .torrent outlives this step
    run.outcomes.append(("16", text))


# ---------------------------------------------------------------- the run

def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Records Jackett's Torznab answers for Toloka as MAM-29 vendor sample candidates: the queries of "
                    "the committed Prowlarr samples, title-shape searches, the newest releases, a wrong key, a refused "
                    "download and, after you type y, one real .torrent download that is never saved.",
        epilog=f"The API key is asked with hidden input. The environment variable {common.ENV_JACKETT_API_KEY} can "
               "stand in for it, for unattended tests only.")
    parser.add_argument("--pause", type=float, default=2.0, metavar="SECONDS",
                        help="the pause between two requests that reach Toloka; default %(default)g")
    parser.add_argument("--out", default=None, help=f"output root; default: {common.OUT_ROOT}")
    parser.add_argument("--timeout", type=float, default=90.0, help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def check_key_shape(key: str) -> None:
    # A key with a space, a control character or a Ukrainian letter is a paste gone wrong; Jackett would only say
    # "Invalid API Key". The message says what kind of character, never which.
    if any(c.isspace() or ord(c) < 32 or ord(c) > 126 for c in key):
        raise Stop(f"The API key you entered is {len(key)} characters long and holds spaces, control characters or "
                   "non-ASCII letters (a Ukrainian keyboard layout?). Copy it again from the top right of Jackett's web "
                   "page and paste it with a right-click.")


def run_all(args: argparse.Namespace, rec: common.Recorder) -> int:
    say("MAM-29 Jackett samples")
    say("GET requests only. 12 searches go on to Toloka through Jackett; the one real .torrent download waits for your "
        "y and is never saved.")
    say(f"Output: {rec.out_root}\\jackett-<version>\\ (named after the version you type)")
    base = common.ask("Jackett address", common.DEFAULT_JACKETT_URL)
    common.check_address(base, "The Jackett address")
    indexer = common.ask("Indexer id (from its Torznab feed link .../indexers/<id>/results/torznab/)", DEFAULT_INDEXER)
    if not re.fullmatch(r"[A-Za-z0-9._-]+", indexer):
        raise Stop(f"{indexer!r} is not an indexer id: Jackett's ids are letters, digits, '.', '_' and '-'.")
    key = common.ask_secret("Jackett API key (top right of Jackett's web page; typing is hidden)",
                            common.ENV_JACKETT_API_KEY)
    if not key:
        raise Stop("No API key was typed; run again and paste it from the top right of Jackett's web page.")
    check_key_shape(key)
    version = common.ask("Jackett version, as the bottom of its web page shows it (e.g. v0.24.100)")
    strip = common.ask('In Jackett, Toloka.to > Configure: is "Strip Cyrillic Letters" ticked (Jackett ticks it by '
                       "default)? y / n, Enter if you don't know").strip().lower()
    strip_text = {"y": "ticked", "yes": "ticked", "n": "not ticked", "no": "not ticked"}.get(strip, "not known")
    rec.version_label = (f"Jackett {version or '(version not typed)'}, indexer {indexer}, "
                         f"Strip Cyrillic Letters {strip_text}")
    folder = rec.open("jackett-" + (version.strip().lstrip("vV") or "unknown"))
    say(f"Output folder: {folder}")
    run = Run(args, Jackett(base, indexer, key, args.timeout), rec)
    try:
        for number, query in enumerate(QUERIES):
            heading(f"{query.step}  {query.what}")
            if number > 1:
                time.sleep(args.pause)  # caps doesn't reach Toloka; every search does
            run_query(run, query)
        heading("14  a wrong API key: caps, a search and a download link (Jackett refuses before Toloka is asked)")
        step_14(run)
        heading("15  a download link whose token is mangled (Jackett refuses before Toloka is asked)")
        step_15(run)
        heading("16  ONE real .torrent download through Jackett from Toloka (after you type y)")
        time.sleep(args.pause)
        step_16(run)
    finally:
        if run.outcomes:
            heading("RESULT")
            for step, text in run.outcomes:
                say(f"{step}  {text}")
    return 0


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(argv)
    common.reset_run()
    out_root = Path(args.out).resolve() if args.out else common.OUT_ROOT
    if common.inside_repo_outside_claude(out_root):
        print(f"--out {out_root} is inside the repository but not under .claude\\ (gitignored); choose another folder.",
              flush=True)
        return 2
    rec = common.Recorder(out_root, "jackett")
    status = 1
    try:
        status = run_all(args, rec)
    except KeyboardInterrupt:
        say("\nInterrupted (Ctrl+C). Nothing was repeated.")
        status = 130
    except ToolError as error:
        say(f"\nSTOPPED: {error}")
    except Exception:  # noqa: BLE001 - a bug in this script: say where, with every secret scrubbed
        say("\nUNEXPECTED ERROR (a bug in this script; nothing was repeated):")
        say(common.redact_text(traceback.format_exc()))
    finally:
        if rec.folder is not None or rec.entries:
            if rec.finish():
                status = status or 3
    return status


if __name__ == "__main__":
    sys.exit(main())
