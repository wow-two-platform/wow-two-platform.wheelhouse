#!/usr/bin/env python3
"""Create first-boot dev configuration on the VPS without printing or replacing secrets."""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import sys


def private_directory(path):
    if path.is_symlink():
        raise ValueError("Refusing a symlinked configuration directory")
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if path.stat().st_mode & 0o077:
        raise ValueError("Configuration directories must have mode 0700")


def prepare(root, identity_file, hostname):
    """Write a complete initial credential set once; partial/existing state requires operator review."""
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
    config = root / "config"
    secret_dir = root / "platform" / ".secrets"
    for directory in (root, config, root / "platform", secret_dir):
        private_directory(directory)
    paths = [secret_dir / "postgres-password", secret_dir / "wheelhouse-dev.sql",
             config / "console.json", config / "wheelhouse-dev.json"]
    if any(path.exists() or path.is_symlink() for path in paths):
        raise ValueError("Bootstrap files already exist; refusing credential replacement")

    db_password = secrets.token_hex(32)
    settings = {
        "AllowedHosts": hostname + ";localhost",
        "ConnectionStrings": {"Wheelhouse": "Host=wheelhouse-postgres;Port=5432;Database=wheelhouse_dev;"
                              "Username=wheelhouse_dev;Password=" + db_password},
        "Identity": {"GitHub": {key: github[key] for key in ("ClientId", "ClientSecret")},
                     "AllowedGitHubLogins": logins},
        "DataProtection": {"KeyPath": "/data/keys"},
        "Deployment": {"Root": "/data/deployments", "Rehearsal": "", "TrustedProxies": ["172.30.0.2"]},
    }
    target = {
        "product": "wheelhouse", "environment": "dev", "root": str(root),
        "settings": {"console": str(config / "console.json")},
        "variables": {"PLATFORM_NETWORK": "platform"},
        "ingress": {"scheme": "https", "port": None, "entryPoints": ["private"],
                    "privateEntryPoints": ["private"], "certResolver": None,
                    "hosts": {"console": hostname}, "probe": "http://127.0.0.1:18080",
                    "privateProbe": "http://127.0.0.1:18080"},
    }
    values = [
        secrets.token_hex(32) + "\n",
        "CREATE ROLE wheelhouse_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '"
        + db_password + "';\nCREATE DATABASE wheelhouse_dev OWNER wheelhouse_dev;\n",
        json.dumps(settings, indent=2) + "\n",
        json.dumps(target, indent=2) + "\n",
    ]
    # Parent 0700 protects host reads; bind-mounted files must be readable by container UID 1654/999.
    for path, value in zip(paths, values):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write(value)
        path.chmod(0o444 if path.name != "wheelhouse-dev.json" else 0o600)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/srv/wheelhouse"))
    parser.add_argument("--identity-file", type=Path, required=True)
    parser.add_argument("--hostname", required=True)
    args = parser.parse_args()
    try:
        prepare(args.root, args.identity_file, args.hostname)
    except (ValueError, OSError, TypeError, AttributeError):
        # Never include an exception containing operator input or partial JSON in command output.
        print("Configuration refused. Check private input, required identity values, hostname and existing files.",
              file=sys.stderr)
        return 1
    print("Dev configuration created. No credentials were printed; existing credentials were not replaced.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
