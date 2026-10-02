"""Products as the control plane's inventory names them. The database owns them; the runner reads the snapshot the
control plane exports (see inventory.py), and the local server adds its own fixture products."""
from __future__ import annotations
from dataclasses import dataclass
import os
import re
from runner import SLUG, require

REPOSITORY = re.compile(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}")
BRANCH = re.compile(r"[A-Za-z0-9][A-Za-z0-9._/-]{0,199}")
ASSET = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,99}")
WORKFLOW = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.ya?ml")
IMAGE = re.compile(r"[a-z0-9][a-z0-9.-]*(?::[0-9]{1,5})?(?:/[a-z0-9][a-z0-9._-]*)+")


@dataclass(frozen=True)
class Release:
    """Where a product's published releases and commit builds come from."""
    # The asset every published GitHub release carries, such as `foreverpin-release.tar.gz`.
    asset: str
    # Service name -> image repository; the bundle's digests must come from exactly these.
    images: tuple[tuple[str, str], ...]
    # The workflow that builds a commit into a `bundle-<sha>` artifact; without one, only releases deploy.
    workflow: str | None = None


@dataclass(frozen=True)
class Product:
    slug: str
    name: str
    description: str
    # The GitHub repository that defines the product, as `owner/name`.
    repository: str
    default_branch: str = "main"
    # Without a release source, the product deploys only bundles imported by hand.
    release: Release | None = None


# Installed from the inventory snapshot; empty until the control plane has exported one.
PRODUCTS: tuple[Product, ...] = ()

# The products the local server's fixture targets deploy. Used only on the local server, and only where the
# inventory lacks the slug, so a database row always wins.
LOCAL_PRODUCTS: tuple[Product, ...] = (
    Product("foreverpin", "ForeverPin",
            "Styled QR codes and short links whose destination can change after printing.",
            "sulton-max/10x-venture-forever-pin",
            release=Release("foreverpin-release.tar.gz",
                            (("management", "ghcr.io/sulton-max/10x-venture-forever-pin/management"),
                             ("redirect", "ghcr.io/sulton-max/10x-venture-forever-pin/redirect")),
                            workflow="publish-docker-image.yml")),
    # Its console image is private, so it deploys `rehearse.py self` imports until targets can pull private images.
    Product("wheelhouse", "Wheelhouse",
            "The portfolio's deploy and operations control plane.",
            "wow-two-platform/wow-two-platform.wheelhouse"),
)


def rehearsal():
    # An explicit operator switch for the local server; never set in a deployed control plane.
    return os.environ.get("WHEELHOUSE_REHEARSAL") in ("1", "network")


def products():
    """Validates the inventory's products, plus the local server's fixtures it lacks, in that order."""
    known = {product.slug for product in PRODUCTS}
    catalog = PRODUCTS + (tuple(item for item in LOCAL_PRODUCTS if item.slug not in known) if rehearsal() else ())
    require(len({product.slug for product in catalog}) == len(catalog), "Duplicate product slug")
    for product in catalog:
        require(SLUG.fullmatch(product.slug), "Invalid product slug")
        require(0 < len(product.name.strip()) <= 80, "Invalid product name")
        require(len(product.description.strip()) <= 200, "Invalid product description")
        require(REPOSITORY.fullmatch(product.repository), "Invalid product repository")
        require(BRANCH.fullmatch(product.default_branch), "Invalid default branch")
        if product.release is not None:
            release = product.release
            services = [service for service, _ in release.images]
            require(ASSET.fullmatch(release.asset), "Invalid release asset")
            require(release.workflow is None or WORKFLOW.fullmatch(release.workflow), "Invalid build workflow")
            require(len(set(services)) == len(services) and all(SLUG.fullmatch(name) for name in services)
                    and all(IMAGE.fullmatch(image) for _, image in release.images), "Invalid release services")
    return catalog


def product(slug):
    """Returns the product the inventory names `slug`; anything else is refused."""
    found = next((item for item in products() if item.slug == slug), None)
    require(found is not None, "Product is not in the inventory")
    return found
