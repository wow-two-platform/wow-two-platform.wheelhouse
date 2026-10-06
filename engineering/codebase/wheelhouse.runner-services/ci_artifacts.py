"""Select a published Wheelhouse bundle for one tested commit without rebuilding or relabelling it."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from urllib.parse import quote

import artifacts
import transport
from runner import CommandFailed, Rejected, SHA, require, validate_bundle

REPOSITORY = "wow-two-platform/wow-two-platform.wheelhouse"
ASSET = "wheelhouse-release.tar.gz"
IMAGE = "ghcr.io/" + REPOSITORY + "/console"
VERSION = re.compile(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")


def release_candidates(repository):
    """Bound discovery to 100 public releases; resolve tags instead of trusting target_commitish."""
    require(repository == REPOSITORY, "Unapproved deployment repository")
    listing = json.loads(artifacts.fetch(artifacts.API + repository + "/releases?per_page=100"))
    require(isinstance(listing, list) and len(listing) <= 100, "Invalid release listing")
    candidates = []
    for release in listing:
        require(isinstance(release, dict), "Invalid release listing")
        tag = release.get("tag_name")
        match = VERSION.fullmatch(tag) if isinstance(tag, str) else None
        if not match or release.get("draft") is not False or release.get("prerelease") is not False:
            continue
        if not release.get("published_at"):
            continue
        assets = release.get("assets")
        require(isinstance(assets, list) and all(isinstance(asset, dict) for asset in assets), "Invalid release assets")
        selected = [asset for asset in assets if asset.get("name") == ASSET]
        if not selected:
            continue
        require(len(selected) == 1, "Ambiguous release asset")
        asset = selected[0]
        require(asset.get("state") == "uploaded" and type(asset.get("size")) is int
                and 0 < asset["size"] <= artifacts.MAX_ARCHIVE
                and type(asset.get("id")) is int and asset["id"] > 0
                and re.fullmatch(r"sha256:[a-f0-9]{64}", str(asset.get("digest", ""))),
                "Invalid release asset metadata")
        resolved = json.loads(artifacts.fetch(artifacts.API + repository + "/commits/" + quote(tag, safe="")))
        require(isinstance(resolved, dict) and isinstance(resolved.get("sha"), str)
                and SHA.fullmatch(resolved["sha"]), "Invalid release source commit")
        candidates.append({"release": tag, "sourceCommit": resolved["sha"], "assetDigest": asset["digest"],
                           "version": tuple(map(int, match.groups()))})
    return sorted(candidates, key=lambda candidate: candidate["version"], reverse=True)


def select_release(candidates, commit, ancestor, equivalent):
    """Exact source wins; fallback needs both ancestry and proof of unchanged deployment inputs."""
    exact = next((candidate for candidate in candidates if candidate["sourceCommit"] == commit), None)
    if exact is not None:
        return exact
    for candidate in candidates:
        if ancestor(candidate["sourceCommit"], commit) and equivalent(candidate):
            return candidate
    raise CommandFailed("No published release matches the tested deployment inputs")


def is_ancestor(repo_root, source, commit):
    result = subprocess.run(["git", "-C", str(repo_root), "merge-base", "--is-ancestor", source, commit],
                            capture_output=True, timeout=30)
    require(result.returncode in (0, 1), "Release ancestry could not be verified")
    return result.returncode == 0


def equivalent_bundle(repo_root, generator_path, commit, bundle, manifest):
    """Use the pinned shared generator's plan contract; never approximate its change-path rules."""
    result = subprocess.run([sys.executable, str(generator_path), "plan", "--repo", str(repo_root),
                             "--commit", commit, "--base", str(bundle)],
                            capture_output=True, text=True, timeout=60)
    require(result.returncode == 0, "Release equivalence planning failed")
    try:
        planned = json.loads(result.stdout)
    except (ValueError, TypeError):
        raise CommandFailed("Invalid release equivalence plan") from None
    require(isinstance(planned, dict) and planned.get("product") == "wheelhouse"
            and planned.get("sourceCommit") == commit and planned.get("base") == manifest["release"]
            and type(planned.get("descriptorChanged")) is bool
            and isinstance(planned.get("services"), dict)
            and set(planned["services"]) == {"console"}, "Invalid release equivalence plan")
    console = planned["services"]["console"]
    require(isinstance(console, dict) and type(console.get("build")) is bool, "Invalid release equivalence plan")
    if planned["descriptorChanged"] or console["build"]:
        return False
    require(console.get("image") == manifest["images"]["console"], "Release equivalence image mismatch")
    return True


def resolve_bundle(repository, commit, destination, repo_root, generator_path):
    """Materialize a verified bundle in a new directory; retain its actual sourceCommit on docs-only pushes."""
    try:
        return _resolve_bundle(repository, commit, destination, repo_root, generator_path)
    except (Rejected, CommandFailed):
        raise
    except (OSError, ValueError, TypeError, KeyError, tarfile.TarError, subprocess.SubprocessError):
        # Parser, archive and process failures can contain input values. Only static diagnostics leave CI.
        raise CommandFailed("Release bundle resolution failed") from None


def _resolve_bundle(repository, commit, destination, repo_root, generator_path):
    require(repository == REPOSITORY, "Unapproved deployment repository")
    require(isinstance(commit, str) and SHA.fullmatch(commit), "Give the full tested commit SHA")
    destination = Path(destination)
    require(not destination.exists() and not destination.is_symlink(), "Deployment bundle destination already exists")
    require(Path(generator_path).is_file(), "Pinned release generator is missing")
    candidates = release_candidates(repository)
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        loaded = {}

        def load(candidate):
            tag = candidate["release"]
            if tag not in loaded:
                payload = artifacts.fetch("https://github.com/" + repository + "/releases/download/"
                                          + quote(tag, safe="") + "/" + ASSET)
                require("sha256:" + hashlib.sha256(payload).hexdigest() == candidate["assetDigest"],
                        "Release asset changed")
                archive = root / (tag + ".tar.gz")
                archive.write_bytes(payload)
                identifier = "release-" + hashlib.sha256(tag.encode()).hexdigest()[:32]
                transport.import_bundle(root, archive, identifier)
                bundle = root / "bundles" / identifier
                manifest = validate_bundle(bundle)
                require(manifest["product"] == "wheelhouse" and manifest["release"] == tag
                        and manifest.get("kind", "release") == "release"
                        and manifest["platform"] == "linux/amd64", "Unexpected deployment release")
                require(manifest["sourceCommit"] == candidate["sourceCommit"], "Release source commit mismatch")
                require(set(manifest["images"]) == {"console"}
                        and manifest["images"]["console"].split("@")[0] == IMAGE,
                        "Image is outside the approved repository")
                loaded[tag] = bundle, manifest
            return loaded[tag]

        def equivalent(candidate):
            bundle, manifest = load(candidate)
            return equivalent_bundle(repo_root, generator_path, commit, bundle, manifest)

        selected = select_release(candidates, commit,
                                  lambda source, head: is_ancestor(repo_root, source, head), equivalent)
        bundle, manifest = load(selected)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(bundle, destination)
        return manifest
