"""Discovery of completed release assets and per-commit candidate builds from the inventory's public repositories."""
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


def fetch(url, limit=MAX_ARCHIVE):
    headers = {"User-Agent": "Wheelhouse-release-catalog", "X-GitHub-Api-Version": "2022-11-28"}
    token_file = os.environ.get("WHEELHOUSE_GITHUB_TOKEN_FILE")
    if token_file and urlparse(url).netloc == "api.github.com":
        token = Path(token_file).read_text().strip()
        require(token and "\n" not in token, "Invalid catalog credential")
        headers["Authorization"] = "Bearer " + token
    request = Request(url, headers=headers)
    try:
        with build_opener(ApiRedirect()).open(request, timeout=15) as response:
            require(response.url.startswith("https://"), "Insecure release response")
            data = response.read(limit + 1)
    except HTTPError as error:
        # Rate limits surface as 403/429. Only the status is reported, never the body or headers.
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None
    except (URLError, TimeoutError):
        raise CommandFailed("GitHub request failed (network)") from None
    require(len(data) <= limit, "Release response exceeded its size limit")
    return data


def token():
    token_file = os.environ.get("WHEELHOUSE_GITHUB_TOKEN_FILE")
    if not token_file:
        return None
    value = Path(token_file).read_text().strip()
    require(value and "\n" not in value, "Invalid catalog credential")
    return value


def post(url, body):
    """A GitHub API write. Starting a build needs a token with Actions write access on the product repository."""
    credential = token()
    require(credential, "Starting a build needs WHEELHOUSE_GITHUB_TOKEN_FILE with Actions write access")
    request = Request(url, data=json.dumps(body).encode(), method="POST",
                      headers={"User-Agent": "Wheelhouse-release-catalog", "X-GitHub-Api-Version": "2022-11-28",
                               "Accept": "application/vnd.github+json", "Content-Type": "application/json",
                               "Authorization": "Bearer " + credential})
    try:
        with build_opener(ApiRedirect()).open(request, timeout=15) as response:
            require(response.status in (200, 201, 202, 204), "GitHub refused the build request")
    except HTTPError as error:
        raise CommandFailed("GitHub request failed (HTTP " + str(error.code) + ")") from None
    except (URLError, TimeoutError):
        raise CommandFailed("GitHub request failed (network)") from None


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


def available(root=None):
    result = []
    for source in sources():
        require(source.provider is ArtifactProvider.GITHUB_RELEASES, "Unsupported artifact provider")
        # A bounded recent catalog; old deployed bundles remain in the target recovery journal.
        try:
            releases = json.loads(fetch(API + source.repository + "/releases?per_page=100"))
        except CommandFailed:
            if rehearsal():
                continue  # An offline local server still lists its imported bundles.
            raise
        for release in releases:
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
            url = ("https://github.com/" + source.repository + "/releases/download/"
                   + quote(artifact["release"], safe="") + "/" + source.asset_name)
            archive_bytes = fetch(url)
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
