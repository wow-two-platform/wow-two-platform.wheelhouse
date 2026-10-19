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
runner key, and the runner deploys ForeverPin dev to it, reached through its private entry point by site name.
"""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
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
    return {**os.environ, "REHEARSAL_STATE": str(STATE), "WHEELHOUSE_REHEARSAL": "1"}


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


def products_settings(bundle_id, credentials):
    """ForeverPin dev's settings on the products host: its own database, its site hosts and the ingress as its proxy."""
    hosts = {service: PRODUCT + "-" + site + "-dev." + PRODUCTS_TAILNET for service, site in SITES.items()}
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
    return {service: folder + "/" + service + ".json" for service in SITES}, hosts


def products_inventory(settings, hosts):
    """The inventory a prod console would export for this server and target, as the Servers and Products forms take
    them: the tailnet address (here the forwarded SSH port), the deploy account and the private entry point."""
    private_file(PRODUCTS_INVENTORY / "inventory.json", json.dumps({
        "version": 1, "products": [], "vaults": [],
        "servers": [{"id": PRODUCTS_SERVER, "name": "Local products host", "provider": "local", "host": "127.0.0.1",
                     "region": "local", "sshUser": DEPLOY_USER, "sshPort": PRODUCTS_PORT,
                     "ingress": {"scheme": "https", "port": None, "entryPoints": ["private"],
                                 "privateEntryPoints": ["private"], "certResolver": None, "pattern": None,
                                 "probe": "http://127.0.0.1:18080", "privateProbe": "http://127.0.0.1:18080"}}],
        "targets": [{"id": PRODUCT + "-dev", "serverId": PRODUCTS_SERVER, "product": PRODUCT, "environment": "dev",
                     "network": "platform", "root": PRODUCTS_ROOT,
                     "settings": [{"service": service, "path": path} for service, path in settings.items()],
                     "smoke": [{"service": "management", "path": "/api/runtime-config", "status": 200},
                               {"service": "redirect", "path": "/health", "status": 200}],
                     "sites": [{"site": SITES[service], "host": host} for service, host in hosts.items()]}],
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
    settings, hosts = products_settings(bundle_id, credentials)
    products_inventory(settings, hosts)
    report = transport("check", "--target", PRODUCT + "-dev", "--bundle", bundle_id, root=PRODUCTS_INVENTORY)
    for check in report["checks"]:
        print("  " + ("ok" if check["ok"] else "FAILED").ljust(9) + " " + check["name"] + " — " + check["detail"])
    if not report["ok"]:
        sys.exit("The products host failed its check")
    outcome = deploy(bundle_id, "dev", root=PRODUCTS_INVENTORY)
    if outcome.get("status") != "succeeded":
        sys.exit("The deploy to the products host did not succeed")
    # A site node's path: from the platform network to Traefik's private entry point, by the site's host name.
    for service, host in hosts.items():
        path = "/api/runtime-config" if service == "management" else "/health"
        answer = as_deploy("docker exec " + PRODUCT + "-dev-" + service + "-1 curl -s -o /dev/null -w '%{http_code}' "
                           "-H 'Host: " + host + "' http://172.30.0.2:8080" + path).stdout.strip()
        print("Private entry point, Host " + host + path + ": " + answer)
        if answer != "200":
            sys.exit("The private entry point did not route " + host)
    prove_runner_authorization()
    prove_private_pull(json.loads((destination / "release.json").read_text())["images"]["redirect"])
    print("Products host proven locally: layout, platform, databases, site nodes, runner key, deploy, private route, "
          "private image pull.")


def down(volumes):
    profiles = ["--profile", "vault", "--profile", "console", "--profile", "dev", "--profile", "products"]
    # The runner names each product project <product>-<environment>.
    for project in (*(PRODUCT + "-" + name for name in (*ENVIRONMENTS, *RETIRED_ENVIRONMENTS)), SELF_TARGET):
        run("docker", "compose", "-p", project, "down", *(["--volumes"] if volumes else []),
            env=environment(), capture=False)
    compose(*profiles, "down", *(["--volumes"] if volumes else []), capture=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["up", "console", "dev", "bundle", "settings", "check", "deploy", "state",
                                           "run", "self", "products", "down"])
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
    elif args.action == "products":
        products(args.bundle)
    else:
        down(args.volumes)


if __name__ == "__main__":
    main()
