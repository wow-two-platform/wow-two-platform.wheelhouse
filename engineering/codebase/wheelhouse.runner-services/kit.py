"""The products-host kit Wheelhouse uploads when it enrolls a server, and again when Refresh kit or a placement brings
an enrolled host to it (upkeep.py): engineering/deployment/vps, shipped inside Wheelhouse's image at /app/kit, so no
job fetches it from GitHub. Its digest is recorded on the job."""
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
# The ingress this kit lays out on a products host (products.compose.yml): public sites on websecure (host port 443)
# with HTTP-01 certificates from the letsencrypt resolver, private sites on the private entry point behind their site
# nodes; the runner probes both on the host. A finished enrollment, placement or host upkeep records it on the server.
PUBLIC_INGRESS = {"scheme": "https", "port": None, "entryPoints": ["websecure"], "privateEntryPoints": ["private"],
                  "certResolver": "letsencrypt", "pattern": None, "probe": "https://127.0.0.1",
                  "privateProbe": "http://127.0.0.1:18080"}


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
