"""The deployment history projection the API filters and aggregates: every local submission with its last observation,
its bundle's services and the step a live rollout is on. Local files only; nothing here contacts a target."""
import uuid
from runner import SLUG, read_json, require

PENDING = {"submitting", "queued", "running"}
# One observation's fields the detail page shows; SSH details and remote paths never leave the control host.
OBSERVED = ("status", "reason", "failure", "startedAt", "completedAt", "mutationStarted", "steps", "sites",
            "warnings", "versions", "reconciledBy", "snapshot", "serviceRollout", "verification", "migrations")


def bundle_facts(root, bundle_id):
    """What a cached bundle declares: product, kind, branch, rollback compatibility, whether it carries schema
    migrations (null while the bundle does not say), whether it declares an end-to-end check for dev (`verifies`),
    each service's image and version and, for a release set, each repository's release (`components`), plus when its
    source published it. None when the bundle is not cached or not
    readable."""
    if not isinstance(bundle_id, str) or not SLUG.fullmatch(bundle_id):
        return None
    bundle = root / "bundles" / bundle_id
    try:
        manifest = read_json(bundle / "release.json")
        source = read_json(bundle / "source.json") if (bundle / "source.json").is_file() else {}
    except (ValueError, OSError):
        return None
    if not isinstance(manifest, dict) or not isinstance(manifest.get("images"), dict):
        return None
    versions = manifest.get("versions") if isinstance(manifest.get("versions"), dict) else {}
    services = {}
    for name, image in sorted(manifest["images"].items()):
        entry = versions.get(name) if isinstance(versions.get(name), dict) else {}
        services[name] = {"image": image if isinstance(image, str) else None,
                          "version": entry.get("version") if isinstance(entry.get("version"), str) else None}
    published = source.get("publishedAt") if isinstance(source, dict) else None
    # A release set (decision 70) names each repository's release; null for a release of one repository.
    components = manifest.get("components") if isinstance(manifest.get("components"), list) else None
    return {"product": manifest.get("product") if isinstance(manifest.get("product"), str) else None,
            "components": None if components is None else [
                {key: component.get(key) for key in ("name", "repository", "release", "kind", "sourceCommit", "services")}
                for component in components if isinstance(component, dict)],
            "kind": manifest.get("kind", "release"), "branch": manifest.get("branch"),
            "rollbackCompatible": manifest.get("rollbackCompatible") is True,
            "migrations": manifest["migrations"] if type(manifest.get("migrations")) is bool else None,
            "verifies": isinstance(manifest.get("verify"), dict),
            "services": services,
            "publishedAt": published if isinstance(published, str) else None}


def observed_of(root, job_id):
    """The target's last report on one submission, as the follower saved it; an empty dict when none is readable."""
    try:
        observed = read_json(root / "observed" / (job_id + ".json"))
    except (ValueError, OSError):
        return {}
    return observed if isinstance(observed, dict) else {}


def current_step(root, job_id, observed=None):
    """The step a live rollout is on: the running one, else the last one recorded; None before the worker starts."""
    observed = observed_of(root, job_id) if observed is None else observed
    steps = [step for step in observed.get("steps") or []
             if isinstance(step, dict) and isinstance(step.get("name"), str)]
    if not steps:
        return None
    step = next((item for item in reversed(steps) if item.get("status") == "running"), steps[-1])
    return {"name": step["name"], "status": step.get("status"), "startedAt": step.get("startedAt"),
            "index": steps.index(step) + 1}


WATCH = ("status", "startedAt", "until", "checks", "findings", "degradedAt", "completedAt")


def watch_of(root, job_id):
    """The watch after a successful rollout, as the transport's watch passes recorded it; None when it was not watched."""
    try:
        record = read_json(root / "watching" / (job_id + ".json"))
    except (ValueError, OSError):
        return None
    if not isinstance(record, dict) or not isinstance(record.get("status"), str):
        return None
    watch = {key: record[key] for key in WATCH if key in record}
    if watch["status"] == "superseded":
        watch["status"] = "replaced"  # the word a window ended by another rollout carried before the rename
    return watch


def service_rollout_of(observed):
    """How the rollout treated each service, as the target reported it: each service's change and outcome, and, from
    runners that record them, its versions from and to, why it changed, its health and when it settled. None for a
    rollout older than service-aware rollouts."""
    rollout = observed.get("serviceRollout")
    return rollout if isinstance(rollout, dict) and isinstance(rollout.get("services"), list) else None


def records(root, submissions):
    """`submissions` is the transport's merged job list; each record gains its bundle's facts, how its rollout treated
    each service (`serviceRollout`, so a list can say `1 of 4 changed`) and, while it is live, its current step. A
    bundle missing from the cache leaves product and services unknown (null)."""
    facts = {}
    result = []
    for job in submissions:
        bundle_id = job.get("bundleId")
        if bundle_id not in facts:
            facts[bundle_id] = bundle_facts(root, bundle_id)
        bundle = facts[bundle_id] or {}
        item = {**job, "product": bundle.get("product"), "kind": bundle.get("kind"), "branch": bundle.get("branch"),
                "rollbackCompatible": bundle.get("rollbackCompatible"), "migrations": bundle.get("migrations"),
                "verifies": bundle.get("verifies"), "services": bundle.get("services"),
                "publishedAt": bundle.get("publishedAt"), "components": bundle.get("components")}
        observed = observed_of(root, job["id"])
        rollout = service_rollout_of(observed)
        if rollout is not None:
            item["serviceRollout"] = rollout
        if job.get("status") in PENDING:
            item["currentStep"] = current_step(root, job["id"], observed)
        elif job.get("status") == "succeeded":
            item["watch"] = watch_of(root, job["id"])
        result.append(item)
    return result


def observation(root, job_id, observe):
    """One submission's last observation, read from local files: its steps, sites, warnings and versions.
    `observe` is the transport's merge of the observed outcome and a reconciliation receipt."""
    require(isinstance(job_id, str) and str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    path = root / "jobs" / (job_id + ".json")
    require(path.is_file(), "Unknown deployment")
    record = read_json(path)
    require(isinstance(record, dict) and record.get("id") == job_id, "Unknown deployment")
    observed = observe(root, record)
    result = {"id": job_id, "targetId": record.get("targetId"), "status": record.get("status", "unknown")}
    result.update({key: observed[key] for key in OBSERVED if key in observed})
    return result
