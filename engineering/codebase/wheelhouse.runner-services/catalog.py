"""Products as the control plane's inventory names them. The database owns them; the runner reads the snapshot the
control plane exports (see inventory.py), and the local server adds its own fixture products.

A product may span several repositories (decision 70): its primary repository and its further `components`, each with
its own release source. Every repository's deploy.yml names the product's slug, and a service name belongs to one
repository of the product only."""
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
class Component:
    """One further repository of a product built from several: its own CI, tags and release source."""
    repository: str
    # Without a release source, the repository deploys nothing; its product's releases leave it out.
    release: Release | None = None


@dataclass(frozen=True)
class Product:
    slug: str
    name: str
    description: str
    # The GitHub repository that defines the product, as `owner/name`: its primary one.
    repository: str
    default_branch: str = "main"
    # Without a release source, the product deploys only bundles imported by hand.
    release: Release | None = None
    # The product's further repositories, after its primary one, in the order its releases name them.
    components: tuple[Component, ...] = ()

    def repositories(self):
        """Every repository of the product with its release source, primary first."""
        return ((self.repository, self.release),) + tuple((item.repository, item.release) for item in self.components)

    def images(self):
        """Every service's image repository across the product's repositories, in repository order."""
        return tuple(image for _, release in self.repositories() if release is not None for image in release.images)


def short_name(product, repository):
    """A repository's name within its product, as a product release's label names it: the repository's name without
    its owner's or the product's prefix (`ocharo-hq/ocharo-studio` -> `studio`), lowercase, in slug characters."""
    owner, name = (part.lower() for part in repository.rsplit("/", 1))
    for prefix in (owner, product):
        for separator in ("-", ".", "_"):
            if name.startswith(prefix + separator) and len(name) > len(prefix) + 1:
                name = name[len(prefix) + 1:]
                break
    name = re.sub(r"[^a-z0-9-]+", "-", name).strip("-") or "repository"
    return name if name[0].isalpha() else "r" + name


# Installed from the inventory snapshot; empty until the control plane has exported one.
PRODUCTS: tuple[Product, ...] = ()

# The products the local server's fixture targets deploy. Used only on the local server, and only where the
# inventory lacks the slug, so a database row always wins.
# The rig's test secrets, which the console seeds into its built-in vault on the local server only: test values a
# deployed control plane never sees, so the rig renders them like any environment's secrets.
LOCAL_SECRETS: tuple[tuple[str, str, str, str], ...] = tuple(
    (product, environment, key, value)
    for product, values in (("foreverpin", (("Billing:SecretKey", "sk_test_local"), ("Billing:WebhookSecret", "whsec_local"))),)
    for environment in ("dev", "prod")
    for key, value in values)

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
        repositories = [repository for repository, _ in product.repositories()]
        require(all(REPOSITORY.fullmatch(repository) for repository in repositories), "Invalid product repository")
        require(len({repository.lower() for repository in repositories}) == len(repositories),
                "A product names each repository once")
        names = [short_name(product.slug, repository) for repository in repositories]
        require(len(set(names)) == len(names), "Two repositories of the product share a short name")
        owners = {}
        for repository, release in product.repositories():
            if release is None:
                continue
            services = [service for service, _ in release.images]
            require(ASSET.fullmatch(release.asset), "Invalid release asset")
            require(release.workflow is None or WORKFLOW.fullmatch(release.workflow), "Invalid build workflow")
            require(len(set(services)) == len(services) and all(SLUG.fullmatch(name) for name in services)
                    and all(IMAGE.fullmatch(image) for _, image in release.images), "Invalid release services")
            for service in services:
                # A product's services share one Compose project, so a name belongs to one repository only.
                require(service not in owners, "Service " + service + " is in both " + owners.get(service, "")
                        + " and " + repository)
                owners[service] = repository
    return catalog


def product(slug):
    """Returns the product the inventory names `slug`; anything else is refused."""
    found = next((item for item in products() if item.slug == slug), None)
    require(found is not None, "Product is not in the inventory")
    return found
