#!/usr/bin/env python3
"""Create the canary's dev and prod databases and settings files on the VPS, never printing a secret."""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys

import prepare

PRODUCT = "wheelhousecanary"
# Each environment answers on its own tailnet node, named in platform.compose.yml.
NODES = {"dev": "canary-dev", "prod": "canary"}


def prepare_canary(root, tailnet, database=prepare.create_database):
    """Write every environment's settings once; existing or partial state requires operator review."""
    if not re.fullmatch(r"[a-z0-9-]+\.ts\.net", tailnet):
        raise ValueError("Use the actual tailnet name, such as tail1234.ts.net")
    if not root.is_absolute() or root in (Path("/"), Path("/srv"), Path("/tmp")):
        raise ValueError("Use a dedicated absolute deployment root")
    config = root / "config"
    prepare.private_directory(config)
    paths = {environment: config / ("canary-" + environment + ".json") for environment in NODES}
    if any(path.exists() or path.is_symlink() for path in paths.values()):
        raise ValueError("Canary settings already exist; refusing credential replacement")

    for environment, path in paths.items():
        name = PRODUCT + "_" + environment
        password = secrets.token_hex(32)
        database(root, name, password)
        settings = {
            "AllowedHosts": NODES[environment] + "." + tailnet + ";localhost",
            "DatabaseSettings": {"ConnectionString": "Host=wheelhouse-postgres;Port=5432;Database=" + name + ";"
                                                     "Username=" + name + ";Password=" + password},
            "Canary": {"Label": environment},
        }
        # Parent 0700 protects host reads; the bind-mounted file must be readable by the container user.
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write(json.dumps(settings, indent=2) + "\n")
        path.chmod(0o444)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/srv/wheelhouse"))
    parser.add_argument("--tailnet", required=True)
    args = parser.parse_args()
    try:
        prepare_canary(args.root, args.tailnet)
    except (ValueError, OSError, TypeError, subprocess.SubprocessError):
        print("Canary configuration refused. Check the tailnet name, existing files and the platform database.",
              file=sys.stderr)
        return 1
    print("Canary configuration created for dev and prod. No credentials were printed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
