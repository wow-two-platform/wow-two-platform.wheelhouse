"""The registry sign-in a deploy's pull borrows from the control host, so products hosts keep no registry token.

For a release with GHCR images, the control host picks the first credential that can read every one of them: the GitHub
App's installation token for the images' owner (an hour-long token), else the read-only token file
(WHEELHOUSE_GITHUB_TOKEN_FILE). A credential counts only when it can read each image's exact manifest: GHCR's token
service grants it a pull, and the registry then answers a HEAD of the image's digest (else its tag) under that grant.
The token service alone says yes too often: it grants an App's installation token a pull of any repository of its
owner, yet the registry refuses that token the packages of a private repository (a package that inherits the
repository's access), so only a classic `read:packages` token reads those. Asking from the control host keeps a refused
credential from ever reaching a target. The chosen one travels on the deploy's SSH stdin; the target signs in for the
pull only and signs out straight after (runner.registry_session). An image no credential and no anonymous pull can read
travels as `unreadable`, so the target's pull step fails on it, if the host lacks it, saying what to place. Records keep
which credential was used, which were refused and what nothing could read, never a token; the console's Issues panel
reads the unreadable packages from `github/packages.json` under the runner root."""
from __future__ import annotations
import base64
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
import artifacts
import catalog
from runner import Rejected, write_json

SERVER = "ghcr.io"
REGISTRY = "https://ghcr.io"
# GHCR's user name for a GitHub App installation token; a token file signs in as its own GitHub login.
INSTALLATION_USER = "x-access-token"
FALLBACK_USER = "wheelhouse"
TOKEN_SERVICE = REGISTRY + "/token"
AGENT = "Wheelhouse-registry"
# The answers that say a credential cannot read an image: refused, forbidden, or hidden as missing. Anything else (no
# answer, a server error) proves nothing, so it never names a package unreadable.
DENIED = (401, 403, 404)
MANIFEST_TYPES = ", ".join([
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.docker.distribution.manifest.v2+json",
])
# Where the owner places the token file under the runner root when the console names no other (Deployment:GitHubTokenFile).
DEFAULT_TOKEN_FILE = Path("github-token") / "token"
# Where the runner keeps the packages nothing could read, under the runner root, for the console's Issues panel.
PACKAGES_NOTES = Path("github") / "packages.json"
# What the owner places when a private repository's package is unreadable; the pull step and the Issues panel say it.
TOKEN_FILE_HINT = ("an App token cannot read a private repository's packages; place a classic read:packages token "
                   "in <runner root>/github-token/token (or Deployment:GitHubTokenFile)")


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """A credential goes to ghcr.io only: a redirect is an answer (its 3xx status), never followed."""

    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


OPENER = urllib.request.build_opener(_NoRedirect())


def reference(image):
    """An image reference's GHCR repository (lowercase) and manifest reference — its digest, else its tag, else None
    (an inventory image names neither) — or None for an image on another registry."""
    if not isinstance(image, str) or not image.startswith(SERVER + "/"):
        return None
    name, _, digest = image[len(SERVER) + 1:].partition("@")
    last = name.rsplit("/", 1)[-1]
    tag = None
    if ":" in last:  # a tag, never part of the repository
        tag = last.split(":", 1)[1]
        name = name[:len(name) - len(last)] + last.split(":", 1)[0]
    return name.lower(), digest or tag or None


def references(images):
    """The distinct GHCR images of image references, as (repository, reference), sorted."""
    return sorted({item for item in map(reference, images) if item}, key=lambda item: (item[0], item[1] or ""))


def repositories(images):
    """The GHCR repositories of image references (`ghcr.io/<owner>/<path>[@sha256:…]`), sorted and lowercase."""
    return sorted({repository for repository, _ in references(images)})


def _grant(repository, username, password):
    """GHCR's token service's pull grant for one repository: (bearer, status). No user name asks anonymously."""
    url = TOKEN_SERVICE + "?" + urllib.parse.urlencode({"service": SERVER, "scope": "repository:" + repository + ":pull"})
    headers = {"User-Agent": AGENT}
    if username is not None:
        headers["Authorization"] = "Basic " + base64.b64encode((username + ":" + (password or "")).encode()).decode()
    try:
        with OPENER.open(urllib.request.Request(url, headers=headers), timeout=10) as response:
            token = json.loads(response.read(65536)).get("token")
            return (token if isinstance(token, str) and token else None), response.status
    except urllib.error.HTTPError as error:
        return None, error.code
    except (urllib.error.URLError, TimeoutError, OSError, ValueError, AttributeError):
        return None, None


def accepts(image, username=None, password=None):
    """Whether a credential can read one image, as the pull would: GHCR's token service grants it a pull of the image's
    repository, and the registry answers a HEAD of the image's exact manifest under that grant — or, for an image that
    names no digest or tag (the inventory's), a listing of the repository's tags. `image` is (repository, reference);
    no user name asks anonymously. Returns (readable, HTTP status): a refusal's status — the token service's or the
    registry's — or None when GHCR gave no answer."""
    repository, manifest = image
    bearer, status = _grant(repository, username, password)
    if bearer is None:
        return False, status if status != 200 else None
    headers = {"Authorization": "Bearer " + bearer, "User-Agent": AGENT}
    if manifest is None:
        request = urllib.request.Request(REGISTRY + "/v2/" + repository + "/tags/list?n=1", headers=headers)
    else:
        url = REGISTRY + "/v2/" + repository + "/manifests/" + urllib.parse.quote(manifest, safe=":")
        request = urllib.request.Request(url, method="HEAD", headers={**headers, "Accept": MANIFEST_TYPES})
    try:
        with OPENER.open(request, timeout=10) as response:
            return response.status == 200, response.status
    except urllib.error.HTTPError as error:
        return False, error.code
    except (urllib.error.URLError, TimeoutError, OSError):
        return False, None


def token_login(token):
    """The GitHub login a token file's token belongs to, which GHCR takes as its user name; None when GitHub won't
    say."""
    request = urllib.request.Request("https://api.github.com/user", headers={
        "Authorization": "Bearer " + token, "User-Agent": AGENT, "X-GitHub-Api-Version": "2022-11-28"})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            login = json.loads(response.read(65536)).get("login")
    except (urllib.error.URLError, TimeoutError, OSError, ValueError):
        return None
    return login if isinstance(login, str) and login and len(login) <= 39 else None


def candidates(owner, login=token_login):
    """The credentials to try, in order: the App's installation token for `owner` (None when the images span owners),
    then the token file. Each is (kind, user name, token)."""
    result = []
    installed = artifacts.installation_token(owner) if owner else None
    if installed:
        result.append(("installation", INSTALLATION_USER, installed))
    try:
        fallback = artifacts.token()
    except (Rejected, OSError):
        fallback = None
    if fallback:
        result.append(("file", login(fallback) or FALLBACK_USER, fallback))
    return result


def _owner(wanted):
    owners = {repository.split("/", 1)[0] for repository, _ in wanted}
    return next(iter(owners)) if len(owners) == 1 else None


def sign_in_for(images, check=None, login=token_login):
    """The sign-in a deploy's pull takes, and what was tried. Returns (sign-in, attempt): the sign-in is
    {server, username, password, credential} or None (no GHCR image, nothing private, or no credential that reads a
    private one, so the pull goes anonymous); the attempt names the credential used, the ones that could not read every
    image (`refused`) and, under `unreadable`, each image repository that neither a credential nor an anonymous pull
    can read, with the status that said so. Never a token."""
    wanted = references(images)
    if not wanted:
        return None, None
    check = check or accepts
    owner, needed, anonymous = _owner(wanted), wanted, None
    if owner is None:
        # Two owners: a product's own images beside another repository's pinned image (M77), which is public. What GHCR
        # serves anonymously needs no credential, so the owner of the rest picks the App's token, and the sign-in
        # covers only those repositories (`repositories`); Compose pulls the public one anonymously.
        anonymous = {image: check(image, None, None) for image in wanted}
        rest = [image for image in wanted if not anonymous[image][0]]
        if rest and len(rest) < len(wanted):
            needed, owner = rest, _owner(rest)
    refused, misses = [], []
    for kind, user, password in candidates(owner, login):
        missed = {}
        for image in needed:
            readable, status = check(image, user, password)
            if not readable:
                missed[image] = status
        if not missed:
            sign_in = {"server": SERVER, "username": user, "password": password, "credential": kind}
            if needed is not wanted:
                sign_in["repositories"] = sorted({SERVER + "/" + repository for repository, _ in needed})
            return sign_in, {"server": SERVER, "credential": kind, "refused": refused}
        refused.append(kind)
        misses.append(((kind, user, password), missed))
    # No credential reads every image. What GHCR serves anonymously needs no sign-in; the rest decides.
    anonymous = anonymous or {image: check(image, None, None) for image in wanted}
    private = [image for image in wanted if not anonymous[image][0]]
    chosen, left = None, private
    for credential, missed in misses:  # the first that reads the most private images, if it reads any
        unread = [image for image in private if image in missed]
        if len(unread) < len(left):
            chosen, left = credential, unread
    unreadable = {}
    for image in left:
        if not all(image in missed for _, missed in misses):
            continue  # another credential reads it; the one sign-in covers fewer
        statuses = [anonymous[image][1]] + [missed[image] for _, missed in misses]
        if all(status in DENIED for status in statuses):
            unreadable.setdefault(image[0], statuses[-1])
    attempt = {"server": SERVER, "credential": chosen[0] if chosen else None, "refused": refused}
    if unreadable:
        attempt["unreadable"] = [{"repository": repository, "status": status}
                                 for repository, status in sorted(unreadable.items())]
    sign_in = ({"server": SERVER, "username": chosen[1], "password": chosen[2], "credential": chosen[0]}
               if chosen else None)
    return sign_in, attempt


def unreadable_images(attempt):
    """The image repositories (`ghcr.io/<owner>/<path>`) an attempt found nothing could read, for the target's pull."""
    return [SERVER + "/" + item["repository"] for item in (attempt or {}).get("unreadable", [])]


def describe(attempt, kinds=None):
    """One operator-safe line for an attempt: who reads the images, or which packages nothing can read and what to
    place. `kinds` names each credential kind in words."""
    kinds = kinds or {"installation": "the GitHub App's token", "file": "the token file"}
    if attempt is None:
        return "no GHCR image"
    unreadable = [item["repository"] for item in attempt.get("unreadable", [])]
    if unreadable:
        return ("No credential can read " + ", ".join(SERVER + "/" + name for name in unreadable) + ": "
                + TOKEN_FILE_HINT)
    if attempt.get("credential"):
        return "GHCR images read with " + kinds.get(attempt["credential"], attempt["credential"])
    return "GHCR images are public; no sign-in needed"


def note_packages(root, product, images, attempt, clock=None):
    """Keeps, under the runner root, each image repository nothing could read for the console's Issues panel
    ({"packages": {repository: {status, product, at}}}), and drops those this attempt read. Visibility only: a failed
    write never fails the caller. Never a token."""
    path = Path(root) / PACKAGES_NOTES
    checked = set(repositories(images))
    if not checked:
        return
    unreadable = {item["repository"]: item.get("status") for item in (attempt or {}).get("unreadable", [])}
    try:
        kept = json.loads(path.read_text())
        packages = kept.get("packages") if isinstance(kept, dict) and isinstance(kept.get("packages"), dict) else {}
    except (OSError, ValueError):
        packages = {}
    before = dict(packages)
    for repository in checked:
        packages.pop(repository, None)
    at = (clock or (lambda: datetime.now(timezone.utc)))().isoformat()
    for repository, status in unreadable.items():
        packages[repository] = {"status": status, "product": product, "at": at}
    if packages == before and not unreadable:
        return
    try:
        path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        write_json(path, {"packages": packages})
    except OSError:
        pass


def probe(root, product=None, check=None, login=token_login):
    """Whether each credential can read each inventory product's images, per image: the App's installation token for
    their owner, the token file, then an anonymous pull; and what a deploy would sign in with. Reads tokens the way a
    deploy does (the console's tokens file and the placed token file under the runner root when no other is named);
    returns statuses only, never a token, and notes the packages nothing can read for the Issues panel."""
    tokens, placed = Path(root) / "github" / "tokens.json", Path(root) / DEFAULT_TOKEN_FILE
    if not os.environ.get("WHEELHOUSE_GITHUB_TOKENS_FILE") and tokens.is_file():
        os.environ["WHEELHOUSE_GITHUB_TOKENS_FILE"] = str(tokens)
    if not os.environ.get("WHEELHOUSE_GITHUB_TOKEN_FILE") and placed.is_file():
        os.environ["WHEELHOUSE_GITHUB_TOKEN_FILE"] = str(placed)
    check, answers, logins = check or accepts, {}, {}

    def asked(image, user, password):  # each question once: the deploy's choice below reuses the answers
        key = (image, user, password)
        if key not in answers:
            answers[key] = check(image, user, password)
        return answers[key]

    def named(token):
        if token not in logins:
            logins[token] = login(token)
        return logins[token]

    result = []
    for item in catalog.products():
        if (product is not None and item.slug != product) or not item.images():
            continue
        # Every repository's images: a product built from several pulls all of them in one deploy.
        images = [image for _, image in item.images()]
        wanted = references(images)
        if not wanted:
            continue
        tried = []
        for kind, user, password in [*candidates(_owner(wanted), named), ("anonymous", None, None)]:
            statuses = {image: asked(image, user, password) for image in wanted}
            tried.append({"credential": kind, "accepted": all(readable for readable, _ in statuses.values()),
                          "repositories": [{"repository": repository, "reference": manifest, "accepted": readable,
                                            "status": status}
                                           for (repository, manifest), (readable, status) in statuses.items()]})
        _, attempt = sign_in_for(images, check=asked, login=named)
        note_packages(root, item.slug, images, attempt)
        owners = sorted({repository.split("/", 1)[0] for repository, _ in wanted})
        result.append({"product": item.slug, "owners": owners, "credentials": tried, "deploy": attempt,
                       "summary": describe(attempt)})
    return {"server": SERVER, "products": result}
