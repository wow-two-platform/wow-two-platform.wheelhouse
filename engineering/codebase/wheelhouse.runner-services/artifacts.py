"""Discovery of completed release assets and per-commit candidate builds from the inventory's repositories."""
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
from runner import SHA, SLUG, CommandFailed, require, validate_bundle, write_json


class ArtifactProvider(str, Enum):
    GITHUB_RELEASES = "GitHubReleases"
    GITHUB_ACTIONS = "GitHubActions"
    LOCAL_IMPORT = "LocalImport"


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


def sources():
    """Every release source comes from the product inventory; a product without one deploys hand-imported bundles."""
    return tuple(Source(product.slug, product.repository, product.release.asset, product.release.images,
                        workflow=product.release.workflow, default_branch=product.default_branch)
                 for product in catalog.products() if product.release is not None)


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
# How long before its expiry an installation token stops being used; the API refreshes it well before.
TOKEN_MARGIN = 60
# The statuses that say a credential is missing, refused or out of rate limit; each is noted for the Issues panel.
ACCESS_STATUSES = (401, 403, 429)


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
    """The GitHub App installation token for an owner from WHEELHOUSE_GITHUB_TOKENS_FILE, which the API writes before
    each runner call ({"owners": {owner: {"token", "expiresAt"}}}); None when the file, the owner or an unexpired token
    is missing. A malformed file counts as missing: the fallback token still serves."""
    tokens_file = os.environ.get("WHEELHOUSE_GITHUB_TOKENS_FILE")
    if not tokens_file or not owner:
        return None
    try:
        entry = json.loads(Path(tokens_file).read_text()).get("owners", {}).get(owner)
        value, expires = entry["token"], datetime.fromisoformat(str(entry["expiresAt"]).replace("Z", "+00:00"))
    except (OSError, ValueError, TypeError, KeyError, AttributeError):
        return None
    if expires.tzinfo is None or not isinstance(value, str) or not value or any(c.isspace() for c in value):
        return None
    return value if expires.timestamp() - (clock or time.time)() > TOKEN_MARGIN else None


def credential(url):
    """The token a GitHub request carries, or None for an anonymous one. Only api.github.com ever receives one: the App
    installation token for the owner the path names, else the single fallback token. Package reads take the fallback
    token only, because an installation token cannot read packages."""
    owner = owner_of(url)
    if owner is None:
        return None
    if "/packages/" not in urlparse(url).path:
        installed = installation_token(owner)
        if installed:
            return installed
    return token()


def note_access(url, status=None, headers=None, authenticated=False):
    """Keeps, per owner, the last GitHub call refused with 401, 403 or 429 in WHEELHOUSE_GITHUB_ACCESS_FILE, which the
    console's Issues panel reads; a later success for that owner clears it. Never stores a body, header or token."""
    path, owner = os.environ.get("WHEELHOUSE_GITHUB_ACCESS_FILE"), owner_of(url)
    if not path or owner is None or "/packages/" in urlparse(url).path:
        return
    try:
        kept = json.loads(Path(path).read_text())
        owners = kept.get("owners") if isinstance(kept, dict) and isinstance(kept.get("owners"), dict) else {}
    except (OSError, ValueError):
        owners = {}
    if status is None:
        if owner not in owners:
            return
        owners.pop(owner)
    else:
        remaining = headers.get("X-RateLimit-Remaining") if headers is not None else None
        owners[owner] = {"status": status, "authenticated": authenticated,
                         "rateLimited": status == 429 or remaining == "0"
                         or (headers is not None and headers.get("Retry-After") is not None),
                         "at": datetime.now(timezone.utc).isoformat()}
    try:
        write_json(path, {"owners": owners})
    except OSError:
        pass  # Visibility only; a failed note never fails the call.


def fetch(url, limit=MAX_ARCHIVE, accept=None):
    headers = {"User-Agent": "Wheelhouse-release-catalog", "X-GitHub-Api-Version": "2022-11-28"}
    if accept:
        headers["Accept"] = accept
    bearer = credential(url)
    if bearer:
        headers["Authorization"] = "Bearer " + bearer
    request = Request(url, headers=headers)
    try:
        with build_opener(ApiRedirect()).open(request, timeout=15) as response:
            require(response.url.startswith("https://"), "Insecure release response")
            data = response.read(limit + 1)
    except HTTPError as error:
        # Rate limits surface as 403/429. Only the status is reported, never the body or headers.
        if error.code in ACCESS_STATUSES:
            note_access(url, error.code, error.headers, bool(bearer))
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None
    except (URLError, TimeoutError):
        raise CommandFailed("GitHub request failed (network)") from None
    require(len(data) <= limit, "Release response exceeded its size limit")
    note_access(url)
    return data


def post(url, body):
    """A GitHub API write. Starting a build needs Actions write on the product repository: the GitHub App installed on
    its owner, or the fallback token."""
    bearer = credential(url)
    require(bearer, "Starting a build needs the GitHub App installed on the repository's owner, or "
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
            note_access(url, error.code, error.headers, True)
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None
    except (URLError, TimeoutError):
        raise CommandFailed("GitHub request failed (network)") from None
    note_access(url)


def release_asset(repository, tag, name, asset_id):
    """A release's archive. With a credential for the repository's owner it comes through the API's asset endpoint,
    which serves private repositories too; anonymous, through the public download URL. Both redirect to GitHub's
    storage host, which never receives the credential."""
    api = API + repository + "/releases/assets/" + str(int(asset_id))
    if credential(api):
        return fetch(api, accept="application/octet-stream")
    return fetch("https://github.com/" + repository + "/releases/download/" + quote(tag, safe="") + "/" + name)


def source_of(product):
    source = next((item for item in sources() if item.product == product), None)
    require(source is not None, "Product has no approved release source")
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
    """Each release source's newest published release (a pre-release never counts), for the release watcher: one
    small GitHub read per product. A source GitHub cannot serve says so and names no release."""
    result = []
    for source in sources():
        try:
            releases = [item for item in published(source, per_page=10) if not item["prerelease"]]
        except (CommandFailed, ValueError) as error:
            result.append({"product": source.product, "ok": False,
                           "reason": str(error)[:300] if isinstance(error, CommandFailed) else "Unreadable releases"})
            continue
        newest = max(releases, key=lambda item: item["publishedAt"], default=None)
        result.append({"product": source.product, "ok": True,
                       "release": None if newest is None else {key: newest[key] for key in
                                                               ("id", "release", "publishedAt")}})
    return result


NOTES_LIMIT = 20000
MIGRATION_PATH = re.compile(r"(^|/)(Migrations|migrations)/")


def changelog(product, start, end):
    """What changed between two releases of a product: the bodies of the releases published after `start` up to `end`,
    newest first, and the compare commit list as a fallback for bodies that say little, with the changed files that
    look like schema migrations. `start` is what the target runs now, or None when it runs nothing. Bodies are
    returned as plain text for the console to show, never rendered."""
    source = source_of(product)
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
    for source in sources():
        if not source.workflow:
            continue
        try:
            result.append({"product": source.product, "ok": True, "failing": failed_build(source)})
        except (CommandFailed, ValueError) as error:
            result.append({"product": source.product, "ok": False, "failing": None,
                           "reason": str(error)[:300] if isinstance(error, CommandFailed) else "Unreadable runs"})
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
    repository = catalog.product(product).repository
    since = datetime.fromtimestamp(now - USAGE_DAYS * 24 * 60 * 60, timezone.utc).date().isoformat()
    runs, seconds, workflows = 0, 0.0, {}
    for page in range(1, USAGE_PAGES + 1):
        listing = json.loads(fetch(API + repository + "/actions/runs?per_page=100&page=" + str(page)
                                   + "&created=%3E%3D" + since))
        items = listing.get("workflow_runs") if isinstance(listing, dict) else None
        for run in items or []:
            if not isinstance(run, dict):
                continue
            runs += 1
            started, ended = parse_time(run.get("run_started_at")), parse_time(run.get("updated_at"))
            if run.get("status") == "completed" and started and ended and ended >= started:
                seconds += (ended - started).total_seconds()
                name = str(run.get("name") or "workflow")[:100]
                workflows[name] = workflows.get(name, 0.0) + (ended - started).total_seconds()
        if not items or len(items) < 100:
            break
    result = {"product": product, "repository": repository, "since": since, "days": USAGE_DAYS,
              "actions": {"runs": runs, "minutes": round(seconds / 60, 1),
                          "workflows": [{"name": name, "minutes": round(value / 60, 1)}
                                        for name, value in sorted(workflows.items(), key=lambda item: -item[1])],
                          "complete": runs < USAGE_PAGES * 100},
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
    source = next((item for item in sources() if item.product == product), None)
    result = []
    for service, image in (source.images if source else ()):
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
    for source in sources():
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
    artifact = next((item for item in available() if item["id"] == identifier), None)
    require(artifact is not None, "Release artifact is no longer available")
    source = source_of(artifact["product"])
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


def validate_source(bundle, source, artifact):
    manifest = validate_bundle(bundle)
    require(manifest["product"] == source.product and manifest["release"] == artifact["release"], "Release identity mismatch")
    expected = dict(source.images)
    require(manifest["images"].keys() == expected.keys(), "Unexpected release service")
    for service, image in manifest["images"].items():
        require(image.split("@")[0] == expected[service], "Image is outside the approved repository")
    if artifact.get("kind") == "candidate":
        # The artifact's name records the commit the workflow built; the bundle must agree.
        require(manifest.get("kind") == "candidate" and manifest["sourceCommit"] == artifact["commit"],
                "Candidate source commit mismatch")
        return manifest
    commit = json.loads(fetch("https://api.github.com/repos/" + source.repository + "/commits/"
                              + quote(artifact["release"], safe="")))
    require(commit.get("sha") == manifest["sourceCommit"], "Release source commit mismatch")
    return manifest


def request_build(product, commit):
    """Starts the product's build workflow for a commit that has no build yet."""
    source = source_of(product)
    require(source.workflow and WORKFLOW.fullmatch(source.workflow), "This product has no build workflow")
    require(isinstance(commit, str) and SHA.fullmatch(commit), "Give the full commit SHA")
    require(not any(item["commit"] == commit for item in candidates(source)), "A build of this commit already exists")
    found = json.loads(fetch(API + source.repository + "/commits/" + commit))
    require(isinstance(found, dict) and found.get("sha") == commit, "The commit is not in the product repository")
    post(API + source.repository + "/actions/workflows/" + quote(source.workflow, safe="") + "/dispatches",
         {"ref": source.default_branch, "inputs": {"commit": commit}})
    return {"product": product, "commit": commit, "status": "requested"}


def branches(product):
    # Any catalog product lists its branches; a release source is needed only to build or deploy them.
    repository = catalog.product(product).repository
    listing = json.loads(fetch(API + repository + "/branches?per_page=100"))
    return sorted(item["name"] for item in listing if isinstance(item, dict)
                  and isinstance(item.get("name"), str) and BRANCH.fullmatch(item["name"]))


def commits(product, branch):
    """A branch's recent commits, each with its build when one exists."""
    repository = catalog.product(product).repository
    source = next((item for item in sources() if item.product == product), None)
    require(isinstance(branch, str) and BRANCH.fullmatch(branch), "Invalid branch")
    listing = json.loads(fetch(API + repository + "/commits?per_page=30&sha=" + quote(branch, safe="")))
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
