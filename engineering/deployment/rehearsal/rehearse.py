#!/usr/bin/env python3
"""Runs the local server: Wheelhouse deploys to a local SSH target, as to a VPS. Never points at a real host.

The local server hosts ForeverPin's dev and prod environments at once. Dev is the default; prod deploys on demand and
asks for its target ID typed out. Prod takes a release only after it succeeded on dev, unless --skip-dev-pass comes
with the typed ID. Each environment's sites answer through the local ingress at
http://<site>-foreverpin.<environment>.localhost:18080.

`self` ships Wheelhouse the way it ships products: its own release generator builds its image and bundle from this
checkout, and the runner deploys it to the wheelhouse-dev target, at http://console-wheelhouse.dev.localhost:18080.

`products` proves the products-host kit on a second, local host with its own Docker daemon: bootstrap-products.sh's
steps lay it out, prepare.py creates its platform, databases and site nodes, authorize-runner.sh pins and accepts a
runner key, and the runner deploys ForeverPin dev to it. Its public sites answer on their baseline sslip.io hosts
through the host's public ingress (80 redirects to 443), from another machine on the rig's network; the private entry
point stays on loopback. Traefik's letsencrypt resolver is wired to each public router; a CA that never answers stands
in for Let's Encrypt, so no certificate is issued here. Then ForeverPin becomes a product of two repositories (decision
70) of three repositories, the third of which has not released: its release is cut into one bundle per service, as the
two released repositories would publish them, the runner composes them into a release set that leaves the third out
(the same set, ID and label the two make alone) and names it as not in it yet, and the set deploys over the
single-repository release with nothing restarting and both baseline hosts still answering.

`networks` proves on that host that the platform network keeps its fixed addresses: containers that start before
Traefik take dynamic ones from .128/25, and repair-networks.sh gives a network made without the range one.

`self-deploy` proves the kit's break-glass self-deploy.py on the local server: Wheelhouse's own release goes to
wheelhouse-dev through the transport, as prod deploys it (its migrate step and declared dev check run), then
self-deploy.py, run as the deploy account on the target with the runner it copies out of the release image, reads the
console, deploys a second release, rolls back with no network, and sees a broken release roll back by itself through
the rollback gate. Each release records its image's migration sets offline, as CI does.

`harden` proves harden-control.sh on a real Ubuntu 26.04 host with real UFW and sshd: --check reports, the first run
turns the firewall on with public SSH kept and converges, --tailnet-only-ssh refuses without a tailnet session and
then closes public SSH and pins the runner keys, and --public-ssh reopens it.

`enroll` enrolls a fresh root-only Ubuntu 26.04 host with the runner from the console image, as Wheelhouse prod does:
the one-time key, the kit, a single-use key minted through the OAuth client, the node and host-key proofs, the seal.
Then it places ForeverPin's databases and site on it and applies the tailnet policy after its review. Tailscale is the
one stand-in: a fake API, and a stub CLI and site-node image on the host that spend the minted keys there.

`generated` proves v0.11's generated and linked setting keys through the console's own API, as a real deploy goes: the
console as it ships, signed in as its owner with a session sealed by its own key ring (session/), adds Keysmith
(keysmith/: a random key, a certificate command, and a second service linking both a key and a site URL) and deploys
its release, built by the pipelines generator, with no value given. It checks the rendered files, the certificate's
expiry, that no answer, log, record or journal outside those files holds a value, Regenerate through to both services,
and that the command ran with no network; it exits non-zero on any failed check.

`set-reset` proves a reset of one migration set on the products host, the rig's VPS (its own Docker daemon, platform
PostgreSQL and database placement; `products` first): Ledger (ledger/), whose identity service carries WoW2's Identity
image and its real `identity` set beside the desk's data in one database, deploys; 001's checksum is rewritten as an
in-place migration rewrite leaves it, so Check now and the next deploy refuse on drift; the set reset, through the
transport as the console submits it, snapshots the database, stops only identity, rolls the set back to 0 with the
running image and sets its level to 0; the desk keeps running and its data stays; the next deploy builds the set again.
It exits non-zero on any failed check.
"""
import argparse
import base64
import io
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tarfile
import time

HERE = Path(__file__).resolve().parent
STATE = HERE / "state"
INVENTORY = STATE / "inventory"
SECRETS = STATE / "secrets"
KEYS = STATE / "keys"
RUNNERS = HERE.parents[1] / "codebase" / "wheelhouse.runner-services"
TRANSPORT = RUNNERS / "transport.py"
WORKBENCH = HERE.parents[4]
# The release generator lives in wow-two-platform.pipelines, a sibling checkout in the workbench.
RELEASE = WORKBENCH / "wow-two-platform" / "wow-two-platform.pipelines" / "generator" / "release.py"
REPOSITORY = HERE.parents[2]
SELF = "wheelhouse"
SELF_TARGET = "wheelhouse-dev"
SELF_HOST = "console-wheelhouse.dev.localhost"
PRODUCT = "foreverpin"
PRODUCT_REPO = WORKBENCH / "ventures" / "10x-venture-forever-pin"
LOCAL_IMAGES = {"management": "foreverpin-management:local", "redirect": "foreverpin-redirect:local"}
SITES = {"management": "app", "redirect": "go"}
ENVIRONMENTS = ("dev", "prod")
# Compose projects of environments the rig no longer runs: `test` went on 2026-10-17, `rehearsal` predates
# environments. `down` still stops what an older rig left running under them.
RETIRED_ENVIRONMENTS = ("test", "rehearsal")
INGRESS = "http://{site}-" + PRODUCT + ".{environment}.localhost:18080"
REGISTRY = "localhost:15000"
VAULT_IMAGE = "secrets-vault:local"
CONSOLE = STATE / "console"
# What an IDE run of Wheelhouse.Api reads on this machine only: the launch profile sets LOCAL_SETTINGS=true. Never
# appsettings.Development.json, which a deployed dev console reads too. Its name before 2026-10-19 was
# appsettings.Machine.json; `migrate` renames an older copy.
API = HERE.parents[1] / "codebase" / "wheelhouse.backend-services" / "Wheelhouse.Api"
LOCAL_SETTINGS = API / "appsettings.Local.json"
# The containerized console's settings, mounted as a deployed target's are (appsettings.Target.json).
CONSOLE_SETTINGS = CONSOLE / "appsettings.Target.json"
CONSOLE_URL = "http://localhost:18210"
# The GitHub login that may sign in to the local console; change it in the generated settings if needed.
CONSOLE_ADMIN = os.environ.get("WHEELHOUSE_ADMIN", "sulton-max")
TERMINAL = {"succeeded", "failed", "rolled_back", "rollback_failed", "interrupted", "rejected"}
# The local products host (`products`): its own inventory root, server and stand-ins for the tailnet's name and address.
PRODUCTS_STATE = STATE / "products"
PRODUCTS_INVENTORY = PRODUCTS_STATE / "inventory"
PRODUCTS_SERVER = "local-products"
PRODUCTS_TAILNET = "rehearsal.ts.net"
PRODUCTS_ADDRESS = "100.88.0.1"
PRODUCTS_ROOT = "/srv/wheelhouse"
PRODUCTS_PORT = 2223
DEPLOY_USER = "wheelhouse-deploy"
# Where the products host's ACME resolver is pointed on the rig: nothing listens there, so Traefik's order for a
# baseline host fails at once and names the host and resolver in its log, and no request leaves the machine.
PRODUCTS_ACME_CA = "https://127.0.0.1:14000/directory"
CURL_IMAGE = "curlimages/curl:8.11.1"


def run(*arguments, env=None, capture=True):
    result = subprocess.run(arguments, env=env, capture_output=capture, text=True)
    if result.returncode:
        detail = (result.stderr or result.stdout or "").strip().splitlines()[-1:] if capture else []
        sys.exit("Failed: " + " ".join(map(str, arguments[:3])) + (" — " + detail[0] if detail else ""))
    return result.stdout if capture else ""


def private_dir(path):
    path.mkdir(parents=True, exist_ok=True)
    path.chmod(0o700)
    return path


def private_file(path, text):
    path.write_text(text)
    path.chmod(0o600)


def environment():
    # Compose builds through Buildx bake, which refuses the console's Dockerfile outside its context
    # (`../deployment/Dockerfile`) unless its filesystem entitlement check is off.
    return {**os.environ, "REHEARSAL_STATE": str(STATE), "WHEELHOUSE_REHEARSAL": "1", "BUILDX_BAKE_ENTITLEMENTS_FS": "0"}


def compose(*arguments, capture=True):
    return run("docker", "compose", "-f", str(HERE / "compose.yml"), *arguments, env=environment(), capture=capture)


def transport(*arguments, root=INVENTORY):
    return json.loads(run(sys.executable, str(TRANSPORT), *arguments, "--root", str(root), env=environment()))


def target_of(name):
    require_environment(name)
    return PRODUCT + "-" + name


def require_environment(name):
    if name not in ENVIRONMENTS:
        sys.exit("Unknown environment " + name + "; use " + ", ".join(ENVIRONMENTS))


def migrate():
    """Moves inventory written before environments existed, when the rig was one `rehearsal` target, and settings
    files written before the target's file became appsettings.Target.json and the machine's appsettings.Local.json."""
    for old, new in ((INVENTORY / "ssh" / "rehearsal", INVENTORY / "ssh" / "local"),
                     (INVENTORY / "vaults" / "rehearsal-vault", INVENTORY / "vaults" / "local-vault")):
        if old.is_dir() and not new.exists():
            old.rename(new)
    for old, new in ((CONSOLE / "appsettings.Local.json", CONSOLE_SETTINGS),
                     (API / "appsettings.Machine.json", LOCAL_SETTINGS)):
        if old.is_file() and not new.exists():
            old.rename(new)


def vault_files():
    """Bootstraps the local vault once, like its own bootstrap script, with a generated password."""
    folder = private_dir(STATE / "vault")
    credential = private_dir(INVENTORY / "vaults" / "local-vault") / "password"
    if (folder / "master_key").exists():
        return
    password = secrets.token_urlsafe(24)
    hashed = subprocess.run(["docker", "run", "--rm", "-i", "--network", "none", VAULT_IMAGE, "--hash-password"],
                            input=password + "\n", capture_output=True, text=True)
    if hashed.returncode or not hashed.stdout.strip().startswith("$argon2id$"):
        sys.exit("Vault password hashing failed; build " + VAULT_IMAGE + " from the secrets-vault repository")
    db_password = secrets.token_hex(32)
    files = {
        "master_key": base64.b64encode(secrets.token_bytes(32)).decode(),
        "admin_password_hash": hashed.stdout.strip(),
        "jwt_key": base64.b64encode(secrets.token_bytes(32)).decode(),
        "db_admin_password": secrets.token_hex(32),
        "db_init.sql": "CREATE ROLE vault LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '" + db_password
                       + "';\nCREATE DATABASE secrets_vault OWNER vault;",
        "db_connection": "Host=vault-db;Port=5432;Database=secrets_vault;Username=vault;Password=" + db_password,
    }
    for name, value in files.items():
        private_file(folder / name, value + "\n")
    private_file(credential, password + "\n")


def console_files():
    """Settings for the containerized console. The developer adds the GitHub OAuth app's id and secret."""
    private_dir(CONSOLE)
    password = CONSOLE / "database-password"
    if not password.exists():
        private_file(password, secrets.token_hex(24))
    settings = CONSOLE_SETTINGS
    if not settings.exists():
        private_file(settings, json.dumps({
            "ConnectionStrings": {"Wheelhouse": "Host=console-db;Database=wheelhouse;Username=wheelhouse;Password="
                                                + password.read_text().strip()},
            "Identity": {"GitHub": {"ClientId": "", "ClientSecret": ""}, "AllowedGitHubLogins": [CONSOLE_ADMIN]},
        }, indent=2) + "\n")
    return bool(json.loads(settings.read_text()).get("Identity", {}).get("GitHub", {}).get("ClientId"))


def machine_settings():
    """Points an IDE run at the local server: its database on 127.0.0.1:15432 and the runner over this checkout, paths
    relative to the API project. Written once; an edited file is left as it is."""
    if LOCAL_SETTINGS.exists():
        return
    LOCAL_SETTINGS.write_text(json.dumps({
        "//Local": "This machine's settings for an IDE run (the https launch profile sets LOCAL_SETTINGS=true); "
                     "gitignored and never in the image. Written by `python3 rehearse.py dev`.",
        "ConnectionStrings": {"Wheelhouse": "Host=localhost;Port=15432;Database=wheelhouse;Username=wheelhouse;"
                                            "Password=wheelhouse"},
        "Deployment": {"Rehearsal": "host", "Root": "../../../deployment/rehearsal/state/inventory",
                       "TransportPath": "../../wheelhouse.runner-services/transport.py"},
    }, indent=2) + "\n")
    print("Wrote " + str(LOCAL_SETTINGS))


def databases():
    """One database per environment on the local server's Postgres, as a VPS gives each product-environment."""
    for name in ENVIRONMENTS:
        database = PRODUCT + "_" + name
        found = compose("exec", "-T", "database", "psql", "-U", PRODUCT, "-d", PRODUCT, "-tAc",
                        "SELECT 1 FROM pg_database WHERE datname = '" + database + "'").strip()
        if found != "1":
            compose("exec", "-T", "database", "psql", "-U", PRODUCT, "-d", PRODUCT, "-c", "CREATE DATABASE " + database)


def up(console=False, dev=False):
    """Creates keys, pins the target's host key and starts the server, plus the console or the IDE database."""
    migrate()
    for folder in (STATE, KEYS, SECRETS, STATE / "settings", INVENTORY / "ssh" / "local"):
        private_dir(folder)
    identity = INVENTORY / "ssh" / "local" / "identity"
    if not identity.exists():
        run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "wheelhouse-local", "-f", str(identity))
    host_key = KEYS / "ssh_host_ed25519_key"
    if not host_key.exists():
        run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "local-target", "-f", str(host_key))
    (KEYS / "authorized_keys").write_text(Path(str(identity) + ".pub").read_text())
    # The host key is generated here, so the pin needs no trust-on-first-use scan. The console reaches the
    # same target as `target` inside the server's network.
    key = " ".join(Path(str(host_key) + ".pub").read_text().split()[:2])
    private_file(INVENTORY / "ssh" / "local" / "known_hosts", "[127.0.0.1]:2222 " + key + "\ntarget " + key + "\n")
    password = SECRETS / "database-password"
    if not password.exists():
        private_file(password, secrets.token_urlsafe(24))
    profiles = []
    if subprocess.run(["docker", "image", "inspect", VAULT_IMAGE], capture_output=True).returncode == 0:
        vault_files()
        profiles = ["--profile", "vault"]
    signin = console_files() if console else False
    if console:
        profiles += ["--profile", "console"]
    if dev:
        profiles += ["--profile", "dev"]
    compose(*profiles, "up", "-d", "--build", "--wait", capture=False)
    databases()
    if console:
        # The console reads its settings once at start; a restart applies an edited OAuth app or allowlist.
        compose(*profiles, "restart", "console", capture=False)
    print("Local server ready: ssh deploy@127.0.0.1 -p 2222, registry " + REGISTRY + ", sites on :18080"
          + (", vault http://127.0.0.1:18201" if "vault" in profiles else ", no vault (build " + VAULT_IMAGE
                                                                       + " to add one)"))
    if console:
        print("Console: " + CONSOLE_URL + (" — sign in with GitHub" if signin else
              " — sign-in is off until " + str(CONSOLE_SETTINGS) + " holds a GitHub OAuth app"
              " (callback " + CONSOLE_URL + "/api/identity/callback)"))
    if dev:
        machine_settings()
        print("IDE: run Wheelhouse.Api with the `https` launch profile, then open https://localhost:8210"
              " (database 127.0.0.1:15432 from appsettings.Local.json; sign-in uses the API project's user-secrets)")


def yaml_python():
    """An interpreter with PyYAML for the deploy.yml generator: this one, else the system Python."""
    if not RELEASE.is_file():
        sys.exit("The release generator is missing: clone wow-two-platform.pipelines beside Wheelhouse (" + str(RELEASE) + ")")
    for candidate in (sys.executable, "/usr/bin/python3", "python3"):
        if subprocess.run([candidate, "-c", "import yaml"], capture_output=True).returncode == 0:
            return candidate
    sys.exit("The deploy.yml generator needs PyYAML: pip install pyyaml")


def latest_release(healthy=False):
    """The newest imported release bundle: the base a candidate keeps unchanged services from. `healthy` passes over
    the releases `bundle --broken` builds to fail."""
    bundles = [path.parent for path in (INVENTORY / "bundles").glob("*/release.json")]
    releases = [bundle for bundle in bundles if json.loads((bundle / "release.json").read_text()).get("kind") == "release"
                and not (healthy and "broken" in bundle.name)]
    return max(releases, key=lambda bundle: (bundle / "release.json").stat().st_mtime, default=None)


def bundle(tag=None, broken=False):
    """Builds a bundle from ForeverPin's checkout with the local images and imports it.

    Without a tag it is a candidate of the checkout's commit (dev only); a tag makes it a release for any environment.
    """
    images = dict(LOCAL_IMAGES)
    if broken:
        if not tag:
            sys.exit("A broken release needs its own --tag, such as v0.0.1-broken.1")
        # It runs the redirect image as management, so the management smoke probe fails after replacement.
        images["management"] = LOCAL_IMAGES["redirect"]
    commit = run("git", "-C", str(PRODUCT_REPO), "rev-parse", "HEAD").strip()
    branch = run("git", "-C", str(PRODUCT_REPO), "rev-parse", "--abbrev-ref", "HEAD").strip()
    label = tag or "sha-" + commit[:7]
    bundle_id = PRODUCT + "-" + re.sub(r"[^a-z0-9-]+", "-", label.lower())
    if (INVENTORY / "bundles" / bundle_id).exists():
        print("Already imported " + bundle_id)
        return bundle_id
    output = private_dir(STATE / "build") / bundle_id
    archive = STATE / "build" / (bundle_id + ".tar.gz")
    arguments = [yaml_python(), str(RELEASE), "build", "--repo", str(PRODUCT_REPO), "--commit", commit,
                 "--registry", REGISTRY + "/" + PRODUCT, "--platform", platform(),
                 "--output", str(output), "--archive", str(archive)]
    if branch != "HEAD":
        arguments += ["--branch", branch]
    if tag:
        arguments += ["--tag", tag]
    base = latest_release()
    if base is not None and not broken:
        arguments += ["--base", str(base)]
    for service, image in images.items():
        arguments += ["--image", service + "=" + image]
    print(run(*arguments).strip())
    transport("import", "--archive", str(archive), "--bundle", bundle_id)
    print("Imported " + bundle_id)
    return bundle_id


def settings(bundle_id, name):
    """Writes the environment's private settings files from the release contract with local-only values."""
    password = (SECRETS / "database-password").read_text().strip()
    hosts = {service: site + "-" + PRODUCT + "." + name + ".localhost" for service, site in SITES.items()}
    values = {
        "DatabaseSettings": {"ConnectionString": "Host=rehearsal-database;Database=" + PRODUCT + "_" + name
                                                + ";Username=" + PRODUCT + ";Password=" + password},
        "ApiSettings": {"RedirectBaseUrl": INGRESS.format(site=SITES["redirect"], environment=name)},
        "Auth": {"Google": {"ClientId": "local.apps.googleusercontent.com"}},
        "Billing": {"SecretKey": "sk_test_local", "WebhookSecret": "whsec_local",
                    "Prices": {"Solo": "price_local_solo", "Pro": "price_local_pro", "Agency": "price_local_agency"},
                    "SuccessUrl": INGRESS.format(site=SITES["management"], environment=name) + "/billing/success",
                    "CancelUrl": INGRESS.format(site=SITES["management"], environment=name) + "/billing/cancel",
                    "PortalReturnUrl": INGRESS.format(site=SITES["management"], environment=name) + "/app/billing"},
        "Deployment": {"TrustedProxies": ["127.0.0.1"]},
    }

    def fill(template, source):
        return {key: fill(value, source.get(key, {})) if isinstance(value, dict) else source.get(key, value)
                for key, value in template.items()}

    folder = private_dir(SECRETS / name)
    for service, template in transport("template", "--bundle", bundle_id).items():
        filled = fill(template, values)
        # Health probes call localhost; visitors arrive through the ingress with the site's host.
        filled["AllowedHosts"] = "localhost;" + hosts[service]
        private_file(folder / (service + ".json"), json.dumps(filled, indent=2) + "\n")
    print("Wrote " + name + " settings for " + bundle_id)


def deploy(bundle_id, name, confirm=None, skip_dev_pass=False, timeout=420, target=None, root=INVENTORY):
    """Submits the bundle through the same transport the dashboard uses and waits for the outcome."""
    target = target or target_of(name)
    arguments = ["submit", "--target", target, "--bundle", bundle_id, "--actor", "local-server"]
    if confirm:
        arguments += ["--confirm", confirm]
    if skip_dev_pass:
        arguments.append("--skip-dev-pass")
    job = transport(*arguments, root=root)
    print("Submitted " + job["id"] + " to " + target)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        time.sleep(5)
        outcome = transport("status", "--job", job["id"], root=root)
        if outcome.get("status") in TERMINAL:
            for step in outcome.get("steps", []):
                print("  " + step["status"].ljust(9) + " " + step["name"]
                      + (" — " + step["detail"] if step.get("detail") else ""))
            print(json.dumps({key: outcome.get(key) for key in ("status", "release", "reason", "failure", "warnings")
                              if outcome.get(key) is not None}))
            if outcome.get("status") == "succeeded":
                for site in transport("state", "--target", target, root=root)["current"].get("sites", []):
                    probe = site.get("probe")
                    answer = "" if probe is None else " (answered " + str(probe.get("status")) + ")" if probe["ok"] \
                        else " (" + probe["detail"] + ")"
                    print("Open " + site["name"] + ": " + site["url"] + answer)
            return outcome
    sys.exit("Timed out waiting for " + job["id"])


def platform():
    return "linux/" + run("docker", "info", "--format", "{{.Architecture}}").strip() \
        .replace("x86_64", "amd64").replace("aarch64", "arm64")


def record_sets(output, archive):
    """Each migrate item's sets, as CI records them: the item's release image answers `<command> sets --json` with no
    network and no settings, and the generator writes `migrationSets` into the bundle and its archive."""
    manifest = json.loads((output / "release.json").read_text())
    items = manifest.get("migrate") or []
    if not items:
        return
    reports = private_dir(output.parent / (output.name + "-sets"))
    for item in items:
        command = item["command"]
        result = subprocess.run(["docker", "run", "--rm", "--network", "none", "--cap-drop", "ALL",
                                 "--security-opt", "no-new-privileges", "--entrypoint", command[0],
                                 manifest["images"][item["service"]], *command[1:], "sets", "--json"],
                                capture_output=True, text=True)
        if result.returncode:
            sys.exit(item["service"] + ": '" + " ".join(command) + " sets --json' exited " + str(result.returncode)
                     + " offline; the image must answer the migrate verbs with no settings or database")
        private_file(reports / (item["service"] + ".out"), result.stdout)
    print(run(yaml_python(), str(RELEASE), "sets", "--output", str(output), "--sets", str(reports),
              "--archive", str(archive)).strip())


def self_bundle():
    """Builds Wheelhouse's image and bundle with its own release generator, as its CI does, and imports it.
    The build takes the checkout as it is, so uncommitted changes ride along under HEAD's commit."""
    commit = run("git", "-C", str(REPOSITORY), "rev-parse", "HEAD").strip()
    bundle_id = SELF + "-sha-" + commit[:7]
    if (INVENTORY / "bundles" / bundle_id).exists():
        print("Already imported " + bundle_id)
        return bundle_id
    output = private_dir(STATE / "build") / bundle_id
    archive = STATE / "build" / (bundle_id + ".tar.gz")
    print(run(yaml_python(), str(RELEASE), "build", "--repo", str(REPOSITORY), "--commit", commit, "--checkout",
              "--registry", REGISTRY + "/" + SELF, "--platform", platform(),
              "--output", str(output), "--archive", str(archive)).strip())
    record_sets(output, archive)
    transport("import", "--archive", str(archive), "--bundle", bundle_id)
    print("Imported " + bundle_id)
    return bundle_id


def self_environment():
    """Wheelhouse's database on the local server's Postgres, and its settings. Sign-in stays off until the settings
    name a GitHub OAuth app whose callback is http://<SELF_HOST>:18080/api/identity/callback."""
    found = compose("exec", "-T", "database", "psql", "-U", PRODUCT, "-d", PRODUCT, "-tAc",
                    "SELECT 1 FROM pg_database WHERE datname = 'wheelhouse_dev'").strip()
    if found != "1":
        compose("exec", "-T", "database", "psql", "-U", PRODUCT, "-d", PRODUCT, "-c", "CREATE DATABASE wheelhouse_dev")
    path = private_dir(SECRETS / "dev") / "wheelhouse-console.json"
    if path.exists():
        return
    password = (SECRETS / "database-password").read_text().strip()
    private_file(path, json.dumps({
        "ConnectionStrings": {"Wheelhouse": "Host=rehearsal-database;Database=wheelhouse_dev;Username=" + PRODUCT
                                            + ";Password=" + password},
        "Identity": {"GitHub": {"ClientId": "set-a-github-oauth-app", "ClientSecret": "set-a-github-oauth-app"},
                     "AllowedGitHubLogins": [CONSOLE_ADMIN]},
        # Health probes call localhost; the operator arrives through the ingress on the private site's host.
        "AllowedHosts": "localhost;" + SELF_HOST,
    }, indent=2) + "\n")
    print("Wrote " + str(path) + "; add a GitHub OAuth app there to sign in")


# The self-deploy rehearsal's own runner root: the local server's fixtures and SSH identity, no other inventory.
SELF_ROOT = STATE / "self-deploy" / "root"


def self_release(tag, root=INVENTORY):
    """Wheelhouse's release bundle under `tag`, built from this checkout as its CI builds one (an archive built
    earlier is imported again), and imported under `root`."""
    bundle_id = SELF + "-" + re.sub(r"[^a-z0-9-]+", "-", tag.lower())
    if (root / "bundles" / bundle_id).exists():
        print("Already imported " + bundle_id)
        return bundle_id
    output = private_dir(STATE / "build") / bundle_id
    archive = STATE / "build" / (bundle_id + ".tar.gz")
    if not archive.is_file():
        commit = run("git", "-C", str(REPOSITORY), "rev-parse", "HEAD").strip()
        print(run(yaml_python(), str(RELEASE), "build", "--repo", str(REPOSITORY), "--commit", commit, "--checkout",
                  "--tag", tag, "--registry", REGISTRY + "/" + SELF, "--platform", platform(),
                  "--output", str(output), "--archive", str(archive)).strip())
        record_sets(output, archive)
    transport("import", "--archive", str(archive), "--bundle", bundle_id, root=root)
    print("Imported " + bundle_id)
    return bundle_id


def self_root():
    """The rehearsal's runner root, holding the local server's SSH identity and pin."""
    import shutil
    folder = private_dir(SELF_ROOT / "ssh" / "local")
    for name in ("identity", "known_hosts"):
        shutil.copyfile(INVENTORY / "ssh" / "local" / name, folder / name)
        (folder / name).chmod(0o600)
    return SELF_ROOT


def derived_bundle(source, release, broken=False):
    """A copy of a bundle under another release label; `broken` gives it a health check that always fails."""
    manifest = json.loads((source / "release.json").read_text())
    compose_file = json.loads((source / "compose.json").read_text())
    manifest["release"] = release
    if broken:
        for service in compose_file["services"].values():
            service["healthcheck"] = {"test": ["CMD", "false"], "interval": "2s", "timeout": "2s", "retries": 1,
                                      "start_period": "0s"}
    content = json.dumps(compose_file, indent=2).encode()
    import hashlib
    manifest["composeSha256"] = hashlib.sha256(content).hexdigest()
    return {"release.json": json.dumps(manifest, indent=2).encode(), "compose.json": content}


def on_target(*command, stdin=None, user="deploy", check=True):
    """A command on the local server's target container, as its deploy account unless `user` says otherwise."""
    result = subprocess.run(["docker", "compose", "-f", str(HERE / "compose.yml"), "exec", "-T", "-u", user, "target",
                             *command], input=stdin, capture_output=True, env=environment())
    if check and result.returncode:
        detail = (result.stderr or result.stdout or b"").decode("utf-8", "replace").strip().splitlines()[-1:]
        sys.exit("Failed on the target: " + " ".join(map(str, command[:3])) + (" — " + detail[0] if detail else ""))
    return result


def prove_self_deploy():
    """Wheelhouse's own release to wheelhouse-dev through the transport, then the kit's self-deploy.py on the target."""
    up()
    self_environment()
    root = self_root()
    first = self_release("v0.0.1-rig.1", root)
    outcome = deploy(first, "dev", target=SELF_TARGET, timeout=900, root=root)
    verification = outcome.get("verification") or {}
    if outcome.get("status") != "succeeded" or verification.get("status") != "passed":
        sys.exit("Wheelhouse's own dev rollout or its declared check did not pass: " + json.dumps(verification))
    print("Declared dev check: " + str(verification.get("summary")))
    # The console's database moves only in the rollout's migrate step; the console checks it at boot.
    migrated = [step for step in outcome.get("steps", []) if step.get("name") == "Migrate console"]
    if [step.get("status") for step in migrated] != ["succeeded"]:
        sys.exit("Wheelhouse's own migrate step did not run on wheelhouse-dev: " + json.dumps(migrated))
    print("Migrate step: " + str(migrated[0].get("detail")))

    # The kit as the control host holds it: self-deploy.py, the console's target file, and two more releases.
    source = root / "bundles" / first
    bundles = {"second": derived_bundle(source, "v0.0.1-rig.2"),
               "broken": derived_bundle(source, "v0.0.1-rig.3", broken=True)}
    on_target("sh", "-c", "umask 077; mkdir -p /srv/wheelhouse/bootstrap /srv/wheelhouse/config /srv/wheelhouse/rig-bundles")
    on_target("sh", "-c", "umask 077; cat > /srv/wheelhouse/bootstrap/self-deploy.py",
              stdin=(HERE.parent / "vps" / "self-deploy.py").read_bytes())
    for name, files in bundles.items():
        for filename, content in files.items():
            on_target("sh", "-c", "umask 077; mkdir -p /srv/wheelhouse/rig-bundles/" + name
                      + " && cat > /srv/wheelhouse/rig-bundles/" + name + "/" + filename, stdin=content)
    resolved = run(sys.executable, "-c", "import json, sys; sys.path.insert(0, sys.argv[1]); import fleet; "
                   "target = fleet.resolve_target(sys.argv[2], 'wheelhouse-dev')['target']; "
                   "[target.pop(key, None) for key in ('probeGate', 'rollbackCompatible')]; print(json.dumps(target))",
                   str(RUNNERS), str(root), env=environment())
    on_target("sh", "-c", "umask 077; cat > /srv/wheelhouse/config/wheelhouse-dev.json", stdin=resolved.encode())

    def cli(*arguments, expect=0):
        result = on_target("python3", "/srv/wheelhouse/bootstrap/self-deploy.py", *arguments, "--root", "/srv/wheelhouse",
                           check=False)
        text = (result.stdout + result.stderr).decode("utf-8", "replace").strip()
        print("$ self-deploy.py " + " ".join(arguments) + "\n" + "\n".join("  " + line for line in text.splitlines()))
        if result.returncode != expect:
            sys.exit("self-deploy.py " + arguments[0] + " exited " + str(result.returncode) + ", expected " + str(expect))
        return text

    def runs():
        reading = json.loads(cli("status", "--env", "dev", "--json"))["consoles"][0]
        return reading["condition"], (reading["current"] or {}).get("release")

    cli("status")
    cli("deploy", "--env", "dev", "--bundle", "/srv/wheelhouse/rig-bundles/second")
    if runs() != ("ready", "v0.0.1-rig.2"):
        sys.exit("self-deploy.py deploy did not leave the console on v0.0.1-rig.2: " + str(runs()))
    cli("rollback", "--env", "dev")
    if runs() != ("ready", "v0.0.1-rig.1"):
        sys.exit("self-deploy.py rollback did not return the console to v0.0.1-rig.1: " + str(runs()))
    broken = cli("deploy", "--env", "dev", "--bundle", "/srv/wheelhouse/rig-bundles/broken", expect=1)
    if '"status": "rolled_back"' not in broken or runs() != ("ready", "v0.0.1-rig.1"):
        sys.exit("A broken release did not roll back by itself: " + str(runs()))
    # It ran its migrate step, so the restore asked the previous release's own `migrate check` first.
    if "Migrate console" not in broken or "Rollback gate console" not in broken:
        sys.exit("The broken release's rollout skipped its migrate step or the restore's rollback gate")
    log = on_target("cat", "/srv/wheelhouse/self-deploy/log.jsonl").stdout.decode().splitlines()
    print("Local log: " + ", ".join(json.loads(line)["action"] + " " + json.loads(line)["release"] for line in log))
    print("Self-deploy proven on the rig: console path with its migrate step and dev check, then deploy, rollback and"
          " a broken release rolled back through the rollback gate, by the release image's own runner.")


HARDEN = STATE / "harden"
# What the fresh host stands in for: the control host's deploy account with CI's and a console's runner key, its
# `control` network (a docker stub answers for it), and a tailnet that is up (a tailscale stub says so).
HARDEN_SETUP = r"""
set -eu
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends ufw unattended-upgrades >/dev/null
id wheelhouse-deploy >/dev/null 2>&1 || useradd --create-home --shell /bin/bash wheelhouse-deploy
install -d -m 0700 -o wheelhouse-deploy -g wheelhouse-deploy /home/wheelhouse-deploy/.ssh
cat > /home/wheelhouse-deploy/.ssh/authorized_keys <<'KEYS'
restrict ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAICiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiK
restrict ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIConsoleRunnerKeyConsoleRunnerKeyConsoleRunnerK
KEYS
chown wheelhouse-deploy:wheelhouse-deploy /home/wheelhouse-deploy/.ssh/authorized_keys
chmod 0600 /home/wheelhouse-deploy/.ssh/authorized_keys
id ops >/dev/null 2>&1 || useradd --create-home --shell /bin/bash ops
install -d -m 0700 -o ops -g ops /home/ops/.ssh
cp /root/.ssh/authorized_keys /home/ops/.ssh/authorized_keys
chown ops:ops /home/ops/.ssh/authorized_keys
cat > /usr/local/bin/docker <<'STUB'
#!/bin/sh
[ "$1 $2 $3" = "network inspect control" ]
STUB
cat > /usr/local/bin/tailscale <<'STUB'
#!/bin/sh
echo '{"BackendState": "Running", "Self": {"Tags": ["tag:control"]}}'
STUB
chmod 755 /usr/local/bin/docker /usr/local/bin/tailscale
mkdir -p /root/kit
"""


def prove_harden():
    """harden-control.sh on the fresh Ubuntu host with real UFW and sshd, probed from the rig's public network and
    its tailnet."""
    import shutil
    compose("--profile", "enroll", "rm", "-sf", FRESH, capture=False)
    for volume in ("fresh-docker", "fresh-containerd"):
        subprocess.run(["docker", "volume", "rm", "-f", "wheelhouse-rehearsal_" + volume], capture_output=True)
    if HARDEN.exists():
        shutil.rmtree(HARDEN)
    private_dir(HARDEN)
    key = HARDEN / "id_ed25519"
    run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "owner@rig", "-f", str(key))
    private_dir(ENROLL_STATE)
    (ENROLL_STATE / "fresh-root-keys").write_text(Path(str(key) + ".pub").read_text())
    compose("--profile", "enroll", "up", "-d", "--build", FRESH, capture=False)
    deadline = time.monotonic() + 180
    while on_fresh("systemctl", "is-active", "ssh.socket", "ssh.service", check=False).returncode:
        if time.monotonic() > deadline:
            sys.exit("The fresh host's SSH never came up")
        time.sleep(2)
    on_fresh("bash", "-c", HARDEN_SETUP)
    for name in ("harden-control.sh", "bootstrap-products.sh"):
        on_fresh("sh", "-c", "cat > /root/kit/" + name, stdin=(HERE.parent / "vps" / name).read_bytes())

    def harden(*arguments, expect=0):
        result = on_fresh("bash", "/root/kit/harden-control.sh", *arguments, check=False)
        text = (result.stdout + result.stderr).decode("utf-8", "replace").strip()
        print("$ harden-control.sh " + " ".join(arguments) + "\n" + "\n".join("  " + line for line in text.splitlines()))
        if result.returncode != expect:
            sys.exit("harden-control.sh " + " ".join(arguments) + " exited " + str(result.returncode)
                     + ", expected " + str(expect))
        return text

    def added():
        return on_fresh("ufw", "show", "added").stdout.decode()

    def reaches(network, address):
        """Whether the owner's key signs in as ops at `address`, from a throwaway peer on `network`."""
        return subprocess.run(["docker", "run", "--rm", "--network", network, "-v", str(HARDEN) + ":/key:ro",
                               "--entrypoint", "ssh", "wheelhouse-rehearsal-fresh", "-i", "/key/id_ed25519",
                               "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=/dev/null",
                               "-o", "ConnectTimeout=6", "ops@" + address, "true"], capture_output=True).returncode == 0

    if "change  firewall: enable it" not in harden("--check", expect=1):
        sys.exit("--check did not report the firewall")
    harden()
    harden("--check")
    before = added()
    harden()
    if added() != before:
        sys.exit("A rerun changed the firewall")
    if not reaches("wheelhouse-rig-public", FRESH_PUBLIC):
        sys.exit("Public SSH closed before --tailnet-only-ssh")
    harden("--tailnet-only-ssh", expect=1)
    session = subprocess.run(["docker", "run", "-d", "--rm", "--network", "wheelhouse-rig-tailnet",
                              "-v", str(HARDEN) + ":/key:ro", "--entrypoint", "ssh", "wheelhouse-rehearsal-fresh",
                              "-i", "/key/id_ed25519", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=no",
                              "-o", "UserKnownHostsFile=/dev/null", "ops@" + FRESH_TAILNET, "sleep", "120"],
                             capture_output=True, text=True).stdout.strip()
    try:
        deadline = time.monotonic() + 30
        while "100.88." not in on_fresh("ss", "-Htn", "state", "established", "( sport = :22 )").stdout.decode():
            if time.monotonic() > deadline:
                sys.exit("The tailnet SSH session never opened")
            time.sleep(1)
        harden("--tailnet-only-ssh")
        harden("--check", "--tailnet-only-ssh")
        keys = on_fresh("cat", "/home/wheelhouse-deploy/.ssh/authorized_keys").stdout.decode()
        proofs = {"runner keys pinned to 172.30.1.0/24": keys.count('restrict,from="172.30.1.0/24" ') == 2,
                  "public SSH refused": not reaches("wheelhouse-rig-public", FRESH_PUBLIC),
                  "tailnet SSH accepted": reaches("wheelhouse-rig-tailnet", FRESH_TAILNET),
                  "a plain rerun keeps public SSH closed": "change" not in harden("--check")}
        harden("--public-ssh")
        proofs["--public-ssh reopens it"] = reaches("wheelhouse-rig-public", FRESH_PUBLIC)
    finally:
        subprocess.run(["docker", "rm", "-f", session], capture_output=True)
    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + name)
    compose("--profile", "enroll", "rm", "-sf", FRESH, capture=False)
    if not all(proofs.values()):
        sys.exit("harden-control.sh did not harden the host as it should")
    print("Control-host hardening proven on a real Ubuntu host: check, converge, close and reopen public SSH.")


def on_products(*command, user=None, stdin=None, check=True):
    """Runs a command on the local products host: as its administrator (root), or as `user`."""
    arguments = ["docker", "compose", "-f", str(HERE / "compose.yml"), "--profile", "products", "exec", "-T",
                 *(["-u", user] if user else []), "products", *command]
    result = subprocess.run(arguments, input=stdin, capture_output=True, text=True, env=environment())
    if check and result.returncode:
        detail = (result.stderr or result.stdout or "").strip().splitlines()[-1:]
        sys.exit("Failed on the products host: " + " ".join(command[:3]) + (" — " + detail[0] if detail else ""))
    return result


def as_deploy(script, stdin=None, check=True):
    return on_products("sh", "-c", script, user=DEPLOY_USER, stdin=stdin, check=check)


def products_address():
    """The products host's network on the rig and its address there: its public address, as a VPS's is on the
    internet."""
    networks = json.loads(run("docker", "inspect", "-f", "{{json .NetworkSettings.Networks}}",
                              "wheelhouse-rehearsal-products-1"))
    name, network = next(iter(sorted(networks.items())))
    return name, network["IPAddress"]


def baseline_hosts(address):
    """ForeverPin dev's baseline hosts on that address (decision 68): its first site, app, takes the product's name."""
    dashed = address.replace(".", "-")
    return {service: PRODUCT + ("" if site == "app" else "-" + site) + "-dev." + dashed + ".sslip.io"
            for service, site in SITES.items()}


def products_settings(bundle_id, credentials, hosts):
    """ForeverPin dev's settings on the products host: its own database, its site hosts and the ingress as its proxy."""
    values = {
        "DatabaseSettings": {"ConnectionString": credentials["ConnectionString"]},
        "ApiSettings": {"RedirectBaseUrl": "https://" + hosts["redirect"]},
        "Auth": {"Google": {"ClientId": "local.apps.googleusercontent.com"}},
        "Billing": {"SecretKey": "sk_test_local", "WebhookSecret": "whsec_local",
                    "Prices": {"Solo": "price_local_solo", "Pro": "price_local_pro", "Agency": "price_local_agency"},
                    "SuccessUrl": "https://" + hosts["management"] + "/billing/success",
                    "CancelUrl": "https://" + hosts["management"] + "/billing/cancel",
                    "PortalReturnUrl": "https://" + hosts["management"] + "/app/billing"},
        "Deployment": {"TrustedProxies": ["172.30.0.2"]},
    }

    def fill(template, source):
        return {key: fill(value, source.get(key, {})) if isinstance(value, dict) else source.get(key, value)
                for key, value in template.items()}

    folder = PRODUCTS_ROOT + "/config/" + PRODUCT + "-dev"
    for service, template in transport("template", "--bundle", bundle_id, root=PRODUCTS_INVENTORY).items():
        filled = fill(template, values)
        filled["AllowedHosts"] = hosts[service] + ";localhost"
        path = folder + "/" + service + ".json"
        # Read-only for the container's own user inside the deploy account's mode-0700 folder, as the runbook writes them.
        as_deploy("umask 077; f=" + path + "; [ ! -e $f ] || chmod 0600 $f; cat > $f; chmod 0444 $f",
                  stdin=json.dumps(filled, indent=2) + "\n")
    return {service: folder + "/" + service + ".json" for service in SITES}


def products_inventory(settings, address):
    """The inventory a prod console would export for this server and target once host upkeep recorded it: the tailnet
    address (here the forwarded SSH port), the deploy account, the kit's public ingress and the public address. The
    target names no site host, so the deploy fills each public site's baseline host. The server stands in for the
    Contabo VPS, so the runner renders settings into the host's own config/ folder, as on a VPS."""
    private_file(PRODUCTS_INVENTORY / "inventory.json", json.dumps({
        "version": 1, "products": [], "vaults": [],
        "servers": [{"id": PRODUCTS_SERVER, "name": "Local products host", "provider": "contabo", "host": "127.0.0.1",
                     "region": "local", "sshUser": DEPLOY_USER, "sshPort": PRODUCTS_PORT,
                     "ingress": {"scheme": "https", "port": None, "entryPoints": ["websecure"],
                                 "privateEntryPoints": ["private"], "certResolver": "letsencrypt", "pattern": None,
                                 "probe": "https://127.0.0.1", "privateProbe": "http://127.0.0.1:18080",
                                 "publicAddress": address}}],
        "targets": [{"id": PRODUCT + "-dev", "serverId": PRODUCTS_SERVER, "product": PRODUCT, "environment": "dev",
                     "network": "platform", "root": PRODUCTS_ROOT,
                     "settings": [{"service": service, "path": path} for service, path in settings.items()],
                     "smoke": [{"service": "management", "path": "/api/runtime-config", "status": 200},
                               {"service": "redirect", "path": "/health", "status": 200}],
                     "sites": []}],
    }, indent=2) + "\n")


def prove_runner_authorization():
    """authorize-runner.sh end to end inside the products host: a stand-in prod console creates its identity pinned to
    the tailnet address, the products host accepts it, and the console reaches the deploy account over SSH. Dev and a
    public address are refused."""
    script = PRODUCTS_ROOT + "/bootstrap/authorize-runner.sh"
    host_key = "/etc/ssh/ssh_host_ed25519_key.pub"
    console = "wheelhouse-prod-console-1"
    if on_products("docker", "inspect", console, check=False).returncode:
        on_products("docker", "run", "-d", "--name", console, "--restart", "unless-stopped", "alpine:3", "sh", "-c",
                    "apk add --no-cache openssh-client >/dev/null; exec sleep infinity")
    else:
        on_products("docker", "start", console)
    deadline = time.monotonic() + 120
    while on_products("docker", "exec", console, "which", "ssh-keygen", check=False).returncode:
        if time.monotonic() > deadline:
            sys.exit("The stand-in console never got an SSH client")
        time.sleep(2)
    remote = ["bash", script, "wheelhouse-prod", PRODUCTS_SERVER, "--remote", PRODUCTS_ADDRESS, "--host-key", host_key]
    for refused in (["bash", script, "wheelhouse-dev", PRODUCTS_SERVER, "--remote", PRODUCTS_ADDRESS, "--host-key",
                     host_key], remote[:5] + ["203.0.113.5"] + remote[6:]):
        if on_products(*refused, check=False).returncode == 0:
            sys.exit("authorize-runner.sh accepted " + " ".join(refused[2:6]))
    first = on_products(*remote, check=False)
    if first.stdout.startswith("ssh-ed25519 "):
        on_products("sh", "-c", "umask 077; cat > /tmp/prod-runner.pub", stdin=first.stdout)
        print(on_products("bash", script, "--accept", "/tmp/prod-runner.pub").stdout.strip())
    proof = on_products(*remote, check=False)
    if proof.returncode:
        sys.exit("The stand-in prod console does not reach the products host: " + proof.stderr.strip())
    print(proof.stderr.strip())


def prove_private_pull(image):
    """prepare.py's GHCR sign-in for real, against a local registry that demands a password: the deploy account signs
    in from a private token file, the token on stdin, and the runner's SSH session pulls with it while root cannot.
    GitHub's scope check is the one stand-in; the unit tests cover it."""
    registry, private = "wheelhouse-private-registry", "localhost:5001/rehearsal/private:latest"
    token = "ghp_" + secrets.token_hex(18)
    if on_products("docker", "inspect", registry, check=False).returncode:
        htpasswd = on_products("docker", "run", "--rm", "--entrypoint", "htpasswd", "httpd:2.4-alpine", "-Bbn",
                               "rehearsal", token).stdout
        on_products("sh", "-c", "umask 077; mkdir -p /root/private-registry; cat > /root/private-registry/htpasswd",
                    stdin=htpasswd)
        on_products("docker", "run", "-d", "--name", registry, "--restart", "unless-stopped", "-p", "127.0.0.1:5001:5000",
                    "-e", "REGISTRY_AUTH=htpasswd", "-e", "REGISTRY_AUTH_HTPASSWD_REALM=rehearsal",
                    "-e", "REGISTRY_AUTH_HTPASSWD_PATH=/auth/htpasswd", "-v", "/root/private-registry:/auth:ro",
                    "registry:2")
    else:
        on_products("docker", "start", registry)
        # A rerun signs in with a fresh token, so the registry learns it again.
        htpasswd = on_products("docker", "run", "--rm", "--entrypoint", "htpasswd", "httpd:2.4-alpine", "-Bbn",
                               "rehearsal", token).stdout
        on_products("sh", "-c", "umask 077; cat > /root/private-registry/htpasswd", stdin=htpasswd)
    token_file = PRODUCTS_ROOT + "/private/ghcr-token"
    as_deploy("umask 077; cat > " + token_file, stdin=token + "\n")
    signed = as_deploy("PYTHONDONTWRITEBYTECODE=1 python3 -c '"
                       "import pathlib, sys; sys.path.insert(0, \"" + PRODUCTS_ROOT + "/bootstrap\"); import prepare; "
                       "prepare.REGISTRY = \"localhost:5001\"; "
                       "prepare.ghcr_login(pathlib.Path(\"" + token_file + "\"), \"rehearsal\", lambda token: {\"read:packages\"})'"
                       " && shred -u " + token_file + " && stat -c %a ~/.docker/config.json", check=False)
    if signed.returncode or signed.stdout.strip() != "600" or token in signed.stdout + signed.stderr:
        sys.exit("The deploy account's registry sign-in failed: " + signed.stderr.strip()[-200:])
    deadline = time.monotonic() + 60
    while as_deploy("docker tag " + image + " " + private + " && docker push -q " + private, check=False).returncode:
        if time.monotonic() > deadline:
            sys.exit("The private registry never took the image")
        time.sleep(2)
    as_deploy("docker image rm " + private + " >/dev/null")
    if on_products("docker", "pull", "-q", private, check=False).returncode == 0:
        sys.exit("The private registry served an image without a sign-in")
    identity = PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER
    pulled = subprocess.run(["ssh", "-p", str(PRODUCTS_PORT), "-i", str(identity / "identity"), "-o", "BatchMode=yes",
                             "-o", "IdentitiesOnly=yes", "-o", "StrictHostKeyChecking=yes",
                             "-o", "UserKnownHostsFile=" + str(identity / "known_hosts"), DEPLOY_USER + "@127.0.0.1",
                             "docker pull -q " + private], capture_output=True, text=True)
    if pulled.returncode:
        sys.exit("The runner's SSH session could not pull the private image: " + pulled.stderr.strip()[-200:])
    print("Private image pulled over the runner's SSH session with the deploy account's token sign-in; root without "
          "one was refused.")


def products(bundle_id=None):
    """Proves the products-host kit on the local products host, then deploys ForeverPin dev to it."""
    for folder in (PRODUCTS_STATE, PRODUCTS_STATE / "keys", PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER,
                   PRODUCTS_INVENTORY / "bundles"):
        private_dir(folder)
    # The transport's identity for this server, standing in for prod console's; the products host accepts it below.
    identity = PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER / "identity"
    if not identity.exists():
        run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "wheelhouse-prod-runner", "-f", str(identity))
    (PRODUCTS_STATE / "keys" / "runner.pub").write_text(Path(str(identity) + ".pub").read_text())
    compose("--profile", "products", "up", "-d", "--build", "--wait", "products", capture=False)
    host_key = " ".join((PRODUCTS_STATE / "keys" / "ssh_host_ed25519_key.pub").read_text().split()[:2])
    private_file(PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER / "known_hosts",
                 "[127.0.0.1]:" + str(PRODUCTS_PORT) + " " + host_key + "\n")
    print(on_products("bash", PRODUCTS_ROOT + "/bootstrap/authorize-runner.sh", "--accept",
                      "/rehearsal-keys/runner.pub").stdout.strip())
    # bootstrap-products.sh's SSH policy: the server offers keys and nothing else.
    offered = subprocess.run(["ssh", "-p", str(PRODUCTS_PORT), "-o", "BatchMode=yes", "-o", "PubkeyAuthentication=no",
                              "-o", "UserKnownHostsFile=" + str(PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER / "known_hosts"),
                              "-o", "StrictHostKeyChecking=yes", DEPLOY_USER + "@127.0.0.1", "true"],
                             capture_output=True, text=True)
    if offered.returncode == 0 or "Permission denied (publickey)." not in offered.stderr:
        sys.exit("The products host offers more than key sign-in: " + offered.stderr.strip())
    print("SSH offers key sign-in only.")

    # The kit as this checkout has it, laid out again as host upkeep's root phase does, so a host made from an older
    # kit takes the public ingress; its ACME resolver points where nothing answers.
    on_products("bash", "-c", ". /kit/bootstrap-products.sh && lay_out_kit /kit")
    as_deploy("cd " + PRODUCTS_ROOT + "/platform && { grep -q '^WHEELHOUSE_ACME_CA=' host.env "
              "|| echo 'WHEELHOUSE_ACME_CA=" + PRODUCTS_ACME_CA + "' >> host.env; }")
    prepare = "python3 " + PRODUCTS_ROOT + "/bootstrap/prepare.py --server products"
    platform = "cd " + PRODUCTS_ROOT + "/platform && docker compose --env-file host.env -f compose.yml "
    print(as_deploy(prepare).stdout.strip())
    as_deploy(platform + "up -d --wait postgres ingress")
    credentials_path = PRODUCTS_ROOT + "/config/" + PRODUCT + "-dev/database.json"
    first = as_deploy("test -e " + credentials_path, check=False).returncode != 0
    print(as_deploy(prepare + " --product " + PRODUCT + (" --database" if first else "") + " --tailnet "
                    + PRODUCTS_TAILNET + "".join(" --site " + site for site in SITES.values())).stdout.strip())
    # The rendered file, site nodes included, must be valid Compose; the nodes start only on a real tailnet.
    as_deploy(platform + "config --quiet")
    if as_deploy(prepare + " --product " + PRODUCT + " --database", check=False).returncode == 0:
        sys.exit("prepare.py replaced existing database credentials")

    bundle_id = bundle_id or (latest_release(healthy=True) or sys.exit("Import a release first: rehearse.py bundle --tag ...")).name
    destination = PRODUCTS_INVENTORY / "bundles" / bundle_id
    if not destination.exists():
        private_dir(destination)
        for name in ("release.json", "compose.json"):
            (destination / name).write_bytes((INVENTORY / "bundles" / bundle_id / name).read_bytes())
    credentials = json.loads(as_deploy("cat " + credentials_path).stdout)
    network, address = products_address()
    hosts = baseline_hosts(address)
    settings = products_settings(bundle_id, credentials, hosts)
    products_inventory(settings, address)
    report = transport("check", "--target", PRODUCT + "-dev", "--bundle", bundle_id, root=PRODUCTS_INVENTORY)
    for check in report["checks"]:
        print("  " + ("ok" if check["ok"] else "FAILED").ljust(9) + " " + check["name"] + " — " + check["detail"])
    if not report["ok"]:
        sys.exit("The products host failed its check")
    outcome = deploy(bundle_id, "dev", root=PRODUCTS_INVENTORY)
    if outcome.get("status") != "succeeded":
        sys.exit("The deploy to the products host did not succeed")
    prove_public_ingress(network, address, hosts)
    prove_release_set(bundle_id, network, address, hosts)
    prove_runner_authorization()
    prove_private_pull(json.loads((destination / "release.json").read_text())["images"]["redirect"])
    print("Products host proven locally: layout, platform, databases, site nodes, runner key, deploy, public routes on "
          "baseline hosts, a three-repository release set without its unreleased repository, private image pull. "
          "Let's Encrypt itself cannot run here: no certificate was issued.")


def split_bundle(source, service, release):
    """One repository's bundle of a product built from several (decision 70): `source` cut down to `service`, its site,
    settings and image, under the repository's own release label."""
    import hashlib
    manifest = json.loads((source / "release.json").read_text())
    compose_file = json.loads((source / "compose.json").read_text())

    def keep(mapping):
        return {key: value for key, value in (mapping or {}).items() if key == service}

    compose_file["services"] = keep(compose_file["services"])
    used = {mount["source"] for mount in compose_file["services"][service].get("volumes") or []
            if isinstance(mount, dict) and mount.get("type") == "volume"}
    if compose_file.get("volumes"):
        compose_file["volumes"] = {name: value for name, value in compose_file["volumes"].items() if name in used}
        if not compose_file["volumes"]:
            del compose_file["volumes"]
    for key in ("images", "requiredConfiguration", "versions", "ports", "needs", "settings"):
        if key in manifest:
            manifest[key] = keep(manifest[key])
    if manifest.get("schemaVersion") == 2:
        manifest["routes"] = keep(manifest.get("routes"))
        served = {site for sites in manifest["routes"].values() for site in sites}
        manifest["sites"] = {site: role for site, role in manifest["sites"].items() if site in served}
    else:
        manifest["sites"] = keep(manifest.get("sites"))
    if isinstance(manifest.get("verify"), dict) and manifest["verify"].get("service") != service:
        del manifest["verify"]
    manifest["release"] = release
    content = json.dumps(compose_file, indent=2).encode()
    manifest["composeSha256"] = hashlib.sha256(content).hexdigest()
    return {"release.json": json.dumps(manifest, indent=2).encode(), "compose.json": content}


def three_repositories():
    """ForeverPin as the inventory would name it once built from three repositories (decision 70): `app` (the front door,
    management) and `go` (redirect), which have released, and `marketing`, whose deploy.yml names a service but which
    has not released yet."""
    def release(*services):
        return {"asset": PRODUCT + "-release.tar.gz", "workflow": "publish-docker-image.yml",
                "images": [{"service": service, "image": REGISTRY + "/" + PRODUCT + "/" + service} for service in services]}
    return {"slug": PRODUCT, "name": "ForeverPin", "description": "ForeverPin as three repositories.",
            "repository": "local-rig/" + PRODUCT + "-app", "defaultBranch": "main", "release": release("management"),
            "components": [{"repository": "local-rig/" + PRODUCT + "-go", "release": release("redirect")},
                           {"repository": "local-rig/" + PRODUCT + "-marketing", "release": release("marketing")}]}


def refused(*arguments, root):
    """A transport call that must be refused; returns its message."""
    result = subprocess.run([sys.executable, str(TRANSPORT), *arguments, "--root", str(root)], env=environment(),
                            capture_output=True, text=True)
    if result.returncode == 0:
        sys.exit("The transport accepted: " + " ".join(arguments[:3]))
    return (result.stderr or result.stdout).strip().splitlines()[-1]


def prove_release_set(bundle_id, network, address, hosts):
    """ForeverPin as a product of three repositories (decision 70), the third of which has not released: one bundle per
    released service, as each repository's publish workflow writes it, composed by the runner into a release set that
    leaves the third out and deployed over the single-repository release. The set is the one the two released
    repositories make on their own (same ID and label), and names the third as not in it yet. The services are the same
    images and settings, so nothing restarts; both baseline hosts keep answering."""
    import hashlib
    source = PRODUCTS_INVENTORY / "bundles" / bundle_id
    release = json.loads((source / "release.json").read_text())["release"]
    parts, identifiers = [], []
    for name, service in (("app", "management"), ("go", "redirect")):
        part_id = PRODUCT + "-" + name + "-" + re.sub(r"[^a-z0-9-]+", "-", release.lower())
        folder = PRODUCTS_INVENTORY / "bundles" / part_id
        if not folder.exists():
            private_dir(folder)
            for file, content in split_bundle(source, service, release).items():
                (folder / file).write_bytes(content)
        parts += ["--part", name + "=" + part_id]
        identifiers.append(part_id)
    two = transport("compose", "--product", PRODUCT, *parts, root=PRODUCTS_INVENTORY)
    snapshot = PRODUCTS_INVENTORY / "inventory.json"
    kept = snapshot.read_text()
    document = json.loads(kept)
    document["products"] = [three_repositories()]
    private_file(snapshot, json.dumps(document, indent=2) + "\n")
    try:
        made = transport("compose", "--product", PRODUCT, *parts, root=PRODUCTS_INVENTORY)
        front_door = refused("compose", "--product", PRODUCT, "--part", parts[3], root=PRODUCTS_INVENTORY)
        print("Composed " + made["id"] + " (" + made["release"] + ") from " + ", ".join(
            component["name"] + " " + component["release"] for component in made["components"])
            + "; not in it yet: " + ", ".join(item["name"] for item in made["notYet"]))
        outcome = deploy(made["id"], "dev", root=PRODUCTS_INVENTORY)
        if outcome.get("status") != "succeeded":
            sys.exit("The release set did not deploy to the products host")
        rollout = outcome.get("serviceRollout") or {}
        changes = {item["name"]: item["change"] for item in rollout.get("services") or []}
        state = transport("state", "--target", PRODUCT + "-dev", root=PRODUCTS_INVENTORY)["current"]
    finally:
        private_file(snapshot, kept)
    digest = hashlib.sha256("\n".join(identifiers).encode()).hexdigest()[:12]
    proofs = {
        "the set holds the two released repositories, in order": [item["name"] for item in made["components"]] == ["app", "go"],
        "the unreleased repository is named as not in it yet": [item["name"] for item in made["notYet"]] == ["marketing"],
        "its ID and label cover the released repositories only (" + made["id"] + ", " + made["release"] + ")":
            (made["id"], made["release"]) == (two["id"], two["release"])
            == (PRODUCT + "-set-" + digest, "app-" + release + "_go-" + release),
        "a set without the front door is refused (" + front_door + ")": "serves the product's front door" in front_door,
        "the target runs the release set": state.get("release") == made["release"],
        "the set restarted no service (" + ", ".join(name + " " + change for name, change in sorted(changes.items())) + ")":
            bool(changes) and set(changes.values()) == {"unchanged"},
        "the set's sites keep their names (" + ", ".join(sorted(made["sites"])) + ")": set(made["sites"]) == set(SITES.values()),
    }
    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + name)
    if not all(proofs.values()):
        sys.exit("The release set did not deploy as one product")
    prove_public_ingress(network, address, hosts)


def prove_public_ingress(network, address, hosts):
    """ForeverPin dev's public sites from another machine on the rig's network, as a visitor reaches a VPS: port 80
    redirects to https but keeps the HTTP-01 challenge path, 443 routes each baseline host (Traefik's default
    certificate, since no CA answers here), the private entry point is not reachable, the render handed each service its
    baseline host, and Traefik's letsencrypt resolver took an order for every baseline host."""
    def curl(*arguments):
        result = subprocess.run(["docker", "run", "--rm", "--network", network, CURL_IMAGE, "-s",
                                 "-o", "/dev/null", "--connect-timeout", "5", "--max-time", "15", *arguments],
                                capture_output=True, text=True)
        return result.returncode, result.stdout.strip()

    proofs = {}
    for service, host in hosts.items():
        path = "/api/runtime-config" if service == "management" else "/health"
        _, redirect = curl("-w", "%{http_code} %{redirect_url}", "--resolve", host + ":80:" + address,
                           "http://" + host + path)
        status, _, location = redirect.partition(" ")
        proofs["http://" + host + path + " redirects to https (" + status + ")"] = (
            status in ("301", "302", "307", "308") and location in ("https://" + host + path,
                                                                     "https://" + host + ":443" + path))
        _, secure = curl("-k", "-w", "%{http_code}", "--resolve", host + ":443:" + address, "https://" + host + path)
        proofs["https://" + host + path + " answers " + secure] = secure == "200"
    # Port 80 hands Let's Encrypt's HTTP-01 path to Traefik's challenge handler (an unknown token: 404), never the
    # redirect, so a real order can validate there.
    host = hosts["management"]
    _, challenge = curl("-w", "%{http_code} %{redirect_url}", "--resolve", host + ":80:" + address,
                        "http://" + host + "/.well-known/acme-challenge/rig-token")
    proofs["port 80 serves the HTTP-01 challenge path itself (" + challenge.strip() + ")"] = challenge.strip() == "404"
    for port in ("18080", "8080", "8081", "8443"):
        code, _ = curl("-w", "%{http_code}", "http://" + address + ":" + port + "/ping")
        proofs["port " + port + " is closed to other machines"] = code != 0
    rendered = json.loads(as_deploy("cat " + PRODUCTS_ROOT + "/config/" + PRODUCT + "-dev/management.target.json").stdout)
    proofs["the render's AllowedHosts names the baseline host"] = hosts["management"] in rendered["AllowedHosts"].split(";")
    proofs["the render's Sites:app:Url is the baseline host"] = (
        rendered.get("Sites", {}).get("app", {}).get("Url") == "https://" + hosts["management"])
    log = on_products("docker", "logs", "wheelhouse-products-ingress-1")
    lines = [line for line in (log.stdout + log.stderr).splitlines() if "letsencrypt" in line]
    proofs["the letsencrypt resolver took an order for each baseline host"] = all(
        any(host in line for line in lines) for host in hosts.values())
    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + name)
    if not all(proofs.values()):
        sys.exit("The products host's public ingress is not as the kit lays it out")


def prove_networks():
    """On the local products host: dynamic containers that start before Traefik never take its .2, and a network made
    before the reserved range gets it from repair-networks.sh with its fixed addresses kept, once."""
    compose("--profile", "products", "up", "-d", "--build", "--wait", "products", capture=False)
    as_deploy("python3 " + PRODUCTS_ROOT + "/bootstrap/prepare.py --server products")
    platform = "cd " + PRODUCTS_ROOT + "/platform && docker compose --env-file host.env -f compose.yml "

    def address(container, network):
        return as_deploy("docker inspect -f '{{(index .NetworkSettings.Networks \"" + network + "\").IPAddress}}' "
                         + container).stdout.strip()

    names = ["rig-dynamic-" + str(index) for index in range(3)]
    as_deploy(platform + "stop ingress >/dev/null 2>&1; docker rm -f " + " ".join(names) + " >/dev/null 2>&1; true")
    for name in names:
        as_deploy("docker run -d --name " + name + " --network platform alpine:3 sleep 600 >/dev/null")
    dynamic = [address(name, "platform") for name in names]
    as_deploy(platform + "up -d --force-recreate --wait ingress")
    ingress = address("wheelhouse-products-ingress-1", "platform")
    as_deploy("docker rm -f " + " ".join(names) + " >/dev/null")
    print("Dynamic containers started first took " + ", ".join(dynamic) + "; Traefik then took " + ingress + ".")
    if ingress != "172.30.0.2" or not all(item.startswith("172.30.0.") and int(item.rsplit(".", 1)[1]) >= 128
                                          for item in dynamic):
        sys.exit("A dynamic container could take a fixed platform address")

    legacy = ("rig-legacy", "172.30.9.0/24", "172.30.9.1", "172.30.9.128/25")
    repair = ". /kit/repair-networks.sh; repair_network " + " ".join(legacy)
    on_products("sh", "-c", "docker rm -f rig-fixed rig-dynamic >/dev/null 2>&1; docker network rm rig-legacy "
                "> /dev/null 2>&1; docker network create --subnet 172.30.9.0/24 --gateway 172.30.9.1 rig-legacy "
                ">/dev/null && docker run -d --name rig-fixed --network rig-legacy --ip 172.30.9.2 --network-alias "
                "edge alpine:3 sleep 600 >/dev/null && docker run -d --name rig-dynamic --network rig-legacy alpine:3 "
                "sleep 600 >/dev/null")
    before = on_products("docker", "inspect", "-f", "{{(index .NetworkSettings.Networks \"rig-legacy\").IPAddress}}",
                         "rig-dynamic").stdout.strip()
    first = on_products("bash", "-c", repair).stdout.strip()
    second = on_products("bash", "-c", repair).stdout.strip()
    config = json.loads(on_products("docker", "network", "inspect", "-f", "{{json .IPAM.Config}}", "rig-legacy").stdout)
    fixed = json.loads(on_products("docker", "inspect", "-f", "{{json (index .NetworkSettings.Networks \"rig-legacy\")}}",
                                   "rig-fixed").stdout)
    moved = on_products("docker", "inspect", "-f", "{{(index .NetworkSettings.Networks \"rig-legacy\").IPAddress}}",
                        "rig-dynamic").stdout.strip()
    on_products("sh", "-c", "docker rm -f rig-fixed rig-dynamic >/dev/null; docker network rm rig-legacy >/dev/null")
    print(first + "\n" + second)
    print("Repair kept rig-fixed at " + fixed["IPAddress"] + " (alias edge: " + str("edge" in (fixed.get("Aliases") or []))
          + ") and moved the dynamic container from " + before + " to " + moved + ".")
    if (config != [{"Subnet": "172.30.9.0/24", "IPRange": "172.30.9.128/25", "Gateway": "172.30.9.1"}]
            or fixed["IPAddress"] != "172.30.9.2" or "edge" not in (fixed.get("Aliases") or [])
            or int(moved.rsplit(".", 1)[1]) < 128 or "reserves" not in second):
        sys.exit("repair-networks.sh did not reserve the range with the fixed address kept")
    print("Networks proven: fixed addresses reserved at creation and by repair, idempotently.")


ENROLL_STATE = STATE / "enroll"
ENROLL_ROOT = ENROLL_STATE / "root"
FRESH = "fresh"
FRESH_PUBLIC = "172.31.250.10"
FRESH_TAILNET = "100.88.1.10"


def enroller(*arguments, script="infra.py", check=True, entrypoint=None):
    """One runner action as the console image runs it, on the rig's public and tailnet networks."""
    command = ["docker", "compose", "-f", str(HERE / "compose.yml"), "--profile", "enroll", "run", "--rm", "-T",
               "--no-deps", *(["--entrypoint", entrypoint] if entrypoint else []), "enroller"]
    command += list(arguments) if entrypoint else ["/app/runner/" + script, *arguments, "--root", "/data/deployments"]
    result = subprocess.run(command, capture_output=True, text=True, env=environment())
    if check and result.returncode:
        detail = (result.stderr or result.stdout).strip().splitlines()[-1:]
        sys.exit("Failed: " + script + " " + arguments[0] + (" — " + detail[0] if detail else ""))
    return json.loads(result.stdout) if check and not entrypoint else result


def on_fresh(*command, stdin=None, check=True):
    result = subprocess.run(["docker", "compose", "-f", str(HERE / "compose.yml"), "--profile", "enroll", "exec", "-T",
                             FRESH, *command], input=stdin, capture_output=True, env=environment())
    if check and result.returncode:
        detail = (result.stderr or result.stdout or b"").decode("utf-8", "replace").strip().splitlines()[-1:]
        sys.exit("Failed on the fresh host: " + " ".join(map(str, command[:3])) + (" — " + detail[0] if detail else ""))
    return result


def enroll_inventory(host):
    """What the console would export for the fresh server: a Contabo VPS reached as the deploy account."""
    private_file(ENROLL_ROOT / "inventory.json", json.dumps({
        "version": 1, "products": [], "vaults": [], "targets": [],
        "servers": [{"id": FRESH, "name": "Fresh Contabo VPS", "provider": "contabo", "host": host, "region": "eu",
                     "sshUser": DEPLOY_USER, "sshPort": 22,
                     "ingress": {"scheme": "https", "port": None, "entryPoints": ["private"],
                                 "privateEntryPoints": ["private"], "certResolver": None, "pattern": None,
                                 "probe": "http://127.0.0.1:18080", "privateProbe": "http://127.0.0.1:18080"}}],
    }, indent=2) + "\n")


def enroll_rig():
    """Enrolls a fresh host end to end, places ForeverPin on it and applies the tailnet policy after its review."""
    import shutil
    compose("--profile", "enroll", "rm", "-sf", FRESH, "fake-tailscale", capture=False)
    for volume in ("fresh-docker", "fresh-containerd"):
        subprocess.run(["docker", "volume", "rm", "-f", "wheelhouse-rehearsal_" + volume], capture_output=True)
    if ENROLL_STATE.exists():
        shutil.rmtree(ENROLL_STATE)
    for folder in (ENROLL_STATE, ENROLL_ROOT, ENROLL_ROOT / "providers" / "tailscale", ENROLL_ROOT / "rig-owner"):
        private_dir(folder)
    os.chmod(ENROLL_ROOT, 0o777)  # the console image's own user writes the runner root
    owner = ENROLL_ROOT / "rig-owner" / "id_ed25519"
    run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "owner@rig", "-f", str(owner))
    private_file(ENROLL_ROOT / "providers" / "tailscale" / "oauth-client.json", json.dumps(
        {"clientId": "kRigClientCNTRL", "clientSecret": "tskey-client-kRigClientCNTRL-" + secrets.token_hex(16)}))
    enroll_inventory(FRESH_PUBLIC)
    # The console image as it ships, its kit from the `kit` build context deploy.yml names.
    run("docker", "build", "-q", "-t", "wheelhouse:local", "-f", str(HERE.parent / "Dockerfile"),
        "--build-context", "kit=" + str(HERE.parent / "vps"), str(HERE.parents[1] / "codebase"))

    key = enroller("enrollment-key", "--server", FRESH)
    (ENROLL_STATE / "fresh-root-keys").write_text(key["publicKey"] + "\n")
    print("One-time key " + key["fingerprint"] + " placed on the fresh host's root, as at order time.")
    compose("--profile", "enroll", "up", "-d", "--build", "fake-tailscale", FRESH, capture=False)
    deadline = time.monotonic() + 180
    while on_fresh("systemctl", "is-active", "ssh.socket", "ssh.service", check=False).returncode:
        if time.monotonic() > deadline:
            sys.exit("The fresh host's SSH never came up")
        time.sleep(2)

    enroller("enroll", "--server", FRESH, "--address", FRESH_PUBLIC, "--login-user", "root", "--admin", "ops",
             "--admin-key", Path(str(owner) + ".pub").read_text().strip(), "--tailnet-hostname", "fresh-vps",
             "--actor", "rig", "--no-launch")
    result = enroller("enroll-run", "--server", FRESH)
    for step in result["steps"]:
        print("  " + step["status"].ljust(9) + " " + step["title"] + (" — " + step["detail"] if step["detail"] else ""))
    if result["status"] != "succeeded":
        sys.exit("The enrollment did not succeed: " + str(result.get("reason")))
    print("Minted: " + ", ".join(mint["keyId"] + " " + "+".join(mint["tags"]) for mint in result["mints"]))

    def owner_ssh(destination):
        return enroller("-F", "/dev/null", "-i", "/data/deployments/rig-owner/id_ed25519", "-o", "BatchMode=yes",
                        "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=/dev/null", "-o",
                        "ConnectTimeout=8", destination, "true", entrypoint="ssh", check=False).returncode
    proofs = {"owner's admin over the tailnet": owner_ssh("ops@" + FRESH_TAILNET) == 0,
              "root over the tailnet refused": owner_ssh("root@" + FRESH_TAILNET) != 0,
              "public SSH closed": owner_ssh("ops@" + FRESH_PUBLIC) != 0}
    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + name)
    if not all(proofs.values()):
        sys.exit("The enrolled host is not sealed as it should be")

    # What the console's follower does once the enrollment succeeds: the server moves to its tailnet address.
    enroll_inventory(FRESH_TAILNET)
    vitals = enroller("vitals", script="transport.py")
    reading = next(item for item in vitals["servers"] if item["serverId"] == FRESH)
    if not reading["ok"]:
        sys.exit("The runner cannot read the enrolled host: " + str(reading.get("reason")))
    print("The runner reads the enrolled host over the tailnet as " + DEPLOY_USER + ".")
    prove_placement_and_policy()
    print("Enrollment proven on the rig: fresh root-only host to sealed tailnet host, product placed, policy applied.")


def prove_placement_and_policy():
    """On the enrolled fresh host: ForeverPin's databases and site through the console's placement, then the tailnet
    policy reviewed, applied and reverted."""
    package = io.BytesIO()
    with tarfile.open(fileobj=package, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for name in ("Dockerfile", "site-node.py"):
            archive.add(str(HERE / "fresh" / "site-node" / name), arcname=name)
    on_fresh("docker", "build", "-q", "-t", "tailscale/tailscale:v1.104.1", "-", stdin=package.getvalue())
    # What the console passes from a deploy.yml: the rig declares ForeverPin's app site private, so it gets a node.
    job = enroller("place", "--server", FRESH, "--product", PRODUCT, "--database", "--site", "app=foreverpin-app",
                   "--declared-site", "app=private", "--needs-postgres", "--actor", "rig", "--no-launch")
    placed = enroller("place-run", "--job", job["id"])
    for step in placed["steps"]:
        print("  " + step["status"].ljust(9) + " " + step["title"] + (" — " + step["detail"] if step["detail"] else ""))
    if placed["status"] != "succeeded":
        sys.exit("The placement did not succeed: " + str(placed.get("reason")))
    for site in placed["facts"]["sites"]:
        print("  site " + site["node"] + " (" + site["environment"] + ") at " + str(site.get("tailnetAddress")))

    review = enroller("policy")
    if not (review["changed"] and review["validation"]["ok"] and review["guard"]["ok"]):
        sys.exit("The policy review is not applicable: " + json.dumps(review["validation"]))
    added = [line["text"].strip() for hunk in review["diff"] for line in hunk["lines"] if line["kind"] == "add"]
    print("Policy diff adds: " + "; ".join(added))
    enroller("policy-apply", "--etag", review["etag"], "--digest", review["renderedDigest"], "--actor", "rig")
    if enroller("policy")["changed"]:
        sys.exit("The applied policy does not match the rendered one")
    reverted = enroller("policy-revert", "--etag", enroller("policy")["etag"], "--actor", "rig")
    print("Policy applied after review, then reverted with one call (" + reverted["digest"][:12] + ").")


# Keysmith, the rig's product for generated and linked setting keys (`generated`, v0.11): its descriptor, image and app
# live in keysmith/. Its identity service has Wheelhouse make a random client secret and run a command for a certificate;
# its desk service links one key to that secret and another to the identity site's origin.
KEYSMITH = "keysmith"
KEYSMITH_TARGET = KEYSMITH + "-dev"
KEYSMITH_FOLDER = HERE / "keysmith"
KEYSMITH_IMAGE = "keysmith:local"
KEYSMITH_SERVICES = ("identity", "desk")
RANDOM_KEY = "Identity:Clients:0:ClientSecret"
COMMAND_KEY = "Identity:Signing:Certificate"
LINKED_KEY = "Auth:ClientSecret"
SITE_KEY = "Auth:Issuer"
KEYSMITH_SITE = "id"
# The rig's owner session (session/): built with the SDK the console image builds with, sealed with the console's own
# Data Protection key ring.
SESSION_SOURCE = HERE / "session"
SESSION_STATE = STATE / "session"
SDK_IMAGE = "mcr.microsoft.com/dotnet/sdk:10.0.302"
CONSOLE_KEYS = "wheelhouse-rehearsal_console-keys"
CONSOLE_CONTAINER = "wheelhouse-rehearsal-console-1"
SESSION_COOKIE = ".wheelhouse.auth"


def console_session(minutes=60):
    """A sign-in cookie for the rig console as its allowlisted owner, so a rig action drives the console's own API the
    way the operator's browser does, with no GitHub OAuth app. session/Program.cs seals it with the console's key ring
    (the console-keys volume); only this rig's console opens it."""
    import hashlib
    sources = sorted(path for path in SESSION_SOURCE.iterdir() if path.suffix in (".cs", ".csproj"))
    folder = "bin-" + hashlib.sha256(b"".join(path.name.encode() + path.read_bytes() for path in sources)).hexdigest()[:12]
    private_dir(SESSION_STATE)
    if not (SESSION_STATE / folder / "Session.dll").is_file():
        run("docker", "run", "--rm", "-v", str(SESSION_SOURCE) + ":/src:ro", "-v", str(SESSION_STATE) + ":/out",
            "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_NOLOGO=1", SDK_IMAGE, "sh", "-c",
            "cp -r /src /tmp/session && dotnet publish /tmp/session/Session.csproj -c Release -nologo -v q -o /out/"
            + folder)
    allowed = json.loads(CONSOLE_SETTINGS.read_text()).get("Identity", {}).get("AllowedGitHubLogins") or [CONSOLE_ADMIN]
    return run("docker", "run", "--rm", "-v", CONSOLE_KEYS + ":/data/keys", "-v", str(SESSION_STATE / folder)
               + ":/session:ro", "--entrypoint", "dotnet", "wheelhouse:local", "/session/Session.dll", "/data/keys",
               allowed[0], str(minutes)).strip()


class ConsoleApi:
    """The rig console's HTTP API as its signed-in owner. Every answer's text is kept, so a run can show that none of
    them holds a generated value."""

    def __init__(self, cookie):
        self.cookie, self.answers = cookie, []

    def call(self, method, path, body=None, action=None, ok=(200, 201, 202, 204)):
        import urllib.error
        import urllib.request
        request = urllib.request.Request(CONSOLE_URL + path, method=method,
                                         data=None if body is None else json.dumps(body).encode())
        request.add_header("Cookie", SESSION_COOKIE + "=" + self.cookie)
        request.add_header("Accept", "application/json")
        if body is not None:
            request.add_header("Content-Type", "application/json")
        if action:
            request.add_header("X-Wheelhouse-Action", action)
        try:
            with urllib.request.urlopen(request, timeout=900) as response:
                status, text = response.status, response.read().decode()
        except urllib.error.HTTPError as error:
            status, text = error.code, error.read().decode()
        self.answers.append(method + " " + path + "\n" + text)
        try:
            document = json.loads(text) if text.strip() else None
        except ValueError:
            document = text
        if ok and status not in ok:
            detail = document.get("detail") or document.get("title") if isinstance(document, dict) else str(document)
            sys.exit(method + " " + path + " answered " + str(status) + ": " + str(detail)[:300])
        return status, document

    def data(self, method, path, body=None, action=None):
        return (self.call(method, path, body, action)[1] or {}).get("data")


def console_ready(timeout=180):
    import urllib.request
    deadline = time.monotonic() + timeout
    while True:
        try:
            with urllib.request.urlopen(CONSOLE_URL + "/api/system/ready", timeout=5) as response:
                if response.status == 200:
                    return
        except OSError:
            pass
        if time.monotonic() > deadline:
            sys.exit("The console never became ready at " + CONSOLE_URL)
        time.sleep(2)


def keysmith_bundle():
    """Keysmith's release: the pipelines generator builds it from keysmith/deploy.yml with the image beside it, publishes
    the image to the rig's registry for both services and writes the bundle, which is imported. Its tag carries a digest
    of the descriptor and the image, so a change makes a new release and a rerun reuses the last one."""
    import hashlib
    run("docker", "build", "-q", "-t", KEYSMITH_IMAGE, str(KEYSMITH_FOLDER))
    image = run("docker", "image", "inspect", "--format", "{{.Id}}", KEYSMITH_IMAGE).strip()
    descriptor = KEYSMITH_FOLDER / "deploy.yml"
    tag = "v0.0.1-rig." + hashlib.sha256(descriptor.read_bytes() + image.encode()).hexdigest()[:10]
    bundle_id = KEYSMITH + "-" + re.sub(r"[^a-z0-9-]+", "-", tag.lower())
    if (INVENTORY / "bundles" / bundle_id).exists():
        print("Already imported " + bundle_id)
        return bundle_id
    output = private_dir(STATE / "build") / bundle_id
    archive = STATE / "build" / (bundle_id + ".tar.gz")
    # The descriptor is read from disk; the checkout only lends the generator a commit to name.
    commit = run("git", "-C", str(REPOSITORY), "rev-parse", "HEAD").strip()
    print(run(yaml_python(), str(RELEASE), "build", "--repo", str(REPOSITORY), "--commit", commit,
              "--descriptor", str(descriptor), "--tag", tag, "--registry", REGISTRY + "/" + KEYSMITH,
              "--platform", platform(), "--output", str(output), "--archive", str(archive),
              *[part for service in KEYSMITH_SERVICES for part in ("--image", service + "=" + KEYSMITH_IMAGE)]).strip())
    transport("import", "--archive", str(archive), "--bundle", bundle_id)
    print("Imported " + bundle_id)
    return bundle_id


def keysmith_inventory(console):
    """Keysmith's product and dev target, added through the console as the owner adds them, afresh on every run: a new
    product row is a new vault namespace, so the first deploy finds an environment that holds no value."""
    if any(item["slug"] == KEYSMITH_TARGET for item in console.data("GET", "/api/targets?product=" + KEYSMITH) or []):
        console.call("DELETE", "/api/targets/" + KEYSMITH_TARGET, action="target")
    if console.call("GET", "/api/products/" + KEYSMITH, ok=None)[0] == 200:
        console.call("DELETE", "/api/products/" + KEYSMITH, action="product")
    console.call("POST", "/api/products", {"slug": KEYSMITH, "name": "Keysmith",
                                           "description": "A rig product whose keys Wheelhouse makes and links itself.",
                                           "repository": "local-rig/" + KEYSMITH}, action="product")
    console.call("POST", "/api/targets", {
        "slug": KEYSMITH_TARGET, "product": KEYSMITH, "server": "local", "environment": "dev",
        "network": "wheelhouse-rehearsal", "root": "/srv/wheelhouse", "settings": [], "sites": [],
        "smokeChecks": [{"service": service, "path": "/", "status": 200} for service in KEYSMITH_SERVICES]},
        action="target")
    print("Added " + KEYSMITH + " and " + KEYSMITH_TARGET + " through the console")


class GeneratedValues:
    """The generated values a run reads from the rendered settings, kept to look for elsewhere and never printed: each
    value, each line of a PEM value, and their base64 forms at every alignment (how a value would sit inside an encoded
    document)."""

    def __init__(self):
        self.fragments = set()

    def add(self, value):
        parts = [value] + [line for line in value.splitlines() if len(line) >= 16 and not line.startswith("-----")]
        for part in parts:
            self.fragments.add(part)
            if len(part) >= 24:
                raw = part.encode()
                for offset in range(3):
                    encoded = base64.b64encode(b"\0" * offset + raw).decode()
                    self.fragments.add(encoded[-(-offset * 4 // 3):(offset + len(raw)) * 8 // 6])

    def found(self, data):
        text = data.decode("utf-8", "replace") if isinstance(data, bytes) else data
        return any(fragment in text for fragment in self.fragments)

    def redact(self, text):
        for fragment in sorted(self.fragments, key=len, reverse=True):
            text = text.replace(fragment, "[generated value]")
        return text


def rendered(service):
    """A Keysmith service's settings file as the deploy rendered it on the target."""
    path = STATE / "settings" / KEYSMITH_TARGET / (service + ".target.json")
    return json.loads(on_target("cat", str(path)).stdout)


def setting(document, key):
    for part in key.split(":"):
        document = document.get(part) if isinstance(document, dict) else None
    return document


def console_deploy(console, bundle_id, label, timeout=600):
    """A deploy as the console's Deploy button submits it; waits for the target's outcome."""
    job = console.data("POST", "/api/deployments", {"target": KEYSMITH_TARGET, "release": bundle_id}, action="deploy")["id"]
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        time.sleep(3)
        outcome = console.data("GET", "/api/deployments/" + job)
        if outcome.get("status") in TERMINAL:
            print(label + ": " + outcome["status"] + " (" + job + ")")
            return job, outcome
    sys.exit("Timed out waiting for " + job)


def print_steps(outcome, values):
    for step in outcome.get("steps", []):
        print(values.redact("  " + step["status"].ljust(9) + " " + step["name"]
                            + (" — " + step["detail"] if step.get("detail") else "")))


def certificate_end(value):
    """The first certificate's notAfter in a PEM bundle, read by openssl in Keysmith's own image with no network."""
    from datetime import datetime, timezone
    block = value[value.index("-----BEGIN CERTIFICATE-----"):value.index("-----END CERTIFICATE-----") + 25] + "\n"
    answer = subprocess.run(["docker", "run", "--rm", "-i", "--network", "none", "--entrypoint", "openssl",
                             KEYSMITH_IMAGE, "x509", "-noout", "-enddate"], input=block, capture_output=True, text=True)
    if answer.returncode or not answer.stdout.startswith("notAfter="):
        sys.exit("openssl could not read the generated certificate's end date")
    stamp = " ".join(answer.stdout.strip()[len("notAfter="):].split())
    return datetime.strptime(stamp, "%b %d %H:%M:%S %Y GMT").replace(tzinfo=timezone.utc)


def instant(text):
    from datetime import datetime
    return datetime.fromisoformat(text.replace("Z", "+00:00")) if text else None


def prove_generated():
    """v0.11's generated and linked setting keys end to end, through the console's own API as a real deploy goes:
    Keysmith's first deploy generates its random and command keys without asking for a value, the render gives each
    service its values and links, the certificate's end date becomes its version's expiry, no answer, log, journal or
    record outside the rendered files holds a generated value, Regenerate makes a version the next deploy renders into
    both services, and the command container had no network. Exits non-zero when any check fails."""
    from datetime import datetime, timezone
    up(console=True)
    console_ready()
    started = datetime.now(timezone.utc)
    console = ConsoleApi(console_session())
    bundle_id = keysmith_bundle()
    keysmith_inventory(console)
    values, proofs = GeneratedValues(), {}
    settings_path = "/api/deployments/targets/" + KEYSMITH_TARGET + "/settings"

    def keys(release=None):
        plan = console.data("GET", settings_path + ("?release=" + release if release else ""))
        return {(service["name"], item["key"]): item for service in plan["services"] for item in service["keys"]}

    def versions(key):
        return console.data("GET", "/api/deployments/targets/" + KEYSMITH_TARGET + "/secrets/" + key + "/versions")

    # 1. The plan names both keys as generated, not missing, and the first deploy makes them with no value given.
    planned = keys(bundle_id)
    for key in (RANDOM_KEY, COMMAND_KEY):
        item = planned.get(("identity", key)) or {}
        proofs["plan: " + key + " is generated (" + str(item.get("source")) + ")"] = item.get("source") == "generate"
    for key, source in ((LINKED_KEY, RANDOM_KEY), (SITE_KEY, "Sites:" + KEYSMITH_SITE + ":Url")):
        item = planned.get(("desk", key)) or {}
        proofs["plan: desk " + key + " links " + source + " (" + str(item.get("source")) + ")"] = (
            item.get("from") == source and item.get("source") == "linked")
    events_file = STATE / "build" / "keysmith-events.jsonl"
    with open(events_file, "w") as sink:
        watcher = subprocess.Popen(["docker", "events", "--format", "{{json .}}", "--filter", "type=container",
                                    "--filter", "type=network"], stdout=sink, stderr=subprocess.DEVNULL)
        try:
            first_job, first = console_deploy(console, bundle_id, "First deploy")
        finally:
            watcher.terminate()
            watcher.wait(10)
    identity, desk = rendered("identity"), rendered("desk")
    random_value, certificate = setting(identity, RANDOM_KEY), setting(identity, COMMAND_KEY)
    for value in (random_value, certificate):
        if isinstance(value, str) and value:
            values.add(value)
    print_steps(first, values)
    proofs["first deploy succeeded with no value given"] = first.get("status") == "succeeded"
    histories = {key: versions(key) for key in (RANDOM_KEY, COMMAND_KEY)}
    for key, history in histories.items():
        made = history["versions"]
        proofs["vault: " + key + " v1 made by the deploy (" + str(made[0].get("origin") if made else None) + ")"] = (
            len(made) == 1 and made[0]["version"] == 1 and "generated by the deploy" in str(made[0].get("origin")))

    # 2. The rendered files hold the values; desk's links equal their sources.
    try:
        random_bytes = len(base64.urlsafe_b64decode(random_value + "=" * (-len(random_value) % 4)))
    except (TypeError, ValueError):
        random_bytes = 0
    proofs["identity renders " + RANDOM_KEY + ": 32 random bytes, base64url"] = random_bytes == 32
    proofs["identity renders " + COMMAND_KEY + ": a certificate and its private key"] = (
        isinstance(certificate, str) and "-----BEGIN CERTIFICATE-----" in certificate and "PRIVATE KEY-----" in certificate)
    proofs["desk renders " + LINKED_KEY + " equal to identity's " + RANDOM_KEY] = (
        bool(random_value) and setting(desk, LINKED_KEY) == random_value)
    state = console.data("GET", "/api/deployments/targets/" + KEYSMITH_TARGET + "/state")
    site = next((item for item in (state.get("current") or {}).get("sites", []) if item.get("name") == KEYSMITH_SITE), {})
    parts = re.match(r"(https?://[^/]+)", str(site.get("url")))
    origin = parts.group(1) if parts else None
    proofs["desk renders " + SITE_KEY + " = the " + KEYSMITH_SITE + " site's origin (" + str(origin) + ")"] = (
        origin is not None and setting(desk, SITE_KEY) == origin == setting(desk, "Sites:" + KEYSMITH_SITE + ":Url"))

    # 3. The certificate's end date is its version's expiry.
    end = certificate_end(certificate) if proofs[
        "identity renders " + COMMAND_KEY + ": a certificate and its private key"] else None
    expiry = instant(histories[COMMAND_KEY].get("expiresAt"))
    proofs["vault: " + COMMAND_KEY + " expires at the certificate's notAfter (" + str(end) + " / " + str(expiry) + ")"] = (
        end is not None and expiry == end)

    # 6. The command ran in a container on no network: Docker's own events, and a control run of the same command on a
    # network, which refuses.
    created, networks = {}, {}
    for line in events_file.read_text().splitlines():
        event = json.loads(line)
        actor = event.get("Actor") or {}
        attributes = actor.get("Attributes") or {}
        if event.get("Type") == "container" and event.get("Action") == "create" \
                and attributes.get("wheelhouse.generate") == KEYSMITH_TARGET:
            created[actor.get("ID")] = attributes.get("name")
        elif event.get("Type") == "network" and event.get("Action") == "connect":
            networks.setdefault(attributes.get("container"), set()).add(attributes.get("name"))
    joined = sorted({name for container in created for name in networks.get(container, set())})
    proofs["generate container" + ("s " if len(created) != 1 else " ") + ", ".join(sorted(created.values()))
           + " joined only network none (" + ", ".join(joined) + ")"] = bool(created) and joined == ["none"]
    control = subprocess.run(["docker", "run", "--rm", "--network", "wheelhouse-rehearsal", "--cap-drop", "ALL",
                              "--entrypoint", "python3", KEYSMITH_IMAGE, "/app/keysmith.py", "certificate"],
                             capture_output=True, text=True)
    proofs["control: the command on a network refuses (exit " + str(control.returncode) + ")"] = (
        control.returncode == 3 and not control.stdout and "has a network" in control.stderr)

    # 5. Regenerate makes a new version; the next deploy renders it into both services and keeps the command key.
    written = console.data("POST", "/api/deployments/targets/" + KEYSMITH_TARGET + "/secrets/" + RANDOM_KEY
                           + "/regenerate", {"note": "rig: regenerate"}, action="settings")
    after = versions(RANDOM_KEY)
    proofs["regenerate: " + RANDOM_KEY + " v" + str(written.get("version")) + ", earlier version kept"] = (
        written.get("version") == 2 and [item["version"] for item in after["versions"]] == [2, 1]
        and after["versions"][0].get("current") is True)
    _, second = console_deploy(console, bundle_id, "Deploy after Regenerate")
    identity_after, desk_after = rendered("identity"), rendered("desk")
    regenerated = setting(identity_after, RANDOM_KEY)
    if isinstance(regenerated, str) and regenerated:
        values.add(regenerated)
    print_steps(second, values)
    proofs["the deploy after Regenerate succeeded"] = second.get("status") == "succeeded"
    proofs["identity renders the new " + RANDOM_KEY] = bool(regenerated) and regenerated != random_value
    proofs["desk renders the new value through its link"] = bool(regenerated) and setting(desk_after, LINKED_KEY) == regenerated
    proofs["the deploy kept the stored " + COMMAND_KEY + " (one version, same value)"] = (
        setting(identity_after, COMMAND_KEY) == certificate and len(versions(COMMAND_KEY)["versions"]) == 1)

    # The audit names the generation and the regeneration, by key and version.
    audit = console.data("GET", "/api/audit?pageSize=200") or {}
    entries = [entry for entry in (audit.get("items") or audit.get("entries") or []) if KEYSMITH in json.dumps(entry)]
    actions = [entry.get("action") for entry in entries]
    proofs["audit: settings.generate and the regeneration recorded (" + ", ".join(sorted(set(map(str, actions)))) + ")"] = (
        "settings.generate" in actions and any("regenerate" in str(action) for action in actions))

    # 4. No generated value outside the rendered files: every console answer, deploy logs and history, the audit, the
    # console's log and database, and the runner's journals and history on both sides.
    console.data("GET", "/api/deployments/" + first_job + "/detail")
    console.call("GET", "/api/deployments/history?limit=50")
    console.call("GET", settings_path)
    for service in KEYSMITH_SERVICES:
        console.call("GET", "/api/deployments/targets/" + KEYSMITH_TARGET + "/services/" + service + "/logs?tail=200")
    for key in (RANDOM_KEY, COMMAND_KEY):
        versions(key)
    sources = {"console API answers (" + str(len(console.answers)) + ")": "\n".join(console.answers)}
    logs = subprocess.run(["docker", "logs", "--since", started.strftime("%Y-%m-%dT%H:%M:%SZ"), CONSOLE_CONTAINER],
                          capture_output=True)
    sources["console log"] = logs.stdout + logs.stderr
    sources["console database dump"] = compose("--profile", "console", "exec", "-T", "console-db", "pg_dump",
                                               "-U", "wheelhouse", "wheelhouse")
    held = [path for path in INVENTORY.rglob("*") if path.is_file()]
    sources["console runner root (" + str(len(held)) + " files)"] = None
    sources["target runner root /srv/wheelhouse"] = on_target("tar", "-C", "/srv/wheelhouse", "-cf", "-", ".").stdout
    leaks = {}
    for name, data in sources.items():
        if data is None:
            hits = [str(path.relative_to(STATE)) for path in held if values.found(path.read_bytes())]
        else:
            hits = [name] if values.found(data) else []
        leaks[name] = hits
        proofs["no generated value in the " + name + ("" if not hits else ": " + ", ".join(hits[:5]))] = not hits
    # The search itself finds them where they belong.
    proofs["control: the search finds the values in the rendered files"] = values.found(
        json.dumps(identity_after)) and values.found(json.dumps(desk_after))

    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + values.redact(name))
    if not all(proofs.values()):
        sys.exit("Generated and linked keys did not hold on the rig")
    print("Generated and linked keys proven on the rig through the console: generated at the first deploy, linked, the "
          "certificate's expiry kept, no value outside the rendered files, regenerated into both services, no network.")


# Ledger, the rig's product for a set reset (`set-reset`): its descriptor and identity image live in ledger/. Its
# identity service hosts WoW2 Identity's `identity` migration set in Ledger's database on the products host; its desk
# runs Keysmith's image, and the proof keeps a table of the desk's in the same database.
LEDGER = "ledger"
LEDGER_TARGET = LEDGER + "-dev"
LEDGER_FOLDER = HERE / "ledger"
LEDGER_SET = "identity"
LEDGER_IMAGES = {"identity": "ledger-identity:local", "desk": KEYSMITH_IMAGE}
LEDGER_DATABASE = LEDGER + "_dev"
# WoW2's Identity image, built by its own repository (wow-two-sdk-beta.identity, deployment.md § Local run).
IDENTITY_IMAGE = "identity:local"
PRODUCTS_PLATFORM = "cd " + PRODUCTS_ROOT + "/platform && docker compose --env-file host.env -f compose.yml "


def ledger_bundle():
    """Ledger's release: the pipelines generator builds it from ledger/deploy.yml with both images, publishes them to the
    rig's registry, records the identity image's sets offline as CI does, and the products host's runner root imports
    it. Its tag carries a digest of the descriptor and the images, so a rerun reuses the last one."""
    import hashlib
    if subprocess.run(["docker", "image", "inspect", IDENTITY_IMAGE], capture_output=True).returncode:
        sys.exit("Build WoW2's Identity image first as " + IDENTITY_IMAGE + ": wow-two-sdk-beta.identity, "
                 "engineering/deployment/deployment.md § Local run")
    run("docker", "build", "-q", "-t", KEYSMITH_IMAGE, str(KEYSMITH_FOLDER))
    run("docker", "build", "-q", "-t", LEDGER_IMAGES["identity"], str(LEDGER_FOLDER))
    ids = [run("docker", "image", "inspect", "--format", "{{.Id}}", image).strip()
           for image in sorted(set(LEDGER_IMAGES.values()))]
    descriptor = LEDGER_FOLDER / "deploy.yml"
    tag = "v0.0.1-rig." + hashlib.sha256(descriptor.read_bytes() + "".join(ids).encode()).hexdigest()[:10]
    bundle_id = LEDGER + "-" + re.sub(r"[^a-z0-9-]+", "-", tag.lower())
    if (PRODUCTS_INVENTORY / "bundles" / bundle_id).exists():
        print("Already imported " + bundle_id)
        return bundle_id
    output = private_dir(STATE / "build") / bundle_id
    archive = STATE / "build" / (bundle_id + ".tar.gz")
    commit = run("git", "-C", str(REPOSITORY), "rev-parse", "HEAD").strip()
    print(run(yaml_python(), str(RELEASE), "build", "--repo", str(REPOSITORY), "--commit", commit,
              "--descriptor", str(descriptor), "--tag", tag, "--registry", REGISTRY + "/" + LEDGER,
              "--platform", platform(), "--output", str(output), "--archive", str(archive),
              *[part for service, image in LEDGER_IMAGES.items() for part in ("--image", service + "=" + image)]).strip())
    record_sets(output, archive)
    transport("import", "--archive", str(archive), "--bundle", bundle_id, root=PRODUCTS_INVENTORY)
    print("Imported " + bundle_id)
    return bundle_id


def ledger_inventory():
    """Ledger and its dev target beside ForeverPin's in the products host's inventory, as a prod console would export
    them: no release source (its bundles are imported), and no settings files, sites or smoke checks; its database is
    the one placement wrote."""
    path = PRODUCTS_INVENTORY / "inventory.json"
    if not path.is_file():
        sys.exit("Prove the products host first: rehearse.py products")
    inventory = json.loads(path.read_text())
    inventory["products"] = [item for item in inventory["products"] if item["slug"] != LEDGER] + [{
        "slug": LEDGER, "name": "Ledger", "description": "A rig product whose identity migration set Wheelhouse resets "
        "alone.", "repository": "local-rig/" + LEDGER, "defaultBranch": "main", "release": None}]
    inventory["targets"] = [item for item in inventory["targets"] if item["id"] != LEDGER_TARGET] + [{
        "id": LEDGER_TARGET, "serverId": PRODUCTS_SERVER, "product": LEDGER, "environment": "dev", "network": "platform",
        "root": PRODUCTS_ROOT, "settings": [], "smoke": [], "sites": []}]
    private_file(path, json.dumps(inventory, indent=2) + "\n")


def ledger_database():
    """Ledger's dev database on the products host, placed as `prepare.py --database` places one: its own role owning only
    it, and the owner-only database.json the runner fills the migrate key from. By hand, so a products host still on
    PostgreSQL 16, which prepare.py refuses until its move, takes it too. An existing placement stays."""
    path = PRODUCTS_ROOT + "/config/" + LEDGER_TARGET + "/database.json"
    if as_deploy("test -e " + path, check=False).returncode == 0:
        return
    password = secrets.token_hex(32)
    as_deploy(PRODUCTS_PLATFORM + "exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 -q",
              stdin="CREATE ROLE " + LEDGER_DATABASE + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '" + password
              + "';\nCREATE DATABASE " + LEDGER_DATABASE + " OWNER " + LEDGER_DATABASE + ";\n")
    connection = ("Host=wheelhouse-postgres;Port=5432;Database=" + LEDGER_DATABASE + ";Username=" + LEDGER_DATABASE
                  + ";Password=" + password)
    as_deploy("umask 077; mkdir -p " + PRODUCTS_ROOT + "/config/" + LEDGER_TARGET + " && cat > " + path,
              stdin=json.dumps({"Host": "wheelhouse-postgres", "Port": 5432, "Database": LEDGER_DATABASE,
                                "Username": LEDGER_DATABASE, "Password": password, "ConnectionString": connection},
                               indent=2) + "\n")
    print("Placed " + LEDGER_DATABASE + " on the products host")


def ledger_sql(sql, role=None):
    """SQL in Ledger's dev database through the products host's platform PostgreSQL, as its superuser or as `role`: the
    rows, one a line."""
    script = ("SET ROLE " + role + ";\n" if role else "") + sql
    return as_deploy(PRODUCTS_PLATFORM + "exec -T postgres psql -U postgres -d " + LEDGER_DATABASE
                     + " -X -q -A -t -v ON_ERROR_STOP=1", stdin=script).stdout.split()


def ledger_level():
    """The identity set's level and source as the target last read them."""
    sets = (transport("state", "--target", LEDGER_TARGET, root=PRODUCTS_INVENTORY).get("migrations") or {}).get("sets", [])
    found = next((item for item in sets if item.get("name") == LEDGER_SET), {})
    return found.get("level"), found.get("source"), found.get("state")


def container_state(service):
    state = on_products("docker", "inspect", "--format", "{{.State.Status}}", LEDGER_TARGET + "-" + service + "-1",
                        check=False)
    return state.stdout.strip() if state.returncode == 0 else "missing"


def follow_job(job, timeout=600):
    """A submitted job's outcome on the products host, its steps printed."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        time.sleep(3)
        outcome = transport("status", "--job", job, root=PRODUCTS_INVENTORY)
        if outcome.get("status") in TERMINAL:
            for step in outcome.get("steps", []):
                print("  " + step["status"].ljust(9) + " " + step["name"]
                      + (" — " + step["detail"] if step.get("detail") else ""))
            return outcome
    sys.exit("Timed out waiting for " + job)


def prove_set_reset():
    """A reset of one migration set on the products host, as Ocharo's identity set needs after Identity rewrote 001 in
    place: the first deploy migrates Ledger's identity set; a rewritten checksum makes Check now and a deploy refuse on
    drift; an unknown set is refused before any job; the set reset snapshots the database, stops only identity, rolls
    the set back to 0 with the running image's own scripts and sets its level to 0, while the desk keeps running and
    its table keeps its rows; the next deploy builds the set again and starts identity. Exits non-zero on any failed
    check."""
    if not (PRODUCTS_INVENTORY / "ssh" / PRODUCTS_SERVER / "identity").exists():
        sys.exit("Prove the products host first: rehearse.py products")
    proofs = {}
    compose("--profile", "products", "up", "-d", "--wait", "products", capture=False)
    as_deploy(PRODUCTS_PLATFORM + "up -d --wait postgres")
    ledger_database()
    ledger_inventory()
    bundle_id = ledger_bundle()

    # 1. Ledger deploys: the migrate step applies the identity set; the desk keeps a table in the same database.
    first = deploy(bundle_id, "dev", target=LEDGER_TARGET, root=PRODUCTS_INVENTORY, timeout=600)
    level = ledger_level()
    proofs["first deploy succeeded, identity at level " + str(level[0])] = (
        first.get("status") == "succeeded" and level[0] == 3)
    marker = "kept " + secrets.token_hex(4)
    ledger_sql("CREATE TABLE IF NOT EXISTS public.desk_notes (id serial PRIMARY KEY, body text NOT NULL);\n"
               "INSERT INTO public.desk_notes (body) VALUES ('" + marker + "');\n", role=LEDGER_DATABASE)

    # 2. 001-accounts no longer matches what the database applied, as after an in-place rewrite.
    history = "identity.identity_migration_history"
    original = ledger_sql("SELECT checksum FROM " + history + " WHERE ordinal = 1;\n")[0]
    ledger_sql("UPDATE " + history + " SET checksum = repeat('0', 64) WHERE ordinal = 1;\n")
    try:
        check = transport("migrations", "--target", LEDGER_TARGET, root=PRODUCTS_INVENTORY)
        item = next(iter(check.get("items") or []), {})
        states = [entry.get("state") for entry in item.get("sets", [])]
        proofs["Check now: identity " + str(item.get("status")) + ", " + ", ".join(map(str, states))] = (
            item.get("status") == "refused" and states == ["drifted"])
        blocked = deploy(bundle_id, "dev", target=LEDGER_TARGET, root=PRODUCTS_INVENTORY, timeout=600)
        proofs["the next deploy stops at its migrate step, identity still serving"] = (
            blocked.get("status") == "failed" and str(blocked.get("reason")).startswith("Migrate identity: Refused")
            and container_state("identity") == "running")

        # 3. Refused before any job: a set Ledger does not carry, and a set on another control.
        message = refused("control", "--target", LEDGER_TARGET, "--operation", "reset", "--set", "nope", "--actor",
                          "local-server", root=PRODUCTS_INVENTORY)
        proofs["an unknown set is refused (" + message[-60:] + ")"] = "has no migration set nope" in message
        message = refused("control", "--target", LEDGER_TARGET, "--operation", "stop", "--set", LEDGER_SET, "--actor",
                          "local-server", root=PRODUCTS_INVENTORY)
        proofs["only a reset names a set"] = "Only a reset names a migration set" in message

        # 4. The set reset, submitted as the console submits it.
        submitted = transport("control", "--target", LEDGER_TARGET, "--operation", "reset", "--set", LEDGER_SET,
                              "--actor", "local-server", "--reason", "identity 001 rewritten in place",
                              root=PRODUCTS_INVENTORY)
        proofs["the job names its set"] = submitted.get("operation") == {"kind": "reset", "services": [],
                                                                         "set": LEDGER_SET}
        reset = follow_job(submitted["id"])
    finally:
        # A run cut short leaves the checksum as it found it; after the reset no row is left to restore.
        ledger_sql("UPDATE " + history + " SET checksum = '" + original + "' WHERE ordinal = 1 AND checksum = "
                   "repeat('0', 64);\n")
    detail = transport("observation", "--job", submitted["id"], root=PRODUCTS_INVENTORY)
    record = detail if isinstance(detail, dict) and detail.get("steps") else reset
    names = [step["name"] for step in record.get("steps", [])]
    proofs["the set reset succeeded: " + ", ".join(names)] = reset.get("status") == "succeeded" and names == [
        "Snapshot the database", "Stop identity", "Reset identity to level 0", "Migration level to 0"]
    snapshot = (record.get("snapshot") or {}).get("path")
    dumped = as_deploy("ls -l " + str(snapshot), check=False).stdout if snapshot else ""
    proofs["snapshot of " + LEDGER_DATABASE + " in " + str(snapshot)] = (
        bool(snapshot) and "-" + LEDGER_DATABASE + ".dump" in dumped)
    proofs["identity stopped (" + container_state("identity") + "), desk running (" + container_state("desk") + ")"] = (
        container_state("identity") == "exited" and container_state("desk") == "running")
    rows = ledger_sql("SELECT count(*) FROM " + history + ";\n")
    proofs["identity's history is empty (" + " ".join(rows) + " rows)"] = rows == ["0"]
    proofs["the desk's row is still there"] = ledger_sql(
        "SELECT count(*) FROM public.desk_notes WHERE body = '" + marker + "';\n") == ["1"]
    level = ledger_level()
    proofs["migrations.json: identity at " + str(level[0]) + ", " + str(level[2]) + ", from " + str(level[1])] = (
        level == (0, "reset", "behind"))

    # 5. The next deploy builds the set again and starts identity.
    rebuilt = deploy(bundle_id, "dev", target=LEDGER_TARGET, root=PRODUCTS_INVENTORY, timeout=600)
    level = ledger_level()
    proofs["the next deploy migrated identity back to level " + str(level[0])] = (
        rebuilt.get("status") == "succeeded" and level[:2] == (3, "migrate"))
    proofs["identity running again (" + container_state("identity") + ")"] = container_state("identity") == "running"
    proofs["the desk's row survived the rebuild"] = ledger_sql(
        "SELECT count(*) FROM public.desk_notes WHERE body = '" + marker + "';\n") == ["1"]

    for name, ok in proofs.items():
        print("  " + ("ok" if ok else "FAILED").ljust(9) + " " + name)
    if not all(proofs.values()):
        sys.exit("The set reset did not hold on the products host")
    print("Set reset proven on the products host: drift refused, the identity set rolled back to 0 by its running "
          "image with only identity stopped and the desk's data kept, then rebuilt by the next deploy.")


def down(volumes):
    profiles = ["--profile", "vault", "--profile", "console", "--profile", "dev", "--profile", "products",
                "--profile", "enroll"]
    # The runner names each product project <product>-<environment>.
    for project in (*(PRODUCT + "-" + name for name in (*ENVIRONMENTS, *RETIRED_ENVIRONMENTS)), SELF_TARGET):
        run("docker", "compose", "-p", project, "down", *(["--volumes"] if volumes else []),
            env=environment(), capture=False)
    compose(*profiles, "down", *(["--volumes"] if volumes else []), capture=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["up", "console", "dev", "bundle", "settings", "check", "deploy", "state",
                                           "run", "self", "self-deploy", "harden", "products", "networks", "enroll",
                                           "generated", "set-reset", "down"])
    parser.add_argument("--env", default="dev", help="dev (default) or prod")
    parser.add_argument("--tag", help="bundle: a release tag such as v0.0.1-local.1; without it, a dev candidate")
    parser.add_argument("--bundle", help="the bundle ID to use; defaults to the checkout's candidate")
    parser.add_argument("--confirm", help="deploy: the target ID typed out, required for prod")
    parser.add_argument("--skip-dev-pass", action="store_true",
                        help="deploy: prod takes a release that never succeeded on dev (needs --confirm)")
    parser.add_argument("--volumes", action="store_true")
    parser.add_argument("--broken", action="store_true", help="bundle: build a release that fails after replacement")
    args = parser.parse_args()
    require_environment(args.env)

    def chosen():
        if args.bundle:
            return args.bundle
        commit = run("git", "-C", str(PRODUCT_REPO), "rev-parse", "HEAD").strip()
        return PRODUCT + "-" + re.sub(r"[^a-z0-9-]+", "-", (args.tag or "sha-" + commit[:7]).lower())

    if args.action == "up":
        up()
    elif args.action == "console":
        up(console=True)
    elif args.action == "dev":
        up(dev=True)
    elif args.action == "bundle":
        bundle(args.tag, args.broken)
    elif args.action == "settings":
        settings(chosen(), args.env)
    elif args.action == "check":
        print(json.dumps(transport("check", "--target", target_of(args.env), "--bundle", chosen()), indent=2))
    elif args.action == "deploy":
        deploy(chosen(), args.env, args.confirm, args.skip_dev_pass)
    elif args.action == "state":
        print(json.dumps(transport("state", "--target", target_of(args.env)), indent=2))
    elif args.action == "run":
        up()
        bundle_id = bundle(args.tag)
        settings(bundle_id, args.env)
        print(json.dumps(transport("check", "--target", target_of(args.env), "--bundle", bundle_id), indent=2))
        deploy(bundle_id, args.env, args.confirm)
    elif args.action == "self":
        up()
        bundle_id = self_bundle()
        self_environment()
        deploy(bundle_id, "dev", target=SELF_TARGET, timeout=600)
    elif args.action == "self-deploy":
        prove_self_deploy()
    elif args.action == "harden":
        prove_harden()
    elif args.action == "products":
        products(args.bundle)
    elif args.action == "networks":
        prove_networks()
    elif args.action == "enroll":
        enroll_rig()
    elif args.action == "generated":
        prove_generated()
    elif args.action == "set-reset":
        prove_set_reset()
    else:
        down(args.volumes)


if __name__ == "__main__":
    main()
