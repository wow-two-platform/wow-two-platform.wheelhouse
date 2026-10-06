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

    def test_ci_credentials_are_private_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'key'
            ci_deploy.write_private(path, 'PRIVATE')
            self.assertEqual(0o600, path.stat().st_mode & 0o777)


if __name__ == '__main__':
    unittest.main()
