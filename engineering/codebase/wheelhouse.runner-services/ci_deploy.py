#!/usr/bin/env python3
"""Deploy one tested main push through pinned SSH and the target's durable runner."""
import json
import os
from pathlib import Path
import re
import shlex
import sys
import tempfile
import time

import artifacts
import ci_artifacts
import runner
from transport import Ssh

REPOSITORY = 'wow-two-platform/wow-two-platform.wheelhouse'
TARGET = '/srv/wheelhouse/config/wheelhouse-dev.json'


def is_current(commit):
    ref = json.loads(artifacts.fetch(artifacts.API + REPOSITORY + '/git/ref/heads/main'))
    return ref.get('object', {}).get('sha') == commit


def write_private(path, value):
    path.write_text(value)
    path.chmod(0o600)
    return str(path)


def credentials(directory):
    for name in ('DEV_SSH_HOST', 'DEV_SSH_USER', 'DEV_SSH_KEY', 'DEV_SSH_KNOWN_HOSTS'):
        runner.require(os.environ.get(name, '').strip(), 'Missing environment secret: ' + name)
    return {'host': os.environ['DEV_SSH_HOST'], 'user': os.environ['DEV_SSH_USER'],
            'port': int(os.environ.get('DEV_SSH_PORT', '22')),
            'keyFile': write_private(directory / 'identity', os.environ['DEV_SSH_KEY'].rstrip() + '\n'),
            'knownHostsFile': write_private(directory / 'known_hosts', os.environ['DEV_SSH_KNOWN_HOSTS'].rstrip() + '\n')}


def follow(ssh, command, timeout=1200, interval=10):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        observed = json.loads(ssh.run(shlex.join([*command, 'status'])))
        status = observed.get('status')
        if status != last:
            print(json.dumps({'deployment': observed.get('id'), 'status': status}), flush=True)
            last = status
        if status in runner.TERMINAL and observed.get('completedAt'):
            runner.require(status == 'succeeded', 'Target deployment failed; inspect its durable journal')
            return observed
        time.sleep(interval)
    raise runner.CommandFailed('CI observation timed out; the target job remains authoritative and was not resubmitted')


def deploy(directory):
    commit = os.environ.get('GITHUB_SHA', '')
    run_id = os.environ.get('GITHUB_RUN_ID', '')
    attempt = os.environ.get('GITHUB_RUN_ATTEMPT', '')
    runner.require(os.environ.get('GITHUB_REPOSITORY', '').lower() == REPOSITORY, 'Unexpected repository')
    runner.require(os.environ.get('GITHUB_EVENT_NAME') == 'push'
                   and os.environ.get('GITHUB_REF') == 'refs/heads/main', 'Only main pushes can deploy dev')
    runner.require(runner.SHA.fullmatch(commit), 'Invalid GitHub SHA')
    runner.require(re.fullmatch(r'[1-9][0-9]{0,19}', run_id)
                   and re.fullmatch(r'[1-9][0-9]{0,5}', attempt), 'Invalid GitHub run identity')
    runner.require(os.environ.get('GH_TOKEN'), 'GitHub read token is required')
    os.environ['WHEELHOUSE_GITHUB_TOKEN_FILE'] = write_private(directory / 'github_token', os.environ['GH_TOKEN'])
    if not is_current(commit):
        return {'status': 'superseded', 'testedCommit': commit}
    root = Path(__file__).resolve().parents[3]
    bundle = directory / 'bundle'
    manifest = ci_artifacts.resolve_bundle(REPOSITORY, commit, bundle, root, root / '.pipelines/generator/release.py')
    ssh = Ssh(credentials(directory))
    remote = '/srv/wheelhouse/ci/uploads/' + run_id + '-' + attempt
    ssh.run(shlex.join(['mkdir', '-p', '-m', '700', remote]))
    ssh.copy([bundle / 'release.json', bundle / 'compose.json', Path(__file__).with_name('runner.py'),
              Path(__file__).with_name('ci_remote.py')], remote)
    command = ['python3', remote + '/ci_remote.py', '--target', TARGET,
               '--run-id', run_id, '--commit', commit]
    # Check again immediately before mutation. A newer push after launch waits for this job to finish.
    if not is_current(commit):
        return {'status': 'superseded', 'testedCommit': commit}
    launched = json.loads(ssh.run(shlex.join([*command, 'launch', '--bundle', remote]), timeout=120))
    print(json.dumps({'deployment': launched['id'], 'resumed': launched['resumed'],
                      'release': manifest['release'], 'sourceCommit': manifest['sourceCommit']}), flush=True)
    follow(ssh, command)
    return json.loads(ssh.run(shlex.join([*command, 'verify']), timeout=120))


def main():
    try:
        with tempfile.TemporaryDirectory(prefix='wheelhouse-ci-') as temporary:
            result = deploy(Path(temporary))
        print(json.dumps(result))
        summary = os.environ.get('GITHUB_STEP_SUMMARY')
        if summary:
            with open(summary, 'a') as stream:
                stream.write('Wheelhouse dev: `' + result.get('status', 'unknown') + '`\n')
                if result.get('verified'):
                    stream.write('Verified digest, container health, trusted HTTPS and anonymous MCP rejection.\n')
                    stream.write('Authenticated browser/MCP acceptance and backup restoration remain separate checks.\n')
        return 0
    except Exception as error:
        print(json.dumps(runner.rejection(error)), file=sys.stderr)
        return 1
    finally:
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE', None)


if __name__ == '__main__':
    sys.exit(main())
