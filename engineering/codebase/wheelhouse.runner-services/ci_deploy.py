#!/usr/bin/env python3
"""Deploy a tested main push to dev, or promote a dev-proven release to prod, through pinned SSH and the runner.

The fallback path since Wheelhouse deploys itself (decision 64): the repository variable WHEELHOUSE_SELF_DEPLOY=true
turns it off, and the workflows pass it in, so a run that reaches this script anyway refuses before any download or
SSH. Unset or false keeps it working; the owner flips it back with one `gh variable set`."""
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
import ci_remote
import runner
from transport import Ssh

REPOSITORY = 'wow-two-platform/wow-two-platform.wheelhouse'
TARGETS = {'dev': '/srv/wheelhouse/config/wheelhouse-dev.json',
           'prod': '/srv/wheelhouse/config/wheelhouse-prod.json'}
# The repository variable that hands Wheelhouse's deploys to Wheelhouse itself.
SWITCH = 'WHEELHOUSE_SELF_DEPLOY'


def self_deploys(environ=None):
    """Whether Wheelhouse deploys itself, so CI's deploy path is off: only an explicit `true` turns it off."""
    return (os.environ if environ is None else environ).get(SWITCH, '').strip().lower() == 'true'


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


def choose_promotion(typed, rollback, facts):
    """The release a promotion deploys and why. Empty input takes the newest release that succeeded on wheelhouse-dev,
    as the console's Ready to promote offers it; a typed release older than prod's needs `rollback`."""
    newest, running = facts.get('newestProven'), facts.get('prodRelease')
    if not typed:
        runner.require(not rollback, 'Name the release to roll back to')
        runner.require(newest, 'No release has succeeded on wheelhouse-dev yet; nothing to promote')
        release = newest['release']
        runner.require(not running or not ci_remote.version_of(running)
                       or ci_remote.version_of(release) >= ci_remote.version_of(running),
                       'Prod runs ' + str(running) + ', newer than ' + release + ', the newest release dev ran;'
                       ' name a release to roll back to, with rollback: true')
        return release, ('no release was given, so the newest release that succeeded on wheelhouse-dev ('
                         + str(newest.get('completedAt')) + ')'
                         + ('; prod already runs it, so it is verified without a restart' if release == running else
                            '; prod runs ' + str(running)))
    runner.require(ci_remote.VERSION.fullmatch(typed), 'Give a published release tag such as v0.5.20, or leave it empty')
    older = bool(running and ci_remote.version_of(running) and ci_remote.version_of(typed) < ci_remote.version_of(running))
    runner.require(not older or rollback, typed + ' is older than ' + str(running) + ', which prod runs; set rollback:'
                   ' true to roll back to it')
    if older:
        return typed, 'a rollback from ' + str(running) + ', as asked (rollback: true)'
    return typed, 'as given; prod runs ' + str(running) + (', so it is verified without a restart'
                                                           if typed == running else '')


def summarize(line):
    print(line, flush=True)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a') as stream:
            stream.write(line + '\n')


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
    typed = os.environ.get('PROMOTE_RELEASE', '').strip()
    # The promotion workflow says so itself, since its release input may be empty.
    promotion = os.environ.get('PROMOTE', '').strip().lower() == 'true' or bool(typed)
    rollback = os.environ.get('PROMOTE_ROLLBACK', '').strip().lower() == 'true'
    environment = 'prod' if promotion else 'dev'
    runner.require(not self_deploys(), 'Wheelhouse deploys itself (' + SWITCH + '=true): promote from the prod console,'
                   ' or set the variable to false to use this fallback')
    runner.require(os.environ.get('GITHUB_REPOSITORY', '').lower() == REPOSITORY, 'Unexpected repository')
    runner.require(os.environ.get('GITHUB_REF') == 'refs/heads/main', 'Only main can deploy Wheelhouse')
    if promotion:
        runner.require(os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch',
                       'Only the dispatched promotion workflow can deploy prod')
    else:
        runner.require(os.environ.get('GITHUB_EVENT_NAME') == 'push', 'Only main pushes can deploy dev')
    runner.require(runner.SHA.fullmatch(commit), 'Invalid GitHub SHA')
    runner.require(re.fullmatch(r'[1-9][0-9]{0,19}', run_id)
                   and re.fullmatch(r'[1-9][0-9]{0,5}', attempt), 'Invalid GitHub run identity')
    runner.require(os.environ.get('GH_TOKEN'), 'GitHub read token is required')
    os.environ['WHEELHOUSE_GITHUB_TOKEN_FILE'] = write_private(directory / 'github_token', os.environ['GH_TOKEN'])
    if not promotion and not is_current(commit):
        return {'status': 'superseded', 'testedCommit': commit}
    bundle = directory / 'bundle'
    if not promotion:
        root = Path(__file__).resolve().parents[3]
        manifest = ci_artifacts.resolve_bundle(REPOSITORY, commit, bundle, root,
                                               root / '.pipelines/generator/release.py')
    ssh = Ssh(credentials(directory))
    remote = '/srv/wheelhouse/ci/uploads/' + run_id + '-' + attempt
    ssh.run(shlex.join(['mkdir', '-p', '-m', '700', remote]))
    ssh.copy([Path(__file__).with_name('runner.py'), Path(__file__).with_name('ci_remote.py')], remote)
    if promotion:
        # An empty input takes the newest dev-proven release; an older one than prod's needs rollback: true.
        facts = json.loads(ssh.run(shlex.join(['python3', remote + '/ci_remote.py', 'promotion',
                                               '--target', TARGETS['prod'], '--dev-target', TARGETS['dev']]),
                                   timeout=60))
        release, why = choose_promotion(typed, rollback, facts)
        summarize('Promoting `' + release + '` to wheelhouse-prod: ' + why + '.')
        manifest = ci_artifacts.resolve_release(REPOSITORY, release, bundle)
        # Prod takes only the exact release that already completed on dev.
        ssh.run(shlex.join(['python3', remote + '/ci_remote.py', 'proven', '--target', TARGETS['dev'],
                            '--release', manifest['release']]), timeout=60)
    ssh.copy([bundle / 'release.json', bundle / 'compose.json'], remote)
    command = ['python3', remote + '/ci_remote.py', '--target', TARGETS[environment],
               '--environment', environment, '--run-id', run_id, '--commit', commit]
    # Check again immediately before mutation. A newer push after launch waits for this job to finish.
    if not promotion and not is_current(commit):
        return {'status': 'superseded', 'testedCommit': commit}
    launched = json.loads(ssh.run(shlex.join([*command, 'launch', '--bundle', remote]), timeout=120))
    unchanged = launched.get('unchanged', False)
    print(json.dumps({'deployment': launched['id'], 'resumed': launched['resumed'], 'unchanged': unchanged,
                      'release': manifest['release'], 'sourceCommit': manifest['sourceCommit']}), flush=True)
    follow(ssh, command)
    return {**json.loads(ssh.run(shlex.join([*command, 'verify']), timeout=120)), 'unchanged': unchanged,
            'environment': environment}


def main():
    try:
        with tempfile.TemporaryDirectory(prefix='wheelhouse-ci-') as temporary:
            result = deploy(Path(temporary))
        print(json.dumps(result))
        summary = os.environ.get('GITHUB_STEP_SUMMARY')
        if summary:
            with open(summary, 'a') as stream:
                stream.write('Wheelhouse ' + result.get('environment', 'dev') + ': `'
                             + result.get('status', 'unknown') + '`\n')
                if result.get('unchanged'):
                    stream.write('No new release: the running release was verified without a restart.\n')
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
