"""Discovery of completed release assets and per-commit candidate builds from the inventory's repositories.

A product built from several repositories (decision 70) lists release sets instead of its repositories' own releases:
one release of each repository that has released, composed into one bundle (release_sets.py). Its release sets follow
its repositories' releases in time — each repository release makes one set with every other repository's newest release
then — and each commit build of one repository makes a candidate set with every other repository's newest release. A
repository with no release yet is left out of the sets until it releases (`notYet` names it); the primary repository
serves the front door, so no set exists before its first release."""
from __future__ import annotations
from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tarfile
import tempfile
import time
import zipfile
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlparse
from urllib.request import Request, HTTPRedirectHandler, build_opener
import catalog
from fleet import rehearsal
import release_sets
from runner import SHA, SLUG, CommandFailed, read_json, require, validate_bundle, write_json


class ArtifactProvider(str, Enum):
    GITHUB_RELEASES = "GitHubReleases"
    GITHUB_ACTIONS = "GitHubActions"
    LOCAL_IMPORT = "LocalImport"
    # A product release composed from one release of each of a product's repositories (decision 70).
    RELEASE_SET = "ReleaseSet"


@dataclass(frozen=True)
class Source:
    product: str
    repository: str
    asset_name: str
    images: tuple[tuple[str, str], ...]
    provider: ArtifactProvider = ArtifactProvider.GITHUB_RELEASES
    # The Actions workflow that builds a commit into a `bundle-<sha>` artifact, and the branch a dispatch runs on.
    # Without one, the product offers published releases only.
    workflow: str | None = None
    default_branch: str = "main"
    # The repository's short name in a product built from several repositories; None for a product's only one.
    component: str | None = None


def sources():
    """Every release source comes from the product inventory, one per repository that has one; a product without one
    deploys hand-imported bundles."""
    result = []
    for product in catalog.products():
        several = bool(product.components)
        for repository, release in product.repositories():
            # A further repository whose deploy.yml names no service yet deploys nothing, so it holds up no set. The control
            # plane derives each repository's services from its deploy.yml again as they change, so one that gains its
            # first deploy.yml joins the sets once it has been read.
            if release is not None and (not several or release.images):
                result.append(Source(product.slug, repository, release.asset, release.images,
                                     workflow=release.workflow, default_branch=product.default_branch,
                                     component=catalog.short_name(product.slug, repository) if several else None))
    return tuple(result)


def product_sources(product):
    """A product's release sources in repository order, primary first."""
    return [source for source in sources() if source.product == product]


def grouped():
    """Each product's release sources, in inventory order."""
    result = {}
    for source in sources():
        result.setdefault(source.product, []).append(source)
    return list(result.values())


def several(group):
    """Whether a product's sources are the repositories of a product built from several."""
    return any(source.component is not None for source in group)


def repository_source(product, repository=None):
    """The release source of one of a product's repositories: the named one, else its primary one."""
    group = product_sources(product)
    require(group, "Product has no approved release source")
    if repository is None:
        return group[0]
    found = next((source for source in group if source.repository.lower() == str(repository).lower()), None)
    require(found is not None, "The repository is not one of the product's")
    return found


VERSION = re.compile(r"v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?")
CANDIDATE = re.compile(r"bundle-([a-f0-9]{40})")
WORKFLOW = catalog.WORKFLOW
BRANCH = re.compile(r"[A-Za-z0-9][A-Za-z0-9._/-]{0,199}")
API = "https://api.github.com/repos/"
MAX_ARCHIVE = 3 * 1024 * 1024


class ApiRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        require(urlparse(new_url).scheme == "https", "Insecure release redirect")
        redirected = super().redirect_request(request, fp, code, message, headers, new_url)
        if redirected is not None and urlparse(new_url).netloc != urlparse(request.full_url).netloc:
            redirected.remove_header("Authorization")
        return redirected


# The account a GitHub API path names: /repos/<owner>/…, /users/<owner>/… or /orgs/<owner>/….
OWNER_PATH = re.compile(r"/(?:repos|users|orgs)/([A-Za-z0-9][A-Za-z0-9-]{0,38})(?:/|$)")
# A read that finds the repository itself — its branch heads, commits, trees, releases, branches — rather than one file
# or run in it: a 404 there means the repository cannot be seen.
REPOSITORY_READ = re.compile(r"/repos/([A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100})"
                             r"(?:/commits(?:/[^/]+)?|/releases|/branches|/git/trees/[^/]+)?/?")
# How long before its expiry an installation token stops being used; the API refreshes it well before.
TOKEN_MARGIN = 60
# The statuses that say a credential is missing, refused or out of rate limit; each is noted for the Issues panel.
ACCESS_STATUSES = (401, 403, 429)


class Credential(str, Enum):
    """Where a GitHub call's token came from."""
    INSTALLATION = "installation"  # the GitHub App installed on the owner the call addresses
    FILE = "file"  # the single fallback token
    BORROWED = "borrowed"  # another owner's installation, for public repositories


def owner_of(url):
    """The lowercase account a GitHub API URL addresses, or None for any other URL."""
    parsed = urlparse(url)
    match = OWNER_PATH.match(parsed.path) if parsed.scheme == "https" and parsed.netloc == "api.github.com" else None
    return match.group(1).lower() if match else None


def token():
    """The single fallback token (WHEELHOUSE_GITHUB_TOKEN_FILE), or None."""
    token_file = os.environ.get("WHEELHOUSE_GITHUB_TOKEN_FILE")
    if not token_file:
        return None
    value = Path(token_file).read_text().strip()
    require(value and "\n" not in value, "Invalid catalog credential")
    return value


def installation_token(owner, clock=None):
    """A GitHub App installation token from WHEELHOUSE_GITHUB_TOKENS_FILE, which the API writes before each runner call:
    {"owners": {owner: {"token", "expiresAt"}}, "any": {"token", "expiresAt"}}. `owner` picks that owner's own
    installation; None picks "any", an installation token for public repositories of owners without the App. None when
    the file, the entry or an unexpired token is missing; a malformed file counts as missing."""
    tokens_file = os.environ.get("WHEELHOUSE_GITHUB_TOKENS_FILE")
    if not tokens_file:
        return None
    try:
        kept = json.loads(Path(tokens_file).read_text())
        entry = kept.get("owners", {}).get(owner) if owner else kept.get("any")
        value, expires = entry["token"], datetime.fromisoformat(str(entry["expiresAt"]).replace("Z", "+00:00"))
    except (OSError, ValueError, TypeError, KeyError, AttributeError):
        return None
    if expires.tzinfo is None or not isinstance(value, str) or not value or any(c.isspace() for c in value):
        return None
    return value if expires.timestamp() - (clock or time.time)() > TOKEN_MARGIN else None


def access(url):
    """The token a GitHub request carries and where it came from, or (None, None) for an anonymous one. Only
    api.github.com ever receives one: the App's installation on the owner the path names, else the fallback token,
    else another installation's token, which reads public repositories of any owner (GitHub App tokens reach public
    resources without permissions). Package reads take the fallback token only: an installation token cannot read
    packages."""
    owner = owner_of(url)
    if owner is None:
        return None, None
    packages = "/packages/" in urlparse(url).path
    installed = None if packages else installation_token(owner)
    if installed:
        return installed, Credential.INSTALLATION
    fallback = token()
    if fallback:
        return fallback, Credential.FILE
    borrowed = None if packages else installation_token(None)
    return (borrowed, Credential.BORROWED) if borrowed else (None, None)


def credential(url):
    """The token a GitHub request carries, or None for an anonymous one (see `access`)."""
    return access(url)[0]


def rate_limited(status, headers):
    return status == 429 or (headers is not None and (headers.get("X-RateLimit-Remaining") == "0"
                                                      or headers.get("Retry-After") is not None))


def note_access(url, status=None, headers=None, kind=None):
    """Keeps, per owner, what kept a GitHub call from an owner's repositories in WHEELHOUSE_GITHUB_ACCESS_FILE, which the
    console's Issues panel reads: a call refused with 401, 403 or 429, and, while the App is on, a repository that could
    not be seen without its own installation (404). A later success for that owner (for a 404, on that repository)
    clears it. Never stores a body, header or token."""
    path, owner = os.environ.get("WHEELHOUSE_GITHUB_ACCESS_FILE"), owner_of(url)
    parsed = urlparse(url)
    if not path or owner is None or "/packages/" in parsed.path:
        return
    seen = REPOSITORY_READ.fullmatch(parsed.path)
    repository = seen.group(1).lower() if seen else None
    if status == 404 and (repository is None or kind is Credential.INSTALLATION
                          or not os.environ.get("WHEELHOUSE_GITHUB_TOKENS_FILE")):
        return
    try:
        kept = json.loads(Path(path).read_text())
        owners = kept.get("owners") if isinstance(kept, dict) and isinstance(kept.get("owners"), dict) else {}
    except (OSError, ValueError):
        owners = {}
    if status is None:
        noted = owners.get(owner)
        if not isinstance(noted, dict) or (noted.get("repository") and noted.get("repository") != repository):
            return
        owners.pop(owner)
    else:
        owners[owner] = {"status": status, "authenticated": kind is not None,
                         "installed": kind is Credential.INSTALLATION,
                         "rateLimited": rate_limited(status, headers), "at": datetime.now(timezone.utc).isoformat()}
        if status == 404:
            owners[owner]["repository"] = repository
    try:
        write_json(path, {"owners": owners})
    except OSError:
        pass  # Visibility only; a failed note never fails the call.


def fetch(url, limit=MAX_ARCHIVE, accept=None):
    bearer, kind = access(url)
    try:
        return _fetch(url, limit, accept, bearer)
    except HTTPError as error:
        if kind is Credential.BORROWED and error.code in (401, 403) and not rate_limited(error.code, error.headers):
            # Should GitHub refuse another installation's token for a public repository, the call goes anonymous.
            bearer, kind = None, None
            try:
                return _fetch(url, limit, accept, None)
            except HTTPError as anonymous:
                error = anonymous
        # Rate limits surface as 403/429. Only the status is reported, never the body or headers.
        if error.code in ACCESS_STATUSES + (404,):
            note_access(url, error.code, error.headers, kind)
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None


def _fetch(url, limit, accept, bearer):
    headers = {"User-Agent": "Wheelhouse-release-catalog", "X-GitHub-Api-Version": "2022-11-28"}
    if accept:
        headers["Accept"] = accept
    if bearer:
        headers["Authorization"] = "Bearer " + bearer
    request = Request(url, headers=headers)
    try:
        with build_opener(ApiRedirect()).open(request, timeout=15) as response:
            require(response.url.startswith("https://"), "Insecure release response")
            data = response.read(limit + 1)
    except (URLError, TimeoutError) as error:
        if isinstance(error, HTTPError):
            raise
        raise CommandFailed("GitHub request failed (network)") from None
    require(len(data) <= limit, "Release response exceeded its size limit")
    note_access(url)
    return data


def post(url, body):
    """A GitHub API write. Starting a build needs Actions write on the product repository: the GitHub App installed on
    its owner, or the fallback token; another owner's installation cannot write here."""
    bearer, kind = access(url)
    require(bearer and kind is not Credential.BORROWED,
            "Starting a build needs the GitHub App installed on the repository's owner, or "
            "WHEELHOUSE_GITHUB_TOKEN_FILE with Actions write access")
    request = Request(url, data=json.dumps(body).encode(), method="POST",
                      headers={"User-Agent": "Wheelhouse-release-catalog", "X-GitHub-Api-Version": "2022-11-28",
                               "Accept": "application/vnd.github+json", "Content-Type": "application/json",
                               "Authorization": "Bearer " + bearer})
    try:
        with build_opener(ApiRedirect()).open(request, timeout=15) as response:
            require(response.status in (200, 201, 202, 204), "GitHub refused the build request")
    except HTTPError as error:
        if error.code in ACCESS_STATUSES:
            note_access(url, error.code, error.headers, kind)
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None
    except (URLError, TimeoutError):
        raise CommandFailed("GitHub request failed (network)") from None
    note_access(url)


def release_asset(repository, tag, name, asset_id):
    """A release's archive. With a credential it comes through the API's asset endpoint, which serves private
    repositories too; anonymous, through the public download URL. Both redirect to GitHub's storage host, which never
    receives the credential."""
    api = API + repository + "/releases/assets/" + str(int(asset_id))
    if credential(api):
        return fetch(api, accept="application/octet-stream")
    return fetch("https://github.com/" + repository + "/releases/download/" + quote(tag, safe="") + "/" + name)


def source_of(product):
    source = next((item for item in sources() if item.product == product), None)
    require(source is not None, "Product has no approved release source")
    require(source.component is None, "A product built from several repositories deploys release sets")
    return source


def candidates(source):
    """Unexpired `bundle-<sha>` artifacts: one deployable build per commit, newest first."""
    if not source.workflow:
        return []
    listing = json.loads(fetch(API + source.repository + "/actions/artifacts?per_page=100"))
    result, seen = [], set()
    for artifact in listing.get("artifacts", []) if isinstance(listing, dict) else []:
        match = CANDIDATE.fullmatch(str(artifact.get("name", "")))
        if (not match or artifact.get("expired") is not False or type(artifact.get("id")) is not int
                or artifact["id"] <= 0 or not 0 < artifact.get("size_in_bytes", 0) <= MAX_ARCHIVE
                or match.group(1) in seen):
            continue
        commit = match.group(1)
        seen.add(commit)
        run = artifact.get("workflow_run") if isinstance(artifact.get("workflow_run"), dict) else {}
        branch = run.get("head_branch")
        result.append({"id": source.product + "-ci-" + str(artifact["id"]), "product": source.product,
                       "release": "sha-" + commit[:7], "kind": "candidate", "commit": commit,
                       "branch": branch if isinstance(branch, str) and BRANCH.fullmatch(branch) else None,
                       "repository": source.repository, "provider": ArtifactProvider.GITHUB_ACTIONS.value,
                       "publishedAt": artifact.get("created_at"), "expiresAt": artifact.get("expires_at"),
                       "prerelease": True})
    return result


def published(source, per_page=100):
    """A source's published releases with a complete, checksummed asset, as GitHub lists them (newest first)."""
    require(source.provider is ArtifactProvider.GITHUB_RELEASES, "Unsupported artifact provider")
    result = []
    for release in json.loads(fetch(API + source.repository + "/releases?per_page=" + str(per_page))):
        tag = release.get("tag_name", "")
        if release.get("draft") or not release.get("published_at") or not VERSION.fullmatch(tag):
            continue
        assets = [asset for asset in release.get("assets", []) if asset.get("name") == source.asset_name]
        if len(assets) != 1:
            continue
        asset = assets[0]
        if (asset.get("state") != "uploaded" or not 0 < asset.get("size", 0) <= MAX_ARCHIVE
                or not re.fullmatch(r"sha256:[a-f0-9]{64}", asset.get("digest") or "")
                or type(asset.get("id")) is not int or asset["id"] <= 0):
            continue
        result.append({"id": source.product + "-gh-" + str(asset["id"]), "product": source.product,
                       "release": tag, "kind": "release", "repository": source.repository,
                       "provider": source.provider.value, "publishedAt": release["published_at"],
                       "prerelease": bool(release.get("prerelease")),
                       "assetDigest": asset["digest"], "assetName": source.asset_name})
    return result


def latest():
    """Each product's newest published release (a pre-release never counts), for the release watcher: one small GitHub
    read per repository. A product of several repositories names its newest release set, made of each repository's
    newest release. A source GitHub cannot serve says so and names no release."""
    result = []
    for group in grouped():
        try:
            if several(group):
                newest = newest_set(group)
            else:
                releases = [item for item in published(group[0], per_page=10) if not item["prerelease"]]
                newest = max(releases, key=lambda item: item["publishedAt"], default=None)
        except (CommandFailed, ValueError) as error:
            result.append({"product": group[0].product, "ok": False,
                           "reason": str(error)[:300] if isinstance(error, CommandFailed) else "Unreadable releases"})
            continue
        result.append({"product": group[0].product, "ok": True,
                       "release": None if newest is None else {key: newest[key] for key in
                                                               ("id", "release", "publishedAt")}})
    return result


# How many of a product's newest release sets the catalog lists, and how many commit builds per repository make
# candidate sets.
SETS_LISTED = 50
CANDIDATE_SETS = 10


def not_yet(product, names):
    """The product's repositories a set does not hold yet, in repository order: each that has not released, or whose
    deploy.yml names no service yet. `names` are the short names of the repositories in the set."""
    found = next((item for item in catalog.products() if item.slug == product), None)
    repositories = [repository for repository, _ in found.repositories()] if found is not None else []
    return [{"name": catalog.short_name(product, repository), "repository": repository} for repository in repositories
            if catalog.short_name(product, repository) not in names]


def set_item(group, parts):
    """The catalog entry of one release set: `parts` hold one artifact of each repository, in repository order, None
    for a repository that has not released. The set, its ID and its label cover the repositories that have; the
    primary repository serves the front door, so it must be one of them."""
    require(parts and parts[0] is not None, group[0].repository + " serves the product's front door and has no release "
            "yet; the product's release sets start with its first release")
    product = group[0].product
    names = [source.component for source, part in zip(group, parts) if part is not None]
    parts = [part for part in parts if part is not None]
    set_id = release_sets.identifier(product, [part["id"] for part in parts])
    kind = "candidate" if any(part["kind"] == "candidate" for part in parts) else "release"
    published_at = max((str(part["publishedAt"]) for part in parts if part.get("publishedAt")), default=None)
    item = {"id": set_id, "product": product,
            "release": release_sets.label(set_id, [(name, part["release"]) for name, part in zip(names, parts)]),
            "kind": kind, "repository": group[0].repository, "provider": ArtifactProvider.RELEASE_SET.value,
            "publishedAt": published_at, "prerelease": kind == "candidate" or any(part.get("prerelease") for part in parts),
            "components": [{"name": name, **part} for name, part in zip(names, parts)],
            "notYet": not_yet(product, names)}
    build = next((part for part in parts if part["kind"] == "candidate"), None)
    if build is not None:
        item.update(commit=build["commit"], branch=build.get("branch"), expiresAt=build.get("expiresAt"))
    return item


def component_releases(group, per_page=100):
    """Each repository's published releases that a set may take (no pre-release), in repository order."""
    return [[item for item in published(source, per_page) if not item["prerelease"]] for source in group]


def newest_set(group):
    """The set of each released repository's newest published release, or None while the primary repository has none."""
    newest = [max(items, key=lambda item: item["publishedAt"], default=None)
              for items in component_releases(group, per_page=10)]
    return set_item(group, newest) if newest[0] is not None else None


def sets_of(group):
    """A product's release sets, newest first: one after each repository release once the primary repository has
    released, holding every other repository's newest release then and leaving out each that has none yet; then one
    candidate set per recent commit build of a repository, beside every other repository's newest release. None while
    the primary repository has no release."""
    listings = component_releases(group)
    if not listings[0]:
        return []
    events = sorted(((item["publishedAt"], index, item) for index, items in enumerate(listings) for item in items),
                    key=lambda event: (event[0], event[1]))
    newest, result = [None] * len(group), []
    for _, index, item in events:
        newest[index] = item
        if newest[0] is not None:
            result.append(set_item(group, list(newest)))
    result = list(reversed(result))[:SETS_LISTED]
    for index, source in enumerate(group):
        try:
            builds = candidates(source)[:CANDIDATE_SETS]
        except (CommandFailed, ValueError):
            continue  # commit builds are a convenience for dev; the release sets stay usable
        for build in builds:
            parts = list(newest)
            parts[index] = build
            result.append(set_item(group, parts))
    return result


NOTES_LIMIT = 20000
MIGRATION_PATH = re.compile(r"(^|/)(Migrations|migrations)/")


def changelog(product, start, end, root=None):
    """What changed between two releases of a product: the bodies of the releases published after `start` up to `end`,
    newest first, and the compare commit list as a fallback for bodies that say little, with the changed files that
    look like schema migrations. `start` is what the target runs now, or None when it runs nothing. Bodies are
    returned as plain text for the console to show, never rendered. A product of several repositories compares two
    release sets repository by repository (`components`); the merged lists name each release and commit's repository."""
    group = product_sources(product)
    if several(group):
        return set_changelog(product, group, start, end, root)
    return source_changelog(product, source_of(product), start, end)


def set_parts(product, text, root):
    """A release set label's releases by repository short name; {} for no label."""
    if text is None:
        return {}
    parts = release_sets.label_parts(text)
    if parts is None and root is not None:
        remembered = release_sets.recall_label(root, product, text)
        parts = {item["name"]: item["release"] for item in remembered["components"]} if remembered else None
    require(parts is not None, "A changelog runs between two release sets of the product")
    return parts


def set_changelog(product, group, start, end, root):
    """A repository the set to promote leaves out (it has not released yet) has no part in the changelog and is named
    in `notYet`; one that joins it lists its releases up to the one it brings."""
    require(end is not None, "A changelog needs the release to promote")
    before, after = set_parts(product, start, root), set_parts(product, end, root)
    components = []
    for source in group:
        to, since = after.get(source.component), before.get(source.component)
        if to is None or to == since or not VERSION.fullmatch(to):
            continue  # not in the set yet, unchanged, or a commit build, which prod never takes
        one = source_changelog(product, source, since if since and VERSION.fullmatch(since) else None, to)
        components.append({"name": source.component, "repository": source.repository, **one})
    releases = sorted(({**item, "name": component["name"] + " " + item["name"], "repository": component["repository"]}
                       for component in components for item in component["releases"]),
                      key=lambda item: item["publishedAt"], reverse=True)
    compared = [component for component in components if component["commits"] is not None]
    return {"product": product, "from": start, "to": end, "releases": releases,
            "commits": [{**commit, "message": component["name"] + ": " + commit["message"]}
                        for component in compared for commit in component["commits"]][:100] if compared else None,
            "totalCommits": sum(component["totalCommits"] or 0 for component in compared) if compared else None,
            "migrations": sorted(component["name"] + "/" + path for component in components
                                 for path in component["migrations"])[:50],
            "compareUrl": compared[0]["compareUrl"] if len(compared) == 1 else None,
            "reason": next((component["reason"] for component in components if component["reason"]), None),
            "components": components, "notYet": not_yet(product, set(after))}


def source_changelog(product, source, start, end):
    for label in (start, end):
        require(label is None or VERSION.fullmatch(label), "A changelog runs between two release tags")
    require(end is not None, "A changelog needs the release to promote")
    listing = json.loads(fetch(API + source.repository + "/releases?per_page=100"))
    published = sorted((item for item in listing if isinstance(item, dict) and not item.get("draft")
                        and item.get("published_at") and VERSION.fullmatch(str(item.get("tag_name", "")))),
                       key=lambda item: item["published_at"])
    tags = [item["tag_name"] for item in published]
    require(end in tags, "The release to promote is not published")
    first = tags.index(start) + 1 if start in tags else max(0, tags.index(end) - 9)
    chosen = published[first:tags.index(end) + 1]
    releases = []
    for item in reversed(chosen):
        body = item.get("body") if isinstance(item.get("body"), str) else ""
        releases.append({"release": item["tag_name"], "name": str(item.get("name") or item["tag_name"])[:200],
                         "publishedAt": item["published_at"], "body": body[:NOTES_LIMIT],
                         "truncated": len(body) > NOTES_LIMIT, "prerelease": bool(item.get("prerelease")),
                         "url": "https://github.com/" + source.repository + "/releases/tag/" + quote(item["tag_name"], safe="")})
    result = {"product": product, "from": start, "to": end, "releases": releases, "commits": None,
              "totalCommits": None, "migrations": [], "compareUrl": None, "reason": None}
    if start is None or start not in tags:
        return result
    result["compareUrl"] = ("https://github.com/" + source.repository + "/compare/" + quote(start, safe="") + "..."
                            + quote(end, safe=""))
    try:
        compare = json.loads(fetch(API + source.repository + "/compare/" + quote(start, safe="") + "..."
                                   + quote(end, safe=""), limit=8 * 1024 * 1024))
    except CommandFailed as error:
        result["reason"] = str(error)[:300]
        return result
    commits = []
    for item in reversed((compare.get("commits") or []) if isinstance(compare, dict) else []):
        sha = item.get("sha") if isinstance(item, dict) else None
        if not isinstance(sha, str) or not SHA.fullmatch(sha):
            continue
        detail = item.get("commit") if isinstance(item.get("commit"), dict) else {}
        author = detail.get("author") if isinstance(detail.get("author"), dict) else {}
        message = detail.get("message") if isinstance(detail.get("message"), str) else ""
        commits.append({"sha": sha, "message": message.splitlines()[0][:200] if message else "",
                        "author": str(author.get("name") or "")[:80], "date": author.get("date"),
                        "url": "https://github.com/" + source.repository + "/commit/" + sha})
    result["commits"] = commits[:100]
    total = compare.get("total_commits") if isinstance(compare, dict) else None
    result["totalCommits"] = total if type(total) is int else len(commits)
    files = compare.get("files") if isinstance(compare, dict) else None
    result["migrations"] = sorted({str(item["filename"])[:300] for item in files or []
                                   if isinstance(item, dict) and isinstance(item.get("filename"), str)
                                   and MIGRATION_PATH.search(item["filename"])})[:50]
    return result


BUILDS_SECONDS = 120
FAILED_RUNS = ("failure", "timed_out", "startup_failure")


def failed_build(source):
    """The newest finished run of a product's build workflow on its default branch when it failed, else None; a later
    success clears it."""
    listing = json.loads(fetch(API + source.repository + "/actions/workflows/" + quote(source.workflow, safe="")
                               + "/runs?per_page=10&exclude_pull_requests=true&branch=" + quote(source.default_branch, safe="")))
    runs = [run for run in (listing.get("workflow_runs") or [] if isinstance(listing, dict) else [])
            if isinstance(run, dict) and run.get("status") == "completed" and type(run.get("id")) is int]
    latest = max(runs, key=lambda run: str(run.get("created_at") or ""), default=None)
    if latest is None or latest.get("conclusion") not in FAILED_RUNS:
        return None
    sha = latest.get("head_sha")
    return {"runId": latest["id"], "conclusion": latest["conclusion"],
            "commit": sha if isinstance(sha, str) and SHA.fullmatch(sha) else None,
            "title": str(latest.get("display_title") or "")[:200],
            "at": latest.get("updated_at") or latest.get("created_at"),
            # Built from the code-owned repository and the run's numeric id, never taken from the response.
            "url": "https://github.com/" + source.repository + "/actions/runs/" + str(latest["id"])}


def builds(root, clock=None):
    """Each product's failing main build, read from GitHub at most every two minutes and kept in
    <root>/github/builds.json between reads; a product GitHub cannot serve says so."""
    now = (clock or time.time)()
    cache = Path(root) / "github" / "builds.json"
    try:
        kept = json.loads(cache.read_text())
        if isinstance(kept, dict) and now - float(kept.get("fetchedAt", 0)) < BUILDS_SECONDS:
            return kept["products"]
    except (OSError, ValueError, KeyError, TypeError):
        pass
    result = []
    for group in grouped():
        entries = []
        for source in group:
            if not source.workflow:
                continue
            try:
                failing = failed_build(source)
                if failing is not None and source.component is not None:
                    failing = {**failing, "repository": source.repository,
                               "title": (source.component + ": " + failing["title"])[:200]}
                entries.append({"product": source.product, "ok": True, "failing": failing})
            except (CommandFailed, ValueError) as error:
                entries.append({"product": source.product, "ok": False, "failing": None,
                                "reason": str(error)[:300] if isinstance(error, CommandFailed) else "Unreadable runs"})
        if len(entries) <= 1:
            result += entries
            continue
        # A product of several repositories: its newest failing build, else a repository GitHub could not serve.
        failing = [entry["failing"] for entry in entries if entry["failing"]]
        if failing:
            result.append({"product": group[0].product, "ok": True,
                           "failing": max(failing, key=lambda item: str(item.get("at") or ""))})
        else:
            result.append(next((entry for entry in entries if not entry["ok"]), entries[0]))
    write_json(cache, {"fetchedAt": now, "products": result})
    return result


USAGE_SECONDS = 60 * 60
USAGE_DAYS = 30
USAGE_PAGES = 3


def usage(root, product, clock=None):
    """What a product's builds cost over the last 30 days: its GitHub Actions runs and their wall-clock minutes (a
    public repository's minutes are free; GitHub reports billable minutes only per run), and how many versions each
    service's GHCR package holds when the token may read packages. GitHub's API reports no container package size, so
    storage is versions, never bytes. Read at most once an hour; kept in <root>/github/usage-<product>.json."""
    now = (clock or time.time)()
    require(isinstance(product, str) and SLUG.fullmatch(product), "Invalid product")
    cache = Path(root) / "github" / ("usage-" + product + ".json")
    try:
        kept = json.loads(cache.read_text())
        if isinstance(kept, dict) and now - float(kept.get("fetchedAt", 0)) < USAGE_SECONDS:
            return kept["usage"]
    except (OSError, ValueError, KeyError, TypeError):
        pass
    found = catalog.product(product)
    repositories = [repository for repository, _ in found.repositories()]
    since = datetime.fromtimestamp(now - USAGE_DAYS * 24 * 60 * 60, timezone.utc).date().isoformat()
    runs, seconds, workflows, complete = 0, 0.0, {}, True
    for repository in repositories:
        # A product of several repositories counts each repository's runs; workflow names carry the repository's.
        prefix = catalog.short_name(product, repository) + ": " if found.components else ""
        counted = 0
        for page in range(1, USAGE_PAGES + 1):
            listing = json.loads(fetch(API + repository + "/actions/runs?per_page=100&page=" + str(page)
                                       + "&created=%3E%3D" + since))
            items = listing.get("workflow_runs") if isinstance(listing, dict) else None
            for run in items or []:
                if not isinstance(run, dict):
                    continue
                runs += 1
                counted += 1
                started, ended = parse_time(run.get("run_started_at")), parse_time(run.get("updated_at"))
                if run.get("status") == "completed" and started and ended and ended >= started:
                    seconds += (ended - started).total_seconds()
                    name = (prefix + str(run.get("name") or "workflow"))[:100]
                    workflows[name] = workflows.get(name, 0.0) + (ended - started).total_seconds()
            if not items or len(items) < 100:
                break
        complete = complete and counted < USAGE_PAGES * 100
    result = {"product": product, "repository": repositories[0], "repositories": repositories, "since": since,
              "days": USAGE_DAYS,
              "actions": {"runs": runs, "minutes": round(seconds / 60, 1),
                          "workflows": [{"name": name, "minutes": round(value / 60, 1)}
                                        for name, value in sorted(workflows.items(), key=lambda item: -item[1])],
                          "complete": complete},
              "packages": packages(product), "storageBytes": None,
              "storageNote": "GitHub's API does not report container package sizes"}
    write_json(cache, {"fetchedAt": now, "usage": result})
    return result


def parse_time(value):
    try:
        moment = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    except ValueError:
        return None
    return moment if moment.tzinfo else moment.replace(tzinfo=timezone.utc)


def packages(product):
    """Each service image's GHCR package and how many versions it holds; null where the token may not read packages."""
    result = []
    for service, image in (image for source in product_sources(product) for image in source.images):
        parts = image.split("/")
        if len(parts) < 3 or parts[0] != "ghcr.io":
            continue
        owner, name = parts[1], "/".join(parts[2:])
        versions = None
        for kind in ("orgs", "users"):
            try:
                listing = json.loads(fetch("https://api.github.com/" + kind + "/" + quote(owner, safe="")
                                           + "/packages/container/" + quote(name, safe="") + "/versions?per_page=100"))
                versions = len(listing) if isinstance(listing, list) else None
                break
            except CommandFailed:
                continue
        result.append({"service": service, "package": name, "versions": versions})
    return result


def available(root=None):
    result = []
    for group in grouped():
        if several(group):
            try:
                found = sets_of(group)
            except CommandFailed:
                if rehearsal():
                    continue
                raise
            # Each listed set is remembered, so one that leaves the listing still deploys (a promotion of an older set).
            for item in found if root is not None else ():
                release_sets.remember(root, item)
            result += found
            continue
        source = group[0]
        # A bounded recent catalog; old deployed bundles remain in the target recovery journal.
        try:
            result += published(source)
        except CommandFailed:
            if rehearsal():
                continue  # An offline local server still lists its imported bundles.
            raise
        # Candidates are a convenience for dev; an unreachable listing leaves the releases usable.
        try:
            result += candidates(source)
        except (CommandFailed, ValueError):
            pass
    if rehearsal() and root is not None:
        result += imported(root)
    return result


def imported(root):
    """Operator-imported bundles. Listed only on the local server because they carry no published source."""
    result = []
    bundles = Path(root) / "bundles"
    for manifest_path in sorted(bundles.glob("*/release.json")) if bundles.is_dir() else ():
        bundle = manifest_path.parent
        if (bundle / "source.json").exists() or not SLUG.fullmatch(bundle.name):
            continue
        try:
            manifest = validate_bundle(bundle)
        except ValueError:
            continue
        published = datetime.fromtimestamp(manifest_path.stat().st_mtime, timezone.utc).isoformat()
        result.append({"id": bundle.name, "product": manifest["product"], "release": manifest["release"],
                       "kind": manifest.get("kind", "release"), "commit": manifest["sourceCommit"],
                       "branch": manifest.get("branch"), "provider": ArtifactProvider.LOCAL_IMPORT.value,
                       "publishedAt": published, "prerelease": True})
    return result


def candidate_archive(payload):
    """Repacks a candidate artifact's zip, which must hold exactly release.json and compose.json, for import."""
    try:
        package = zipfile.ZipFile(io.BytesIO(payload))
    except zipfile.BadZipFile:
        raise CommandFailed("Candidate artifact is not a zip archive") from None
    with package:
        members = package.infolist()
        require(len(members) == 2 and {member.filename for member in members} == {"release.json", "compose.json"},
                "Candidate artifact must contain only release.json and compose.json")
        require(all(member.file_size <= 1024 * 1024 for member in members), "Invalid candidate artifact member")
        output = io.BytesIO()
        with tarfile.open(fileobj=output, mode="w:gz") as archive:
            for member in members:
                data = package.read(member)
                entry = tarfile.TarInfo(member.filename)
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
    return output.getvalue()


def prepare(root, identifier, importer):
    if rehearsal() and any(item["id"] == identifier for item in imported(root)):
        return Path(root) / "bundles" / identifier
    if release_sets.is_set(identifier):
        return prepare_set(root, identifier, importer)
    artifact = next((item for item in available() if item["id"] == identifier), None)
    require(artifact is not None, "Release artifact is no longer available")
    return prepare_artifact(root, artifact, source_of(artifact["product"]), importer)


def prepare_set(root, identifier, importer):
    """A release set's bundle: each repository's release prepared and checked against that repository's source, then
    composed (release_sets.compose). A composed set is cached like any bundle, with the set as its receipt."""
    bundle = Path(root) / "bundles" / identifier
    require(SLUG.fullmatch(identifier) and not bundle.is_symlink(), "Release cache cannot be a symlink")
    if (bundle / "release.json").is_file() and (bundle / "source.json").is_file():
        manifest = validate_bundle(bundle)
        require(manifest.get("set") == identifier, "Release receipt changed")
        return bundle
    item = release_sets.recall(root, identifier) or next(
        (entry for entry in available(root) if entry["id"] == identifier), None)
    require(item is not None, "Release set is no longer available")
    group = product_sources(item["product"])
    components = item.get("components") if isinstance(item.get("components"), list) else []
    # A set holds the repositories that had released, in repository order and always the primary one; the rest join a
    # later set when they release.
    order = {source.repository: index for index, source in enumerate(group)}
    places = [order.get(part.get("repository")) if isinstance(part, dict) else None for part in components]
    require(several(group) and places and places[0] == 0 and None not in places and places == sorted(set(places)),
            "The product's repositories changed since this release set; deploy a newer one")
    parts = []
    for component in components:
        source = group[order[component["repository"]]]
        artifact = {key: value for key, value in component.items() if key != "name"}
        folder = prepare_artifact(root, artifact, source, importer)
        parts.append((source.component, source.repository, read_json(folder / "release.json"),
                      read_json(folder / "compose.json")))
    manifest, compose_bytes = release_sets.compose(item["product"], identifier, parts)
    require(manifest["release"] == item["release"], "Release set label mismatch")
    with tempfile.TemporaryDirectory() as directory:
        staged = Path(directory) / "set"
        release_sets.write(staged, manifest, compose_bytes)
        validate_bundle(staged)
    release_sets.write(bundle, manifest, compose_bytes)
    write_json(bundle / "source.json", item)
    return bundle


def prepare_artifact(root, artifact, source, importer):
    """One repository's release or commit build in the local cache, downloaded and checked once."""
    identifier = artifact["id"]
    require(isinstance(identifier, str) and SLUG.fullmatch(identifier), "Invalid release artifact")
    bundle = Path(root) / "bundles" / identifier
    require(not bundle.is_symlink(), "Release cache cannot be a symlink")
    if not bundle.exists():
        if artifact["kind"] == "candidate":
            archive_bytes = candidate_archive(fetch(API + source.repository + "/actions/artifacts/"
                                                    + identifier.rsplit("-", 1)[1] + "/zip"))
        else:
            archive_bytes = release_asset(source.repository, artifact["release"], source.asset_name,
                                          identifier.rsplit("-", 1)[1])
            require("sha256:" + hashlib.sha256(archive_bytes).hexdigest() == artifact["assetDigest"], "Release asset changed")
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "release.tar.gz"
            archive.write_bytes(archive_bytes)
            # Validate the entire archive before publishing it to the durable cache.
            importer(Path(directory), archive, "candidate")
            validate_source(Path(directory) / "bundles/candidate", source, artifact)
            importer(root, archive, identifier)
        write_json(bundle / "source.json", artifact)
    else:
        receipt = json.loads((bundle / "source.json").read_text())
        require(receipt.get("assetDigest") == artifact.get("assetDigest")
                and receipt.get("commit") == artifact.get("commit"), "Release receipt changed")
    validate_source(bundle, source, artifact)
    return bundle


def own_image(repository, service):
    """The image the shared publish workflow pushes a repository's service to: the repository's own GHCR namespace."""
    return "ghcr.io/" + repository.lower() + "/" + service


def validate_source(bundle, source, artifact):
    manifest = validate_bundle(bundle)
    # Every repository of a product built from several names the product's slug in its deploy.yml (decision 70).
    require(source.component is None or manifest["product"] == source.product,
            source.repository + "'s release names product " + str(manifest["product"]) + "; its deploy.yml must name "
            + source.product)
    require(manifest["product"] == source.product and manifest["release"] == artifact["release"], "Release identity mismatch")
    # A release's services are the ones its own deploy.yml named: a newer release may add one the catalog has not
    # approved yet, and an older one, or one that removed a service, lacks some it approved. Each image must still be
    # the service's own in the repository's GHCR namespace, or the one the catalog approved for that service.
    approved = dict(source.images)
    for service, image in manifest["images"].items():
        require(image.split("@")[0] in (approved.get(service), own_image(source.repository, service)),
                "Image is outside the approved repository")
    if artifact.get("kind") == "candidate":
        # The artifact's name records the commit the workflow built; the bundle must agree.
        require(manifest.get("kind") == "candidate" and manifest["sourceCommit"] == artifact["commit"],
                "Candidate source commit mismatch")
        return manifest
    commit = json.loads(fetch("https://api.github.com/repos/" + source.repository + "/commits/"
                              + quote(artifact["release"], safe="")))
    require(commit.get("sha") == manifest["sourceCommit"], "Release source commit mismatch")
    return manifest


def request_build(product, commit, repository=None):
    """Starts the build workflow of one of the product's repositories (its primary one unless named) for a commit that
    has no build yet."""
    source = repository_source(product, repository)
    require(source.workflow and WORKFLOW.fullmatch(source.workflow), "This product has no build workflow")
    require(isinstance(commit, str) and SHA.fullmatch(commit), "Give the full commit SHA")
    require(not any(item["commit"] == commit for item in candidates(source)), "A build of this commit already exists")
    found = json.loads(fetch(API + source.repository + "/commits/" + commit))
    require(isinstance(found, dict) and found.get("sha") == commit, "The commit is not in the product repository")
    post(API + source.repository + "/actions/workflows/" + quote(source.workflow, safe="") + "/dispatches",
         {"ref": source.default_branch, "inputs": {"commit": commit}})
    return {"product": product, "commit": commit, "status": "requested"}


def product_repository(product, repository=None):
    """One of a catalog product's repositories: the named one, else its primary one."""
    found = catalog.product(product)
    if repository is None:
        return found.repository
    match = next((name for name, _ in found.repositories() if name.lower() == str(repository).lower()), None)
    require(match is not None, "The repository is not one of the product's")
    return match


def branches(product, repository=None):
    # Any catalog product lists its branches; a release source is needed only to build or deploy them.
    repository = product_repository(product, repository)
    listing = json.loads(fetch(API + repository + "/branches?per_page=100"))
    return sorted(item["name"] for item in listing if isinstance(item, dict)
                  and isinstance(item.get("name"), str) and BRANCH.fullmatch(item["name"]))


def commits(product, branch, repository=None):
    """A branch's recent commits, each with its build when one exists. In a product of several repositories, a build
    is the candidate set that takes it beside every other repository's newest release."""
    repository = product_repository(product, repository)
    group = product_sources(product)
    source = next((item for item in group if item.repository == repository), None)
    require(isinstance(branch, str) and BRANCH.fullmatch(branch), "Invalid branch")
    listing = json.loads(fetch(API + repository + "/commits?per_page=30&sha=" + quote(branch, safe="")))
    if source is not None and several(group):
        built = {item["commit"]: item["id"] for item in sets_of(group) if item["kind"] == "candidate"
                 and any(part["repository"] == repository and part["kind"] == "candidate" for part in item["components"])}
    else:
        built = {item["commit"]: item["id"] for item in candidates(source)} if source else {}
    result = []
    for item in listing if isinstance(listing, list) else []:
        sha = item.get("sha") if isinstance(item, dict) else None
        if not isinstance(sha, str) or not SHA.fullmatch(sha):
            continue
        detail = item.get("commit") if isinstance(item.get("commit"), dict) else {}
        author = detail.get("author") if isinstance(detail.get("author"), dict) else {}
        message = detail.get("message") if isinstance(detail.get("message"), str) else ""
        # The permalink is built from the code-owned repository and the validated SHA, never taken from the response.
        result.append({"sha": sha, "message": message.splitlines()[0][:120] if message else "",
                       "author": str(author.get("name") or "")[:80], "date": author.get("date"),
                       "buildId": built.get(sha), "canBuild": source is not None and source.workflow is not None,
                       "url": "https://github.com/" + repository + "/commit/" + sha})
    return result
