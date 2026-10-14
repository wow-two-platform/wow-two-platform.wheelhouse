#!/usr/bin/env python3
"""CI adapter around the existing durable target runner for Wheelhouse dev and prod; no deployment engine of its own."""
import argparse
import fcntl
import json
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.request import Request, build_opener, HTTPRedirectHandler

import runner

ENVIRONMENTS = ('dev', 'prod')


def checked_target(path, environment='dev'):
    target = runner.read_json(path)
    runner.validate_target(target)
    runner.require(environment in ENVIRONMENTS and target['product'] == 'wheelhouse'
                   and target['environment'] == environment, 'CI can deploy only wheelhouse-dev or wheelhouse-prod')
    return target


def project_root(target):
    return Path(target['root']) / (target['product'] + '-' + target['environment'])


def proven(dev_target_path, release):
    """Prod takes only a release that completed successfully on dev; reruns and rollbacks reuse that record."""
    target = checked_target(dev_target_path, 'dev')
    runner.require(runner.RELEASE.fullmatch(release or ''), 'Invalid release')
    for path in (project_root(target) / 'jobs').glob('*.json'):
        job = runner.read_json(path)
        if job.get('release') == release and job.get('status') == 'succeeded' and job.get('completedAt'):
            return {'release': release, 'provenBy': job.get('id')}
    raise runner.Rejected('The release has not succeeded on wheelhouse-dev')


def identity(run_id, commit, manifest):
    runner.require(re.fullmatch(r'[1-9][0-9]{0,19}', run_id), 'Invalid GitHub run id')
    runner.require(runner.SHA.fullmatch(commit), 'Invalid tested commit')
    return {'runId': run_id, 'testedCommit': commit, 'sourceCommit': manifest['sourceCommit'],
            'composeSha256': manifest['composeSha256'], 'release': manifest['release']}


def receipt_path(target, run_id):
    return Path(target['root']) / 'ci' / 'receipts' / (run_id + '.json')


def launch_once(bundle, target_path, run_id, commit, environment='dev'):
    target = checked_target(target_path, environment)
    manifest = runner.validate_bundle(bundle)
    runner.validate_target(target, manifest)
    wanted = identity(run_id, commit, manifest)
    receipt = receipt_path(target, run_id)
    receipt.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    actor = 'github:wheelhouse:' + run_id
    with receipt.with_suffix('.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if receipt.exists():
            saved = runner.read_json(receipt)
            runner.require(all(saved.get(key) == value for key, value in wanted.items()),
                           'GitHub run already refers to a different bundle')
            if saved.get('jobId'):
                return {'id': saved['jobId'], 'resumed': True}
            # A disconnect between launch and receipt publication must not submit another rollout.
            jobs = project_root(target) / 'jobs'
            matches = [runner.read_json(path) for path in jobs.glob('*.json')]
            matches = [job for job in matches if job.get('actor') == actor]
            runner.require(len(matches) == 1,
                           'Uncertain CI launch requires target journal inspection; no automatic resubmission')
            saved['jobId'] = matches[0]['id']
            runner.write_json(receipt, saved)
            return {'id': saved['jobId'], 'resumed': True}
        # A push that changes no release (docs only) verifies the running release instead of restarting it.
        observed = runner.state(target)
        running = observed.get('current') or {}
        if (observed['condition'] == 'ready' and running.get('id')
                and running.get('release') == manifest['release']
                and running.get('sourceCommit') == manifest['sourceCommit']):
            runner.write_json(receipt, {**wanted, 'bundle': str(Path(bundle).resolve()), 'createdAt': runner.now(),
                                        'jobId': running['id'], 'unchanged': True})
            return {'id': running['id'], 'resumed': False, 'unchanged': True}
        checks = runner.check(target, manifest)
        runner.require(checks.get('ok') is True, 'Target readiness failed; inspect runner check privately')
        # Persist intent first. A crash before launch is deliberately ambiguous and fails closed on rerun.
        saved = {**wanted, 'bundle': str(Path(bundle).resolve()), 'createdAt': runner.now()}
        runner.write_json(receipt, saved)
        result = runner.launch(bundle, target_path, actor)
        saved['jobId'] = result['id']
        runner.write_json(receipt, saved)
        return {'id': result['id'], 'resumed': False}


def observe(target_path, run_id, commit, environment='dev'):
    target = checked_target(target_path, environment)
    runner.require(re.fullmatch(r'[1-9][0-9]{0,19}', run_id), 'Invalid GitHub run id')
    saved = runner.read_json(receipt_path(target, run_id))
    runner.require(saved['testedCommit'] == commit, 'Receipt does not match tested source')
    record = runner.status(target, saved['jobId'])
    # Do not relay site hosts, settings paths, exception output or logs into public CI.
    return {key: record.get(key) for key in ('id', 'status', 'completedAt', 'release', 'sourceCommit')}


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def http_status(request):
    try:
        with build_opener(NoRedirect()).open(request, timeout=20) as response:
            return response.status
    except HTTPError as error:
        return error.code


def verify(target_path, run_id, commit, environment='dev'):
    target = checked_target(target_path, environment)
    observed = observe(target_path, run_id, commit, environment)
    runner.require(observed['status'] == 'succeeded' and observed['completedAt'],
                   'Deployment has not completed successfully')
    saved = runner.read_json(receipt_path(target, run_id))
    manifest = runner.validate_bundle(saved['bundle'])
    runner.require(all(saved.get(key) == value for key, value in identity(run_id, commit, manifest).items()),
                   'CI receipt bundle changed')
    with runner.deployment_lock(project_root(target)):
        state = runner.state(target)
        runner.require(state['condition'] == 'ready' and state['current']['id'] == observed['id'],
                       'Target changed since this CI deployment')
        runner.Docker(target, runner.environment_for(target, manifest)).verify(saved['bundle'], manifest)
        ingress = runner.target_ingress(target)
        host = (ingress or {}).get('hosts', {}).get('console', '')
        runner.require(runner.HOST.fullmatch(host) and ingress['scheme'] == 'https',
                       'Private HTTPS hostname is required')
        port = ingress.get('port')
        origin = 'https://' + host + (':' + str(port) if port and port != 443 else '')
        # TLS verification is deliberately separate from the runner's route-only probes.
        runner.require(http_status(Request(origin + '/api/system/ready')) == 200,
                       'Trusted HTTPS readiness failed')
        request = Request(origin + '/mcp', method='POST',
                          data=json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize',
                                           'params': {}}).encode(),
                          headers={'Content-Type': 'application/json',
                                   'Accept': 'application/json, text/event-stream'})
        runner.require(http_status(request) == 401, 'MCP must reject an unauthenticated initialization')
    return {**observed, 'verified': True, 'testedCommit': commit}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['launch', 'status', 'verify', 'proven'])
    parser.add_argument('--target', required=True)
    parser.add_argument('--environment', choices=ENVIRONMENTS, default='dev')
    parser.add_argument('--bundle')
    parser.add_argument('--release')
    parser.add_argument('--run-id')
    parser.add_argument('--commit')
    args = parser.parse_args()
    try:
        runner.require(args.action == 'proven' or (args.run_id and args.commit), 'Give the GitHub run id and commit')
        if args.action == 'proven':
            result = proven(args.target, args.release)
        elif args.action == 'launch':
            result = launch_once(args.bundle, args.target, args.run_id, args.commit, args.environment)
        elif args.action == 'status':
            result = observe(args.target, args.run_id, args.commit, args.environment)
        else:
            result = verify(args.target, args.run_id, args.commit, args.environment)
        print(json.dumps(result))
        return 0
    except Exception as error:
        print(json.dumps(runner.rejection(error)), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
