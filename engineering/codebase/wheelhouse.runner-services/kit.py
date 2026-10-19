"""The products-host kit Wheelhouse uploads when it enrolls a server: engineering/deployment/vps, shipped inside
Wheelhouse's image at /app/kit, so an enrollment never fetches it from GitHub. Its digest is recorded on the job."""
from __future__ import annotations
import hashlib
import io
import os
from pathlib import Path
import tarfile
from runner import require

# What a fresh host needs; enroll-products.sh sources the two scripts beside it and lays out the rest.
FILES = ("enroll-products.sh", "bootstrap-products.sh", "create-admin.sh", "repair-networks.sh", "authorize-runner.sh",
         "prepare.py", "products.compose.yml", "tailscale-serve-products.json")
IMAGE_KIT = Path("/app/kit")


def kit_dir():
    """WHEELHOUSE_KIT when set, else the image's /app/kit, else this checkout's engineering/deployment/vps."""
    configured = os.environ.get("WHEELHOUSE_KIT", "")
    if configured:
        return Path(configured)
    if IMAGE_KIT.is_dir():
        return IMAGE_KIT
    return Path(__file__).resolve().parents[2] / "deployment" / "vps"


def files():
    folder = kit_dir()
    result = []
    for name in FILES:
        path = folder / name
        require(path.is_file() and not path.is_symlink(), "The products kit is incomplete: " + name + " is missing")
        result.append((name, path.read_bytes()))
    return result


def digest(entries=None):
    """One hash over the kit's names and contents, independent of timestamps and archive layout."""
    total = hashlib.sha256()
    for name, content in sorted(entries or files()):
        total.update(name.encode() + b"\0" + hashlib.sha256(content).digest())
    return total.hexdigest()


def archive(entries=None):
    """A gzip tar of the kit with fixed ownership and times; scripts executable, the rest owner-readable."""
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz", format=tarfile.PAX_FORMAT) as package:
        for name, content in sorted(entries or files()):
            info = tarfile.TarInfo(name)
            info.size, info.mtime, info.uid, info.gid = len(content), 0, 0, 0
            info.mode = 0o700 if name.endswith((".sh", ".py")) else 0o600
            package.addfile(info, io.BytesIO(content))
    return buffer.getvalue()
