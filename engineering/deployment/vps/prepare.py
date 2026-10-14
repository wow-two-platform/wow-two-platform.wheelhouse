#!/usr/bin/env python3
"""Create a Wheelhouse environment's configuration on the VPS without printing or replacing secrets."""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys

# Dev and prod share one host and one private hostname; each has its own Traefik entry point and HTTPS port.
ENVIRONMENTS = {
    "dev": {"settings": "console-dev.json", "entryPoint": "dev", "port": 8443, "probe": "http://127.0.0.1:18082"},
    "prod": {"settings": "console-prod.json", "entryPoint": "private", "port": None, "probe": "http://127.0.0.1:18080"},
}


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
        "Deployment": {"Root": "/data/deployments", "Rehearsal": "", "TrustedProxies": ["172.30.0.2"]},
    }
    target = {
        "product": "wheelhouse", "environment": environment, "root": str(root),
        "settings": {"console": str(config / shape["settings"])},
        "variables": {"PLATFORM_NETWORK": "platform"},
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/srv/wheelhouse"))
    parser.add_argument("--identity-file", type=Path, required=True)
    parser.add_argument("--hostname", required=True)
    parser.add_argument("--environment", choices=sorted(ENVIRONMENTS), default="dev")
    args = parser.parse_args()
    try:
        prepare(args.root, args.identity_file, args.hostname, args.environment)
    except (ValueError, OSError, TypeError, AttributeError, subprocess.SubprocessError):
        # Never include an exception containing operator input or partial JSON in command output.
        print("Configuration refused. Check private input, required identity values, hostname and existing files.",
              file=sys.stderr)
        return 1
    print(args.environment.capitalize() + " configuration created. No credentials were printed;"
          " existing credentials were not replaced.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
