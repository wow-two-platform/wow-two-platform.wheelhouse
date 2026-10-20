#!/usr/bin/env python3
"""Infrastructure actions Wheelhouse prod runs: Tailscale through the owner's OAuth client (nodes, the access policy
after a reviewed diff), enrolling a fresh server, placing a product's databases and private sites on a server, and
bringing an enrolled host to the current kit (host upkeep).

Each action prints one JSON document; a refusal prints {"status": "rejected", "reason": ...} on stderr and exits 1, as
transport.py does. Long jobs (`enroll`, `place`, `upkeep`) record their journal and start `enroll-run`, `place-run` or
`upkeep-run` detached; the console passes --no-launch and starts that run as its own child instead. Nothing here prints
a secret."""
import argparse
import json
from pathlib import Path
import sys
import enroll
import infra_jobs
import inventory
import placement
import tailnet
import upkeep
from runner import rejection


def parse_site(value):
    site, _, node = value.partition("=")
    return {"site": site, "node": node}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["tailnet", "policy", "policy-apply", "policy-revert", "enrollment",
                                           "enrollment-key", "enroll", "enroll-run", "placements", "place",
                                           "place-run", "upkeeps", "upkeep", "upkeep-run", "finished",
                                           "acknowledge"])
    parser.add_argument("--root", required=True)
    parser.add_argument("--server")
    parser.add_argument("--product")
    parser.add_argument("--job")
    parser.add_argument("--actor", default="operator")
    parser.add_argument("--etag", help="policy-apply, policy-revert: the live policy's ETag the owner reviewed")
    parser.add_argument("--digest", help="policy-apply: the rendered policy's SHA-256 the owner reviewed")
    parser.add_argument("--address", help="enroll: the server's public IP address")
    parser.add_argument("--port", type=int, default=22, help="enroll: its public SSH port")
    parser.add_argument("--login-user", help="enroll: root, or the image's administrator such as ubuntu")
    parser.add_argument("--admin", help="enroll: the administrator the owner signs in as afterwards")
    parser.add_argument("--admin-key", help="enroll: the owner's SSH public key for a new administrator")
    parser.add_argument("--tailnet-hostname", help="enroll: the host's name on the tailnet")
    parser.add_argument("--expected-host-key", help="enroll: the host key fingerprint from the provider's console")
    parser.add_argument("--database", action="store_true", help="place: create the product's dev and prod databases")
    parser.add_argument("--site", action="append", default=[], metavar="SITE=NODE", help="place: a private site")
    parser.add_argument("--no-launch", action="store_true",
                        help="enroll, place, upkeep: record the job only; the caller runs its -run action itself")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    try:
        inventory.install(root)
        if args.action == "tailnet":
            result = tailnet.status(root)
        elif args.action == "policy":
            result = tailnet.policy_review(root)
        elif args.action == "policy-apply":
            result = tailnet.apply_policy(root, args.etag, args.digest, args.actor)
        elif args.action == "policy-revert":
            result = tailnet.revert_policy(root, args.etag, args.actor)
        elif args.action == "enrollment":
            result = enroll.show(root, args.server)
        elif args.action == "enrollment-key":
            result = enroll.prepare_key(root, args.server)
        elif args.action == "enroll":
            result = enroll.start(root, {
                "serverId": args.server, "publicAddress": args.address, "sshPort": args.port,
                "loginUser": args.login_user, "admin": args.admin, "adminKey": args.admin_key,
                "tailnetHostname": args.tailnet_hostname, "expectedHostKey": args.expected_host_key}, args.actor,
                launch=not args.no_launch)
        elif args.action == "enroll-run":
            result = enroll.run(root, args.server)
        elif args.action == "placements":
            result = placement.listing(root, product=args.product, server=args.server)
        elif args.action == "place":
            result = placement.start(root, {"serverId": args.server, "product": args.product,
                                            "database": args.database, "sites": list(map(parse_site, args.site))},
                                     args.actor, launch=not args.no_launch)
        elif args.action == "place-run":
            result = placement.run(root, args.job)
        elif args.action == "upkeeps":
            result = upkeep.listing(root, args.server)
        elif args.action == "upkeep":
            result = upkeep.start(root, {"serverId": args.server}, args.actor, launch=not args.no_launch)
        elif args.action == "upkeep-run":
            result = upkeep.run(root, args.job)
        elif args.action == "finished":
            result = infra_jobs.finished(root)
        else:
            result = infra_jobs.acknowledge(root, args.job)
        print(json.dumps(result))
        return 0
    except Exception as error:
        print(json.dumps(rejection(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
