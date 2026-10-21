"""The registry sign-in a deploy's pull borrows from the control host, so products hosts keep no registry token.

For a release with GHCR images, the control host picks the first credential GHCR accepts for pulling every one of
them: the GitHub App's installation token for the images' owner (an hour-long token), else the read-only token file
(WHEELHOUSE_GITHUB_TOKEN_FILE). It asks GHCR's token service first, from the control host, so a refused credential
never reaches a target. The chosen one travels on the deploy's SSH stdin; the target signs in for the pull only and
signs out straight after (runner.registry_session). Records keep which credential was used and which were refused,
never a token."""
from __future__ import annotations
import base64
import json
import os
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
import artifacts
import catalog
from runner import Rejected

SERVER = "ghcr.io"
# GHCR's user name for a GitHub App installation token; a token file signs in as its own GitHub login.
INSTALLATION_USER = "x-access-token"
FALLBACK_USER = "wheelhouse"
TOKEN_SERVICE = "https://ghcr.io/token"


def repositories(images):
    """The GHCR repositories of image references (`ghcr.io/<owner>/<path>[@sha256:…]`), sorted and lowercase."""
    result = set()
    for image in images:
        if isinstance(image, str) and image.startswith(SERVER + "/"):
            name = image[len(SERVER) + 1:].split("@", 1)[0]
            last = name.rsplit("/", 1)[-1]
            if ":" in last:  # a tag, never part of the repository
                name = name[:len(name) - len(last)] + last.split(":", 1)[0]
            result.add(name.lower())
    return sorted(result)


def accepts(repository, username, password):
    """Whether GHCR's token service grants a pull of one repository to a credential: (accepted, HTTP status). A
    credential GHCR refuses answers 401 or 403, even for a public repository; None is no answer at all."""
    url = TOKEN_SERVICE + "?" + urllib.parse.urlencode({"service": SERVER, "scope": "repository:" + repository + ":pull"})
    basic = base64.b64encode((username + ":" + password).encode()).decode()
    request = urllib.request.Request(url, headers={"Authorization": "Basic " + basic, "User-Agent": "Wheelhouse-registry"})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            granted = response.status == 200 and isinstance(json.loads(response.read(65536)).get("token"), str)
            return granted, response.status
    except urllib.error.HTTPError as error:
        return False, error.code
    except (urllib.error.URLError, TimeoutError, OSError, ValueError):
        return False, None


def token_login(token):
    """The GitHub login a token file's token belongs to, which GHCR takes as its user name; None when GitHub won't
    say."""
    request = urllib.request.Request("https://api.github.com/user", headers={
        "Authorization": "Bearer " + token, "User-Agent": "Wheelhouse-registry", "X-GitHub-Api-Version": "2022-11-28"})
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


def sign_in_for(images, check=accepts, login=token_login):
    """The sign-in a deploy's pull takes, and what was tried. Returns (sign-in, attempt): the sign-in is
    {server, username, password, credential} or None (no GHCR image, or no credential GHCR accepts, so the pull goes
    anonymous); the attempt names the credential used and the ones GHCR refused, never a token."""
    repos = repositories(images)
    if not repos:
        return None, None
    owners = {repo.split("/", 1)[0] for repo in repos}
    refused = []
    for kind, user, password in candidates(next(iter(owners)) if len(owners) == 1 else None, login):
        if all(check(repo, user, password)[0] for repo in repos):
            return ({"server": SERVER, "username": user, "password": password, "credential": kind},
                    {"server": SERVER, "credential": kind, "refused": refused})
        refused.append(kind)
    return None, {"server": SERVER, "credential": None, "refused": refused}


def probe(root, product=None, check=accepts, login=token_login):
    """Which credential GHCR accepts for each inventory product's images, per repository: the App's installation token
    for their owner and the token file. Reads tokens the way a deploy does (the console's tokens file under the runner
    root when no other is named); returns statuses only, never a token."""
    tokens = Path(root) / "github" / "tokens.json"
    if not os.environ.get("WHEELHOUSE_GITHUB_TOKENS_FILE") and tokens.is_file():
        os.environ["WHEELHOUSE_GITHUB_TOKENS_FILE"] = str(tokens)
    result = []
    for item in catalog.products():
        if (product is not None and item.slug != product) or not item.images():
            continue
        # Every repository's images: a product built from several pulls all of them in one deploy.
        repos = repositories(image for _, image in item.images())
        if not repos:
            continue
        owners = sorted({repo.split("/", 1)[0] for repo in repos})
        tried = []
        for kind, user, password in candidates(owners[0] if len(owners) == 1 else None, login):
            statuses = {repo: check(repo, user, password) for repo in repos}
            tried.append({"credential": kind, "accepted": all(accepted for accepted, _ in statuses.values()),
                          "repositories": [{"repository": repo, "accepted": accepted, "status": status}
                                           for repo, (accepted, status) in statuses.items()]})
        result.append({"product": item.slug, "owners": owners, "credentials": tried})
    return {"server": SERVER, "products": result}
