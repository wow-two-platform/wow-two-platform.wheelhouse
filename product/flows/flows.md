# Wheelhouse — Flows

*Last updated: 2026-10-17*

- **Build:** push to any branch → CI builds only the services whose paths changed → a candidate bundle
  (`sha-<commit>`) lands as an Actions artifact. A commit without a build can be built from Wheelhouse.
- **Release:** tag the chosen commit `vX.Y.Z` and publish the GitHub release → changed services build as `X.Y.Z`,
  unchanged ones keep their images and versions → the bundle attaches to the release.
- **Promote:** deploy the `main` release to dev → deploy the same release to prod, which takes only a release that
  succeeded on dev. Typing the target ID skips the dev pass.
- **Deploy:** choose the environment and the build → confirm (prod on the local server needs the typed target ID) →
  follow the steps: check target, pull, start, verify, publish sites, probe sites, remove unused images →
  open each site from the result.
- **Recover:** inspect the target and its steps → read a service's logs → reconcile the locked rollout →
  redeploy a verified release, or explicitly recover data → verify health.
  The operator runner works without the dashboard. Database restoration is never automatic.
- **Account for changes:** open Activity → every deploy, build, reconcile, secret and product change with its operator
  and outcome → the chain status shows whether an entry was edited after it was written.
- **Manage secrets:** choose a vault → namespace per product environment → add or rotate values (write-only) →
  mint a product token, copy it once into the product's mounted settings → revoke tokens when rotating access.
- **Integrate a VPS:** verify the provider/host → add its provider enum/integration if needed →
  add host and environment bindings in code → mount credentials → test and rebuild Wheelhouse.
  No server-registration action appears in the UI.
- **Move a workload:** prepare another code-owned target → copy/restore data and keys → verify →
  perform an explicit cutover. Selecting another host does not transfer persistent data.

Future domain purchasing, DNS changes, backup automation and teardown need their own reviewed flows.
Execution details: [deployment operations](../../engineering/deployment/deployment.md).
