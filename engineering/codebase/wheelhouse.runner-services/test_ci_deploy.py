import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import uuid

import ci_deploy
import ci_remote
import runner


class CiRemoteTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.target = {'product': 'wheelhouse', 'environment': 'dev', 'root': str(self.root / 'state'),
                       'ingress': {'scheme': 'https', 'hosts': {'console': 'wheelhouse.example.ts.net'}}}
        self.target_path = self.root / 'target.json'
        self.target_path.write_text(json.dumps(self.target))
        self.manifest = {'product': 'wheelhouse', 'release': 'v0.3.5', 'sourceCommit': 'a' * 40,
                         'composeSha256': 'b' * 64}
        self.commit = 'c' * 40
        self.job = str(uuid.uuid4())
        self.bundle = self.root / 'bundle'
        self.patch('validate_bundle', return_value=self.manifest)
        self.check = self.patch('check', return_value={'ok': True})
        self.launch = self.patch('launch', return_value={'id': self.job})

    def patch(self, name, **kwargs):
        patched = patch.object(runner, name, **kwargs)
        self.addCleanup(patched.stop)
        return patched.start()

    def start(self):
        return ci_remote.launch_once(self.bundle, self.target_path, '123', self.commit)

    def test_rerun_observes_the_original_job_without_resubmitting(self):
        self.assertFalse(self.start()['resumed'])
        self.assertEqual({'id': self.job, 'resumed': True}, self.start())
        self.launch.assert_called_once()
        self.check.assert_called_once()

    def test_crash_after_launch_recovers_matching_durable_job(self):
        def interrupted(*args):
            runner.write_json(Path(self.target['root']) / 'wheelhouse-dev/jobs' / (self.job + '.json'),
                              {'id': self.job, 'actor': 'github:wheelhouse:123', 'status': 'queued'})
            raise OSError('connection lost')
        self.launch.side_effect = interrupted
        with self.assertRaises(OSError):
            self.start()
        self.assertEqual({'id': self.job, 'resumed': True}, self.start())
        self.launch.assert_called_once()

    def test_uncertain_intent_does_not_submit_a_second_job(self):
        self.launch.side_effect = OSError('connection lost before launch')
        with self.assertRaises(OSError):
            self.start()
        with self.assertRaisesRegex(runner.Rejected, 'Uncertain CI launch'):
            self.start()
        self.launch.assert_called_once()

    def test_same_run_cannot_switch_tested_commit_or_bundle(self):
        self.start()
        self.manifest['composeSha256'] = 'd' * 64
        with self.assertRaisesRegex(runner.Rejected, 'different bundle'):
            self.start()
        self.launch.assert_called_once()

    def test_unchanged_release_is_verified_without_a_restart(self):
        running = {'id': 'running-job', 'release': 'v0.3.5', 'sourceCommit': 'a' * 40}
        self.patch('state', return_value={'condition': 'ready', 'current': running})
        self.assertEqual({'id': 'running-job', 'resumed': False, 'unchanged': True}, self.start())
        self.assertEqual({'id': 'running-job', 'resumed': True}, self.start())
        self.launch.assert_not_called()
        self.check.assert_not_called()
        with patch.object(runner, 'status', return_value={'id': 'running-job', 'status': 'succeeded',
                                                        'completedAt': 'then'}), \
                patch.object(runner, 'environment_for', return_value={}), \
                patch.object(runner, 'Docker') as docker, \
                patch.object(ci_remote, 'http_status', side_effect=[200, 401]):
            self.assertTrue(ci_remote.verify(self.target_path, '123', self.commit)['verified'])
            docker.return_value.verify.assert_called_once()

    def test_new_release_or_unsettled_target_still_launches(self):
        for state in ({'condition': 'ready', 'current': {'id': 'old', 'release': 'v0.3.4', 'sourceCommit': 'a' * 40}},
                      {'condition': 'needs_reconciliation',
                       'current': {'id': 'old', 'release': 'v0.3.5', 'sourceCommit': 'a' * 40}}):
            with self.subTest(state=state['condition']), patch.object(runner, 'state', return_value=state):
                ci_remote.receipt_path(self.target, '123').unlink(missing_ok=True)
                self.launch.reset_mock()
                self.assertFalse(self.start().get('unchanged', False))
                self.launch.assert_called_once()

    def test_preflight_failure_does_not_write_intent_or_launch(self):
        self.check.return_value = {'ok': False}
        with self.assertRaisesRegex(runner.Rejected, 'Target readiness failed'):
            self.start()
        self.launch.assert_not_called()
        self.assertFalse(ci_remote.receipt_path(self.target, '123').exists())

    def test_production_target_is_refused(self):
        self.target['environment'] = 'prod'
        self.target_path.write_text(json.dumps(self.target))
        with self.assertRaisesRegex(runner.Rejected, 'only wheelhouse-dev'):
            self.start()
        self.launch.assert_not_called()

    def test_success_requires_completed_at_and_current_job_identity(self):
        self.start()
        with patch.object(runner, 'status', return_value={'id': self.job, 'status': 'succeeded'}):
            with self.assertRaisesRegex(runner.Rejected, 'not completed successfully'):
                ci_remote.verify(self.target_path, '123', self.commit)
        with patch.object(runner, 'status', return_value={'id': self.job, 'status': 'succeeded', 'completedAt': 'now'}), \
                patch.object(runner, 'state', return_value={'condition': 'ready', 'current': {'id': 'different'}}):
            with self.assertRaisesRegex(runner.Rejected, 'Target changed'):
                ci_remote.verify(self.target_path, '123', self.commit)

    def test_verification_checks_running_digest_tls_and_mcp_auth(self):
        self.start()
        with patch.object(runner, 'status', return_value={'id': self.job, 'status': 'succeeded', 'completedAt': 'now'}), \
                patch.object(runner, 'state', return_value={'condition': 'ready', 'current': {'id': self.job}}), \
                patch.object(runner, 'environment_for', return_value={}), \
                patch.object(runner, 'Docker') as docker, \
                patch.object(ci_remote, 'http_status', side_effect=[200, 401]) as http:
            self.assertTrue(ci_remote.verify(self.target_path, '123', self.commit)['verified'])
            docker.return_value.verify.assert_called_once()
            self.assertEqual('https://wheelhouse.example.ts.net/api/system/ready', http.call_args_list[0].args[0].full_url)
            self.assertEqual('POST', http.call_args_list[1].args[0].get_method())

    def test_observation_does_not_emit_secrets_or_private_hosts(self):
        self.start()
        with patch.object(runner, 'status', return_value={'id': self.job, 'status': 'running',
                                                        'reason': 'PRIVATE', 'sites': [{'url': 'PRIVATE'}]}):
            self.assertNotIn('PRIVATE', json.dumps(ci_remote.observe(self.target_path, '123', self.commit)))

    def test_prod_takes_only_a_release_that_completed_on_dev(self):
        jobs = Path(self.target['root']) / 'wheelhouse-dev/jobs'
        with self.assertRaisesRegex(runner.Rejected, 'not succeeded on wheelhouse-dev'):
            ci_remote.proven(self.target_path, 'v0.3.6')
        runner.write_json(jobs / 'running.json', {'id': 'running', 'release': 'v0.3.6', 'status': 'succeeded'})
        runner.write_json(jobs / 'failed.json', {'id': 'failed', 'release': 'v0.3.6', 'status': 'failed',
                                                 'completedAt': 'then'})
        with self.assertRaisesRegex(runner.Rejected, 'not succeeded on wheelhouse-dev'):
            ci_remote.proven(self.target_path, 'v0.3.6')
        runner.write_json(jobs / 'done.json', {'id': 'done', 'release': 'v0.3.6', 'status': 'succeeded',
                                               'completedAt': 'then'})
        self.assertEqual({'release': 'v0.3.6', 'provenBy': 'done'}, ci_remote.proven(self.target_path, 'v0.3.6'))
        with self.assertRaisesRegex(runner.Rejected, 'not succeeded on wheelhouse-dev'):
            ci_remote.proven(self.target_path, 'v0.3.7')

    def test_promotion_facts_name_the_newest_dev_proven_release_by_version_and_what_prod_runs(self):
        dev_jobs = Path(self.target['root']) / 'wheelhouse-dev/jobs'
        for job, release, status in (('a', 'v0.5.9', 'succeeded'), ('b', 'v0.5.10', 'succeeded'),
                                     ('c', 'v0.5.11', 'rolled_back'), ('d', 'sha-abcdef0', 'succeeded'),
                                     ('e', 'v0.4.7', 'succeeded')):
            runner.write_json(dev_jobs / (job + '.json'), {'id': job, 'release': release, 'status': status,
                                                           'completedAt': '2026-10-1' + str(len(job)) + 'T00:00:00'})
        prod = {**self.target, 'environment': 'prod'}
        prod_path = self.root / 'prod.json'
        prod_path.write_text(json.dumps(prod))
        runner.write_json(Path(prod['root']) / 'wheelhouse-prod/current.json', {'id': 'p', 'release': 'v0.3.9'})
        facts = ci_remote.promotion_facts(prod_path, self.target_path)
        self.assertEqual(('v0.5.10', 'b', 'v0.3.9'),
                         (facts['newestProven']['release'], facts['newestProven']['job'], facts['prodRelease']))
        with self.assertRaisesRegex(runner.Rejected, 'only wheelhouse-dev or wheelhouse-prod'):
            ci_remote.promotion_facts(self.target_path, self.target_path)

    def test_prod_launch_uses_its_own_project_and_receipt(self):
        prod = {**self.target, 'environment': 'prod'}
        prod_path = self.root / 'prod.json'
        prod_path.write_text(json.dumps(prod))
        self.assertFalse(ci_remote.launch_once(self.bundle, prod_path, '456', self.commit, 'prod')['resumed'])
        self.assertTrue(ci_remote.receipt_path(prod, '456').exists())
        with self.assertRaisesRegex(runner.Rejected, 'only wheelhouse-dev or wheelhouse-prod'):
            ci_remote.launch_once(self.bundle, prod_path, '789', self.commit, 'dev')


class CiDriverTests(unittest.TestCase):
    def test_follow_waits_for_cleanup_not_only_terminal_label(self):
        ssh = Mock()
        ssh.run.side_effect = [json.dumps({'id': 'job', 'status': 'succeeded'}),
                               json.dumps({'id': 'job', 'status': 'succeeded', 'completedAt': 'now'})]
        with patch.object(ci_deploy.time, 'sleep') as sleep:
            self.assertEqual('now', ci_deploy.follow(ssh, ['python3', 'remote.py'])['completedAt'])
        self.assertEqual(2, ssh.run.call_count)
        sleep.assert_called_once()

    def test_remote_failure_is_failure_even_when_ssh_succeeded(self):
        ssh = Mock()
        ssh.run.return_value = json.dumps({'id': 'job', 'status': 'rolled_back', 'completedAt': 'now'})
        with self.assertRaisesRegex(runner.Rejected, 'Target deployment failed'):
            ci_deploy.follow(ssh, ['python3', 'remote.py'])

    def test_observation_disconnect_does_not_resubmit(self):
        # The driver delegates retries to a later workflow rerun and the durable remote receipt.
        ssh = Mock()
        ssh.run.side_effect = runner.CommandFailed('SSH operation timed out')
        with self.assertRaises(runner.CommandFailed):
            ci_deploy.follow(ssh, ['python3', 'remote.py'])
        self.assertEqual(1, ssh.run.call_count)

    def test_ref_check_compares_exact_sha(self):
        with patch.object(ci_deploy.artifacts, 'fetch', return_value=json.dumps({'object': {'sha': 'a' * 40}}).encode()):
            self.assertTrue(ci_deploy.is_current('a' * 40))
            self.assertFalse(ci_deploy.is_current('b' * 40))

    def test_promotion_runs_only_from_the_dispatched_main_workflow(self):
        base = {'GITHUB_REPOSITORY': 'wow-two-platform/wow-two-platform.wheelhouse', 'GITHUB_REF': 'refs/heads/main',
                'GITHUB_SHA': 'a' * 40, 'GITHUB_RUN_ID': '1', 'GITHUB_RUN_ATTEMPT': '1', 'GH_TOKEN': 'token'}
        for event, promote, message in (('push', 'v0.3.6', 'dispatched promotion'),
                                        ('workflow_dispatch', '', 'Only main pushes')):
            with self.subTest(event=event), tempfile.TemporaryDirectory() as directory, \
                    patch.dict(ci_deploy.os.environ, {**base, 'GITHUB_EVENT_NAME': event, 'PROMOTE_RELEASE': promote},
                               clear=True):
                with self.assertRaisesRegex(runner.Rejected, message):
                    ci_deploy.deploy(Path(directory))

    def test_promotion_proves_the_release_on_dev_before_launching_prod(self):
        env = {'GITHUB_REPOSITORY': 'wow-two-platform/wow-two-platform.wheelhouse', 'GITHUB_REF': 'refs/heads/main',
               'GITHUB_EVENT_NAME': 'workflow_dispatch', 'PROMOTE_RELEASE': 'v0.3.6', 'GITHUB_SHA': 'a' * 40,
               'GITHUB_RUN_ID': '7', 'GITHUB_RUN_ATTEMPT': '1', 'GH_TOKEN': 'token', 'DEV_SSH_HOST': 'host',
               'DEV_SSH_USER': 'user', 'DEV_SSH_KEY': 'key', 'DEV_SSH_KNOWN_HOSTS': 'known'}
        result, resolve, commands, summary = self.promote(env, {'newestProven': {'release': 'v0.3.7', 'job': 'j'},
                                                               'prodRelease': 'v0.3.5'})
        self.assertEqual('v0.3.6', resolve.call_args.args[1], 'a typed release wins over the newest dev-proven one')
        self.assertIn('promotion', commands[1])
        self.assertIn('proven', commands[2])
        self.assertIn('wheelhouse-dev.json', commands[2])
        self.assertIn('wheelhouse-prod.json', commands[3])
        self.assertIn('--environment prod', commands[3])
        self.assertEqual('prod', result['environment'])
        self.assertIn('Promoting `v0.3.6` to wheelhouse-prod: as given; prod runs v0.3.5.', summary)

    def promote(self, env, facts, release=None):
        ssh = Mock()
        ssh.run.side_effect = ['', json.dumps(facts), '', json.dumps({'id': 'job', 'resumed': False}),
                               json.dumps({'id': 'job', 'status': 'succeeded', 'completedAt': 'now'}),
                               json.dumps({'id': 'job', 'status': 'succeeded', 'verified': True})]
        with tempfile.TemporaryDirectory() as directory:
            summary = Path(directory) / 'summary.md'
            env = {**env, 'GITHUB_STEP_SUMMARY': str(summary)}
            with patch.dict(ci_deploy.os.environ, env, clear=True), \
                    patch.object(ci_deploy.ci_artifacts, 'resolve_release',
                                 side_effect=lambda repository, tag, bundle: {'release': tag,
                                                                              'sourceCommit': 'b' * 40}) as resolve, \
                    patch.object(ci_deploy, 'Ssh', return_value=ssh), \
                    patch.object(ci_deploy, 'is_current') as current:
                result = ci_deploy.deploy(Path(directory))
            current.assert_not_called()
            return result, resolve, [call.args[0] for call in ssh.run.call_args_list], summary.read_text()

    def test_an_empty_promotion_takes_the_newest_dev_proven_release_and_says_why(self):
        env = {'GITHUB_REPOSITORY': 'wow-two-platform/wow-two-platform.wheelhouse', 'GITHUB_REF': 'refs/heads/main',
               'GITHUB_EVENT_NAME': 'workflow_dispatch', 'PROMOTE': 'true', 'PROMOTE_RELEASE': '',
               'GITHUB_SHA': 'a' * 40, 'GITHUB_RUN_ID': '7', 'GITHUB_RUN_ATTEMPT': '1', 'GH_TOKEN': 'token',
               'DEV_SSH_HOST': 'host', 'DEV_SSH_USER': 'user', 'DEV_SSH_KEY': 'key', 'DEV_SSH_KNOWN_HOSTS': 'known'}
        result, resolve, _, summary = self.promote(env, {
            'newestProven': {'release': 'v0.5.21', 'job': 'j', 'completedAt': '2026-10-19T10:00:00+00:00'},
            'prodRelease': 'v0.3.9'})
        self.assertEqual('v0.5.21', resolve.call_args.args[1])
        self.assertIn('Promoting `v0.5.21` to wheelhouse-prod: no release was given, so the newest release that'
                      ' succeeded on wheelhouse-dev (2026-10-19T10:00:00+00:00); prod runs v0.3.9.', summary)

    def test_a_promotion_choice_refuses_an_older_release_unless_it_is_a_rollback(self):
        facts = {'newestProven': {'release': 'v0.5.21', 'completedAt': 'then'}, 'prodRelease': 'v0.5.20'}
        for typed, rollback, expected in (('', False, 'v0.5.21'), ('v0.5.21', False, 'v0.5.21'),
                                          ('v0.5.20', False, 'v0.5.20'), ('v0.5.19', True, 'v0.5.19'),
                                          ('v0.10.0', False, 'v0.10.0')):
            with self.subTest(typed=typed, rollback=rollback):
                self.assertEqual(expected, ci_deploy.choose_promotion(typed, rollback, facts)[0])
        self.assertIn('rollback from v0.5.20', ci_deploy.choose_promotion('v0.4.7', True, facts)[1])
        self.assertIn('verified without a restart', ci_deploy.choose_promotion('v0.5.20', False, facts)[1])
        for typed, rollback, refusal in (('v0.4.7', False, 'older than v0.5.20, which prod runs; set rollback'),
                                         ('v0.5.9', False, 'older than v0.5.20'),
                                         ('', True, 'Name the release to roll back to'),
                                         ('latest', False, 'published release tag')):
            with self.subTest(typed=typed), self.assertRaisesRegex(runner.Rejected, refusal):
                ci_deploy.choose_promotion(typed, rollback, facts)
        with self.assertRaisesRegex(runner.Rejected, 'No release has succeeded on wheelhouse-dev'):
            ci_deploy.choose_promotion('', False, {'newestProven': None, 'prodRelease': 'v0.5.20'})
        with self.assertRaisesRegex(runner.Rejected, 'Prod runs v0.6.0, newer than v0.5.21'):
            ci_deploy.choose_promotion('', False, {**facts, 'prodRelease': 'v0.6.0'})
        # A first promotion, onto a prod that runs nothing yet.
        self.assertEqual('v0.4.7', ci_deploy.choose_promotion('v0.4.7', False, {'newestProven': None,
                                                                                'prodRelease': None})[0])

    def test_the_switch_turns_ci_deploys_off_only_when_it_says_true(self):
        for value, off in (('true', True), (' TRUE ', True), ('false', False), ('', False), ('1', False), (None, False)):
            with self.subTest(value=value):
                environ = {} if value is None else {ci_deploy.SWITCH: value}
                self.assertEqual(off, ci_deploy.self_deploys(environ))

    def test_a_self_deploying_wheelhouse_refuses_ci_before_any_download_or_ssh(self):
        environ = {'GITHUB_SHA': 'c' * 40, 'GITHUB_RUN_ID': '42', 'GITHUB_RUN_ATTEMPT': '1',
                   'GITHUB_REPOSITORY': ci_deploy.REPOSITORY, 'GITHUB_REF': 'refs/heads/main',
                   'GITHUB_EVENT_NAME': 'push', 'GH_TOKEN': 'token', ci_deploy.SWITCH: 'true'}
        for promotion in ('', 'v0.5.20'):
            with self.subTest(promotion=promotion), patch.dict('os.environ', {**environ, 'PROMOTE_RELEASE': promotion}), \
                    patch.object(ci_deploy, 'is_current') as current, patch.object(ci_deploy, 'Ssh') as ssh, \
                    patch.object(ci_deploy.ci_artifacts, 'resolve_release') as resolve, \
                    tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(runner.Rejected, 'Wheelhouse deploys itself'):
                    ci_deploy.deploy(Path(directory))
                current.assert_not_called()
                ssh.assert_not_called()
                resolve.assert_not_called()

    def test_ci_credentials_are_private_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'key'
            ci_deploy.write_private(path, 'PRIVATE')
            self.assertEqual(0o600, path.stat().st_mode & 0o777)


WORKFLOWS = Path(__file__).resolve().parents[3] / '.github' / 'workflows'


@unittest.skipUnless(importlib.util.find_spec('yaml'), 'Reading the workflows needs PyYAML (CI installs it)')
class FallbackSwitchWorkflowTests(unittest.TestCase):
    """CI's deploy path stays a one-switch fallback: every job that reaches the host runs only while the repository
    variable is not `true` and hands it to ci_deploy.py, and its twin job runs only while it is."""
    ON = "vars.WHEELHOUSE_SELF_DEPLOY != 'true'"
    OFF = "vars.WHEELHOUSE_SELF_DEPLOY == 'true'"

    def jobs(self, name):
        import yaml
        return yaml.safe_load((WORKFLOWS / name).read_text())['jobs']

    def test_each_host_reaching_job_is_gated_and_passes_the_switch_on(self):
        for name, job in (('deploy-dev.yml', 'deploy'), ('promote-prod.yml', 'promote')):
            with self.subTest(workflow=name):
                jobs = self.jobs(name)
                self.assertEqual(self.ON, jobs[job]['if'])
                step = next(step for step in jobs[job]['steps'] if 'ci_deploy.py' in step.get('run', ''))
                self.assertEqual('${{ vars.WHEELHOUSE_SELF_DEPLOY }}', step['env'][ci_deploy.SWITCH])
                twin = jobs['self-deploy']
                self.assertEqual(self.OFF, twin['if'])
                self.assertNotIn('environment', twin, 'the twin never enters an environment that holds host secrets')
                self.assertFalse(any('secrets.' in str(value) for value in twin.values()))

    def test_a_dispatched_promotion_fails_loudly_while_wheelhouse_promotes_itself(self):
        script = self.jobs('promote-prod.yml')['self-deploy']['steps'][0]['run']
        self.assertIn('exit 1', script)
        self.assertIn('WHEELHOUSE_SELF_DEPLOY=false', script)

    def test_a_promotion_takes_an_optional_release_and_an_explicit_rollback(self):
        import yaml
        workflow = yaml.safe_load((WORKFLOWS / 'promote-prod.yml').read_text())
        # PyYAML reads the bare `on:` key as True.
        inputs = (workflow.get('on') or workflow[True])['workflow_dispatch']['inputs']
        self.assertEqual((False, ''), (inputs['release']['required'], inputs['release']['default']))
        self.assertEqual(('boolean', False), (inputs['rollback']['type'], inputs['rollback']['default']))
        step = next(step for step in workflow['jobs']['promote']['steps'] if 'ci_deploy.py' in step.get('run', ''))
        self.assertEqual(('true', '${{ inputs.release }}', '${{ inputs.rollback }}'),
                         (step['env']['PROMOTE'], step['env']['PROMOTE_RELEASE'], step['env']['PROMOTE_ROLLBACK']))

    def test_no_other_workflow_reaches_the_host(self):
        reaching = sorted(path.name for path in WORKFLOWS.glob('*.yml')
                          if 'DEV_SSH_KEY' in path.read_text() or 'ci_deploy.py' in path.read_text())
        self.assertEqual(['deploy-dev.yml', 'promote-prod.yml'], reaching)


if __name__ == '__main__':
    unittest.main()
