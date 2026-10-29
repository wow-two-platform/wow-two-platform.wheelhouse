#!/usr/bin/env python3
"""Create a host's configuration without printing or replacing secrets.

The control host (default): a Wheelhouse console environment's database, settings and target. A products host
(`--server products`): its PostgreSQL password and rendered Compose file, each product's per-environment databases with
the untrusted extensions it declares and its settings folders, a tailnet node per private site, and a read-only GHCR
sign-in for the deploy account. A products host whose PostgreSQL 16 data waits for its move to 18 is refused first
(postgres-move.py)."""
import argparse
import fcntl
import ipaddress
import json
import os
from pathlib import Path
import pwd
import re
import secrets
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

# Dev and prod share one host and one private hostname; each has its own Traefik entry point and HTTPS port.
ENVIRONMENTS = {
    "dev": {"settings": "console-dev.json", "entryPoint": "dev", "port": 8443, "probe": "http://127.0.0.1:18082"},
    "prod": {"settings": "console-prod.json", "entryPoint": "private", "port": None, "probe": "http://127.0.0.1:18080"},
}

# A products host runs every product's dev and prod side by side, as the control host runs the consoles.
PRODUCT_ENVIRONMENTS = ("dev", "prod")
PRODUCT = re.compile(r"[a-z][a-z0-9-]{0,47}")
# Tailnet hostnames for site nodes; prod's node is the base name, dev's adds "-dev", as the canary's nodes do.
NODE = re.compile(r"[a-z][a-z0-9-]{0,50}[a-z0-9]")
# Compose services the products template already defines.
RESERVED_NODES = ("postgres", "ingress")
# Site nodes take fixed addresses here; Traefik's private entry point trusts forwarded headers from this range only.
SITE_NODES = ipaddress.ip_network("172.30.0.64/26")
TAILNET = re.compile(r"[a-z0-9-]+\.ts\.net")
DATABASE_HOST = "wheelhouse-postgres"
GITHUB_LOGIN = re.compile(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?")
# GHCR takes classic personal access tokens; its read-only scope is read:packages.
CLASSIC_TOKEN = re.compile(r"ghp_[A-Za-z0-9]{36,251}")
REGISTRY = "ghcr.io"
# An untrusted PostgreSQL extension a product declares (deploy.yml `extensions`); created as superuser at placement.
EXTENSION = re.compile(r"[a-z][a-z0-9_-]{0,62}")
READ_ONLY_SCOPES = {"read:packages"}
# The kit's Compose projects: the control host's platform and a products host's.
CONTROL_PROJECT = "wheelhouse-platform"
PRODUCTS_PROJECT = "wheelhouse-products"
# PostgreSQL 16 kept its cluster in the project's `postgres` volume; the kit's 18 keeps it in `postgres-pg18` and
# refuses a 16 data directory. postgres-move.py moves one into the other and records it in platform/postgres-move/.
PREVIOUS_VOLUME = "postgres"
MOVE_FOLDER = "postgres-move"
MOVE_MARKER = "moved.json"


class Refused(ValueError):
    """A refusal whose message names a rule, never an input value; the CLI prints it as it is."""


def volume_present(name):
    """Whether Docker holds a volume of exactly this name: `docker volume inspect` must print the name back, so a
    stand-in Docker that answers every call with exit 0 and no output reads as no volume. No Docker CLI, no volume."""
    try:
        result = subprocess.run(["docker", "volume", "inspect", "--format", "{{.Name}}", name], capture_output=True,
                                text=True, timeout=30)
    except FileNotFoundError:
        return False
    except subprocess.TimeoutExpired:
        raise Refused("Docker did not answer whether " + name + " exists") from None
    return result.returncode == 0 and result.stdout.strip() == name


def move_running(root):
    """Whether a PostgreSQL move holds the host's move lock now; the lock file is never created here."""
    try:
        with (root / "platform" / MOVE_FOLDER / "lock").open("r") as stream:
            try:
                fcntl.flock(stream, fcntl.LOCK_SH | fcntl.LOCK_NB)
            except BlockingIOError:
                return True
            fcntl.flock(stream, fcntl.LOCK_UN)
            return False
    except FileNotFoundError:
        return False


def move_recorded(root):
    """The finished move's record (platform/postgres-move/moved.json), or None when no move to 18 is recorded."""
    path = root / "platform" / MOVE_FOLDER / MOVE_MARKER
    if not path.is_file() or path.is_symlink():
        return None
    try:
        value = json.loads(path.read_text())
    except (OSError, ValueError):
        return None
    return value if isinstance(value, dict) else None


def postgres_guard(root, project, present=None):
    """Why the kit's PostgreSQL 18 must not start on this host now, or None when it may: a move runs, or 16 data waits
    in the project's `postgres` volume with no move recorded, where 18 would start empty beside it. Every step that
    installs or renders the platform Compose file asks this first: prepare.py --server products, bootstrap.sh,
    retire-test.sh and migrate-control-network.sh (the last three through postgres-move.py check)."""
    if move_running(root):
        return "A PostgreSQL move runs on this host; wait for it to finish (postgres-move.py status)"
    previous = project + "_" + PREVIOUS_VOLUME
    if (present or volume_present)(previous) and move_recorded(root) is None:
        return ("PostgreSQL 16 data waits in " + previous + "; 18 would start empty beside it. Move it first: "
                "postgres-move.py move (vps.md § PostgreSQL 18)")
    return None


def private_directory(path):
    if path.is_symlink():
        raise ValueError("Refusing a symlinked configuration directory")
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if path.stat().st_mode & 0o077:
        raise ValueError("Configuration directories must have mode 0700")


def create_database(root, name, password):
    """Create a later environment's role and database in the running platform PostgreSQL; SQL travels on stdin."""
    sql = ("CREATE ROLE " + name + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '" + password + "';\n"
           "CREATE DATABASE " + name + " OWNER " + name + ";\n")
    platform = root / "platform"
    result = subprocess.run(["docker", "compose", "--env-file", str(platform / "host.env"),
                             "-f", str(platform / "compose.yml"), "exec", "-T", "postgres",
                             "psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-q"],
                            input=sql, capture_output=True, text=True, timeout=60)
    if result.returncode != 0:
        raise ValueError("Database creation failed")


def create_extensions(root, name, extensions):
    """Create untrusted extensions in one database as the platform PostgreSQL's superuser (M41); SQL travels on stdin
    and each name is checked and quoted, so a rerun creates only what is missing."""
    sql = "".join('CREATE EXTENSION IF NOT EXISTS "' + extension + '";\n' for extension in extensions)
    platform = root / "platform"
    result = subprocess.run(["docker", "compose", "--env-file", str(platform / "host.env"),
                             "-f", str(platform / "compose.yml"), "exec", "-T", "postgres",
                             "psql", "-U", "postgres", "-d", name, "-v", "ON_ERROR_STOP=1", "-q"],
                            input=sql, capture_output=True, text=True, timeout=120)
    if result.returncode != 0:
        raise ValueError("Extension creation failed")


def prepare(root, identity_file, hostname, environment="dev", database=create_database):
    """Write a complete credential set once; partial/existing state requires operator review.
    Dev is the first environment and initializes the platform database; prod joins the running one."""
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.[a-z0-9-]+\.ts\.net", hostname):
        raise ValueError("Use the actual node.tailnet.ts.net private hostname")
    if (not identity_file.is_file() or identity_file.is_symlink()
            or identity_file.stat().st_mode & 0o077):
        raise ValueError("Identity input must be a regular private file, mode 0600")
    identity = json.loads(identity_file.read_text()).get("Identity", {})
    github = identity.get("GitHub", {})
    if not all(isinstance(github.get(key), str) and github[key].strip()
               for key in ("ClientId", "ClientSecret")):
        raise ValueError("Configure the real OAuth ClientId and ClientSecret")
    logins = identity.get("AllowedGitHubLogins")
    if not isinstance(logins, list) or not logins or not all(
            isinstance(login, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,38}", login)
            for login in logins):
        raise ValueError("Provide at least one real owner GitHub login")
    if not root.is_absolute() or root in (Path("/"), Path("/srv"), Path("/tmp")):
        raise ValueError("Use a dedicated absolute deployment root")
    shape = ENVIRONMENTS[environment]
    name = "wheelhouse_" + environment
    config = root / "config"
    secret_dir = root / "platform" / ".secrets"
    for directory in (root, config, root / "platform", secret_dir):
        private_directory(directory)
    first = environment == "dev"
    paths = ([secret_dir / "postgres-password", secret_dir / "wheelhouse-dev.sql"] if first else []) + [
        config / shape["settings"], config / ("wheelhouse-" + environment + ".json")]
    if any(path.exists() or path.is_symlink() for path in paths):
        raise ValueError("Bootstrap files already exist; refusing credential replacement")

    db_password = secrets.token_hex(32)
    settings = {
        "AllowedHosts": hostname + ";localhost",
        "ConnectionStrings": {"Wheelhouse": "Host=wheelhouse-postgres;Port=5432;Database=" + name + ";"
                              "Username=" + name + ";Password=" + db_password},
        "Identity": {"GitHub": {key: github[key] for key in ("ClientId", "ClientSecret")},
                     "AllowedGitHubLogins": logins},
        "DataProtection": {"KeyPath": "/data/keys"},
        "Deployment": {"Root": "/data/deployments", "Rehearsal": "", "TrustedProxies": ["172.30.1.2"]},
    }
    target = {
        "product": "wheelhouse", "environment": environment, "root": str(root),
        "settings": {"console": str(config / shape["settings"])},
        "variables": {"PLATFORM_NETWORK": "control"},
        "ingress": {"scheme": "https", "port": shape["port"], "entryPoints": [shape["entryPoint"]],
                    "privateEntryPoints": [shape["entryPoint"]], "certResolver": None,
                    "hosts": {"console": hostname}, "probe": shape["probe"], "privateProbe": shape["probe"]},
    }
    values = ([
        secrets.token_hex(32) + "\n",
        "CREATE ROLE wheelhouse_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '"
        + db_password + "';\nCREATE DATABASE wheelhouse_dev OWNER wheelhouse_dev;\n",
    ] if first else []) + [json.dumps(settings, indent=2) + "\n", json.dumps(target, indent=2) + "\n"]
    if not first:
        database(root, name, db_password)
    # Parent 0700 protects host reads; bind-mounted files must be readable by container UID 1654/999.
    for path, value in zip(paths, values):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write(value)
        path.chmod(0o600 if path.name.startswith("wheelhouse-") and path.suffix == ".json" else 0o444)


def dedicated_root(root):
    if not root.is_absolute() or root in (Path("/"), Path("/srv"), Path("/tmp")):
        raise Refused("Use a dedicated absolute deployment root")


def write_once(path, value, mode):
    """Writes a new file that no one else could have read before its mode was set; an existing file is never reused."""
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w") as output:
        output.write(value)
    path.chmod(mode)


def replace_private(path, value):
    """Publishes a whole mode-0600 file at once, so Compose never reads a half-written one."""
    with tempfile.NamedTemporaryFile("w", dir=path.parent, prefix="." + path.name + ".", delete=False) as output:
        os.chmod(output.name, 0o600)
        output.write(value)
    os.replace(output.name, path)


def site_nodes(root):
    """The site nodes registered on this products host, each checked again before it reaches Compose."""
    registry = root / "platform" / "sites.json"
    if registry.is_symlink():
        raise Refused("Refusing a symlinked site registry")
    if not registry.exists():
        return []
    nodes = json.loads(registry.read_text()).get("nodes")
    if not isinstance(nodes, list):
        raise Refused("The site registry is malformed")
    for node in nodes:
        if (not isinstance(node, dict) or not NODE.fullmatch(str(node.get("name"))) or node["name"] in RESERVED_NODES
                or not PRODUCT.fullmatch(str(node.get("product"))) or not PRODUCT.fullmatch(str(node.get("site")))
                or node.get("environment") not in PRODUCT_ENVIRONMENTS
                or ipaddress.ip_address(str(node.get("address"))) not in SITE_NODES):
            raise Refused("The site registry is malformed")
    if len({node["name"] for node in nodes}) != len(nodes) or len({node["address"] for node in nodes}) != len(nodes):
        raise Refused("The site registry is malformed")
    return nodes


def render_products_compose(root):
    """platform/compose.yml: the installed products template plus one tailnet node service per registered site node."""
    platform = root / "platform"
    template = (platform / "products.compose.yml").read_text()
    top = [line for line in template.splitlines() if line and not line[0].isspace() and not line.startswith("#")]
    if not top or top[-1] != "services:":
        raise Refused("The products Compose template must end with its services")
    nodes = sorted(site_nodes(root), key=lambda node: ipaddress.ip_address(node["address"]))
    # Each node also reads its own private/sites/<node>.env: the single-use key Wheelhouse minted for it, written just
    # before the node first starts and removed once it has. The shared file is the hand-run kit's reusable key.
    blocks = "".join(
        "  " + node["name"] + ":\n"
        "    <<: *site-node\n"
        "    hostname: " + node["name"] + "\n"
        "    env_file:\n"
        "      - { path: ../private/tailscale-sites.env, required: false }\n"
        "      - { path: ../private/sites/" + node["name"] + ".env, required: false }\n"
        "    environment: { <<: *site-env, TS_HOSTNAME: " + node["name"] + " }\n"
        "    volumes: [./sites/" + node["name"] + ":/var/lib/tailscale, ./tailscale-serve.json:/config/serve.json:ro]\n"
        "    networks: { platform: { ipv4_address: " + node["address"] + " } }\n" for node in nodes)
    rendered = template.rstrip("\n") + "\n" + (
        "  # Site nodes rendered by prepare.py from sites.json.\n" + blocks if blocks else "")
    replace_private(platform / "compose.yml", rendered)
    return nodes


def prepare_products_platform(root):
    """The products host's PostgreSQL password, created once and kept on every rerun, and its rendered Compose file.
    Refused before anything is written while 16 data waits for its move, so a kit refresh never starts 18 empty."""
    dedicated_root(root)
    waiting = postgres_guard(root, PRODUCTS_PROJECT)
    if waiting:
        raise Refused(waiting)
    platform = root / "platform"
    # acme/ holds the public ingress's certificates and ACME account, written by Traefik as this account.
    for directory in (root, root / "config", platform, platform / ".secrets", platform / "sites", platform / "acme"):
        private_directory(directory)
    password = platform / ".secrets" / "postgres-password"
    if password.is_symlink():
        raise Refused("Refusing a symlinked database password")
    if not password.exists():
        # Readable inside the mode-0700 folder by the PostgreSQL container's own user, as on the control host.
        write_once(password, secrets.token_hex(32) + "\n", 0o444)
    return render_products_compose(root)


def product_folders(root, product):
    if not PRODUCT.fullmatch(product or ""):
        raise Refused("Use the product's slug, such as ocharo-platform")
    folders = {environment: root / "config" / (product + "-" + environment) for environment in PRODUCT_ENVIRONMENTS}
    for folder in folders.values():
        private_directory(folder)
    return folders


def prepare_product_databases(root, product, database=create_database):
    """One role and database per environment in the products host's PostgreSQL, each password in an owner-only file.
    Existing or partial credentials are refused: a live password is never rotated here."""
    folders = product_folders(root, product)
    files = {environment: folder / "database.json" for environment, folder in folders.items()}
    if any(path.exists() or path.is_symlink() for path in files.values()):
        raise Refused("Database credentials already exist for " + product + "; refusing replacement")
    created = []
    for environment in PRODUCT_ENVIRONMENTS:
        name = product.replace("-", "_") + "_" + environment
        password = secrets.token_hex(32)
        try:
            database(root, name, password)
        except (ValueError, OSError, subprocess.SubprocessError):
            raise Refused("Database creation failed for " + name + "; is the products platform running?") from None
        # Written as soon as its role exists, so a later failure never strands a password no one holds.
        write_once(files[environment], json.dumps({
            "Host": DATABASE_HOST, "Port": 5432, "Database": name, "Username": name, "Password": password,
            "ConnectionString": "Host=" + DATABASE_HOST + ";Port=5432;Database=" + name + ";Username=" + name
                                + ";Password=" + password}, indent=2) + "\n", 0o600)
        created.append(name)
    return created


def prepare_product_extensions(root, product, extensions, create=create_extensions):
    """The product's declared untrusted extensions in its dev and prod databases, which must exist already."""
    if not extensions or not all(isinstance(name, str) and EXTENSION.fullmatch(name) for name in extensions):
        raise Refused("Use --extension with a PostgreSQL extension name, such as postgres_fdw")
    folders = product_folders(root, product)
    names = []
    for environment in PRODUCT_ENVIRONMENTS:
        if not (folders[environment] / "database.json").is_file():
            raise Refused("Create the product's databases before its extensions")
        name = product.replace("-", "_") + "_" + environment
        try:
            create(root, name, list(dict.fromkeys(extensions)))
        except (ValueError, OSError, subprocess.SubprocessError):
            raise Refused("Extension creation failed in " + name + "; is each extension installed in the platform "
                          "PostgreSQL image?") from None
        names.append(name)
    return names


def register_sites(root, product, sites):
    """Gives each (site, environment) its own tailnet node at a fixed address; a repeated site keeps its node."""
    nodes = site_nodes(root)
    added = []
    for site, base in sites:
        if not PRODUCT.fullmatch(site or "") or not NODE.fullmatch(base or "") or base in RESERVED_NODES:
            raise Refused("Use --site <site>[=<node>], lowercase, such as app=ocharo-desk")
        for environment in PRODUCT_ENVIRONMENTS:
            name = base + ("-dev" if environment == "dev" else "")
            role = (product, site, environment)
            same = next((node for node in nodes if node["name"] == name), None)
            if same is not None:
                if (same["product"], same["site"], same["environment"]) != role:
                    raise Refused("Tailnet node " + name + " already serves another site")
                continue
            if any((node["product"], node["site"], node["environment"]) == role for node in nodes):
                raise Refused("That site already has a node under another name")
            taken = {node["address"] for node in nodes}
            address = next((str(host) for host in SITE_NODES.hosts() if str(host) not in taken), None)
            if address is None:
                raise Refused("No site node address is left")
            node = {"name": name, "address": address, "product": product, "site": site, "environment": environment}
            nodes.append(node)
            added.append(node)
    if added:
        for node in added:
            private_directory(root / "platform" / "sites" / node["name"])
        replace_private(root / "platform" / "sites.json", json.dumps({"nodes": nodes}, indent=2) + "\n")
    return [node for node in nodes if node["product"] == product]


def token_scopes(token):
    """The classic token's scopes as GitHub reports them; the token goes only to api.github.com."""
    request = urllib.request.Request("https://api.github.com/user", headers={
        "Authorization": "token " + token, "Accept": "application/vnd.github+json", "User-Agent": "wheelhouse-prepare"})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            header = response.headers.get("X-OAuth-Scopes")
    except (urllib.error.URLError, OSError):
        raise Refused("GitHub did not accept the token") from None
    if header is None:
        raise Refused("Use a classic token with read:packages only")
    return {scope.strip() for scope in header.split(",") if scope.strip()}


def docker_login(user, token):
    """Signs the current account in to GHCR with the token on stdin, into its own ~/.docker/config.json, which the
    runner's Docker reads over SSH. Docker's output is never relayed."""
    home = pwd.getpwuid(os.getuid()).pw_dir
    environment = {key: value for key, value in os.environ.items() if key != "DOCKER_CONFIG"}
    environment["HOME"] = home
    result = subprocess.run(["docker", "login", REGISTRY, "--username", user, "--password-stdin"], input=token,
                            capture_output=True, text=True, timeout=60, env=environment)
    if result.returncode:
        raise Refused("GHCR sign-in failed")
    config = Path(home) / ".docker" / "config.json"
    if config.is_file() and not config.is_symlink():
        config.chmod(0o600)


def ghcr_login(token_file, user, scopes=token_scopes, login=docker_login):
    """A read-only GHCR sign-in from an owner-placed token file: classic, read:packages and nothing else."""
    if not GITHUB_LOGIN.fullmatch(user or ""):
        raise Refused("Use the GitHub login that owns the token")
    if (token_file is None or token_file.is_symlink() or not token_file.is_file()
            or token_file.stat().st_mode & 0o077 or token_file.stat().st_size > 1024):
        raise Refused("The token file must be a regular private file, mode 0600")
    token = token_file.read_text().strip()
    if not CLASSIC_TOKEN.fullmatch(token):
        raise Refused("Use a classic token with read:packages only")
    if scopes(token) != READ_ONLY_SCOPES:
        raise Refused("The token must carry read:packages and nothing else")
    login(user, token)


def prepare_products(root, product=None, database=False, sites=(), tailnet=None, ghcr_user=None,
                     ghcr_token_file=None, create=create_database, scopes=token_scopes, login=docker_login,
                     extensions=(), extend=create_extensions):
    """Every products-host step the arguments ask for, in a safe order; returns operator-safe report lines."""
    if (database or sites or extensions) and not product:
        raise Refused("Name the product with --product")
    if sites and not TAILNET.fullmatch(tailnet or ""):
        raise Refused("Use the actual tailnet name with --tailnet, such as tail1234.ts.net")
    if (ghcr_user is None) != (ghcr_token_file is None):
        raise Refused("GHCR sign-in needs both --ghcr-user and --ghcr-token-file")
    prepare_products_platform(root)
    report = []
    if product:
        folders = product_folders(root, product)
        if database:
            for name in prepare_product_databases(root, product, create):
                report.append("Database " + name + ": owner-only credentials in config/" + product + "-"
                              + name.rsplit("_", 1)[1] + "/database.json.")
        if extensions:
            names = prepare_product_extensions(root, product, list(extensions), extend)
            report.append("Extensions " + ", ".join(dict.fromkeys(extensions)) + " in " + " and ".join(names)
                          + ", created as superuser.")
        for environment, folder in folders.items():
            report.append("Settings for " + product + "-" + environment + ": " + str(folder) + "/<service>.json")
        if sites:
            for node in register_sites(root, product, sites):
                report.append("Site " + node["site"] + " (" + node["environment"] + "): https://" + node["name"] + "."
                              + tailnet + " on node " + node["name"] + " at " + node["address"])
            report.append("Start new site nodes: docker compose --env-file host.env -f compose.yml up -d")
    if ghcr_user is not None:
        ghcr_login(ghcr_token_file, ghcr_user, scopes, login)
        report.append("GHCR: this account is signed in with a read-only token. No credential was printed.")
    nodes = render_products_compose(root)
    return ["Platform: PostgreSQL password in place; compose.yml rendered with " + str(len(nodes))
            + " site nodes."] + report


def parse_site(value):
    site, _, node = value.partition("=")
    return site, node or None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/srv/wheelhouse"))
    parser.add_argument("--server", choices=["control", "products"], default="control")
    parser.add_argument("--identity-file", type=Path, help="control: the console's private identity input")
    parser.add_argument("--hostname", help="control: the actual node.tailnet.ts.net name")
    parser.add_argument("--environment", choices=sorted(ENVIRONMENTS), help="control: dev (default) or prod")
    parser.add_argument("--product", help="products: the product's slug")
    parser.add_argument("--database", action="store_true", help="products: create the product's dev and prod databases")
    parser.add_argument("--extension", action="append", default=[], metavar="NAME",
                        help="products: an untrusted PostgreSQL extension to create in both databases as superuser")
    parser.add_argument("--site", action="append", default=[], metavar="SITE[=NODE]",
                        help="products: a tailnet node per environment for the product's site")
    parser.add_argument("--tailnet", help="products: the tailnet name, such as tail1234.ts.net")
    parser.add_argument("--ghcr-user", help="products: the GitHub login that owns the GHCR token")
    parser.add_argument("--ghcr-token-file", type=Path, help="products: an owner-placed classic read:packages token")
    args = parser.parse_args()
    if args.server == "products":
        if args.identity_file or args.hostname or args.environment:
            parser.error("--identity-file, --hostname and --environment configure the control host")
        sites = [(site, node or (args.product or "") + "-" + site) for site, node in map(parse_site, args.site)]
        try:
            report = prepare_products(args.root, args.product, args.database, sites, args.tailnet, args.ghcr_user,
                                      args.ghcr_token_file, extensions=args.extension)
        except Refused as error:
            print("Products configuration refused: " + str(error) + ".", file=sys.stderr)
            return 1
        except (ValueError, OSError, TypeError, AttributeError, KeyError, subprocess.SubprocessError):
            # Never include an exception that could carry a token, a password or partial JSON in command output.
            print("Products configuration refused. Check the kit files, existing files and the platform.",
                  file=sys.stderr)
            return 1
        print("\n".join(report))
        return 0
    if (args.identity_file is None or args.hostname is None or args.product or args.database or args.site
            or args.extension
            or args.tailnet or args.ghcr_user or args.ghcr_token_file):
        parser.error("the control host takes --identity-file and --hostname; products options need --server products")
    environment = args.environment or "dev"
    try:
        prepare(args.root, args.identity_file, args.hostname, environment)
    except (ValueError, OSError, TypeError, AttributeError, subprocess.SubprocessError):
        # Never include an exception containing operator input or partial JSON in command output.
        print("Configuration refused. Check private input, required identity values, hostname and existing files.",
              file=sys.stderr)
        return 1
    print(environment.capitalize() + " configuration created. No credentials were printed;"
          " existing credentials were not replaced.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
