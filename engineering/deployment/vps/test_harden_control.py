"""harden-control.sh against a stand-in host: ufw, sshd, the deploy account's keys and the tailnet are functions over a
scratch folder, so the script's own decisions run without root or a real firewall. `rehearse.py harden` runs it on a
real Ubuntu host with real UFW."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

KIT = Path(__file__).resolve().parent
CONSOLE_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIConsoleRunnerKeyConsoleRunnerKeyConsoleRunnerK"
CI_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAICiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiKeyCiK"

# The stand-in host. $STATE holds ufw's added rules (`added`), its defaults and whether it is active; sshd -T reports
# what harden_ssh wrote; $CONTROL, $TAILNET and $SESSION choose the host's network, tailnet and open SSH sessions.
HOST = r"""
. "$KIT/harden-control.sh"
ufw_defaults=$STATE/ufw-defaults
upgrades_policy=$STATE/upgrades
deploy_keys=$STATE/authorized_keys
docker() { [[ $CONTROL == yes && "$1 $2 $3" == "network inspect control" ]]; }
id() { case "$1" in -gn) echo deploy ;; *) return 0 ;; esac; }
chown() { :; }
ufw() {
    local line=ufw previous='' argument
    case "$1" in
        status) if [[ -e $STATE/active ]]; then echo 'Status: active'; else echo 'Status: inactive'; fi; [[ ${2:-} != verbose ]] || cat "$STATE/added" ;;
        show) echo "Added user rules (see 'ufw status' for running firewall):"; cat "$STATE/added" ;;
        default)
            local key=DEFAULT_INPUT_POLICY value=DROP
            [[ $3 == incoming ]] || key=DEFAULT_OUTPUT_POLICY
            [[ $2 == deny ]] || value=ACCEPT
            { grep -v "^$key=" "$ufw_defaults" || true; echo "$key=\"$value\""; } > "$STATE/defaults.new"
            mv "$STATE/defaults.new" "$ufw_defaults" ;;
        allow)
            for argument in "$@"; do
                if [[ $previous == comment ]]; then line+=" '$argument'"; else line+=" $argument"; fi
                previous=$argument
            done
            grep -qxF -- "$line" "$STATE/added" || echo "$line" >> "$STATE/added" ;;
        --force)
            case "$2" in
                enable) touch "$STATE/active" ;;
                delete) { grep -v "^ufw allow ${4:-} " "$STATE/added" || true; } > "$STATE/added.new"; mv "$STATE/added.new" "$STATE/added" ;;
            esac ;;
    esac
    echo "ufw $*" >> "$STATE/calls"
}
harden_ssh() { echo 'passwordauthentication no
kbdinteractiveauthentication no
permitrootlogin no' > "$STATE/sshd"; }
sshd() { cat "$STATE/sshd" 2>/dev/null || echo 'passwordauthentication yes'; }
enable_security_upgrades() { echo 'Unattended-Upgrade::Automatic-Reboot "false";' > "$upgrades_policy"; }
tailnet_state() { if [[ $TAILNET == yes ]]; then echo 'Running tag:control'; else echo Stopped; fi; }
ip() { [[ $TAILNET == yes ]]; }
ss() { printf '%s' "$SESSION"; }
"""

TAILNET_SESSION = "0 0 100.107.84.92:22 100.119.108.22:50122\n"
PUBLIC_SESSION = "0 0 203.0.113.10:22 198.51.100.7:51544\n"


class HardenControlTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.state = Path(self.temp.name)
        (self.state / "added").write_text("")
        (self.state / "ufw-defaults").write_text('IPV6=yes\nDEFAULT_INPUT_POLICY="ACCEPT"\nDEFAULT_OUTPUT_POLICY="ACCEPT"\n')
        self.keys = self.state / "authorized_keys"
        self.keys.write_text(f"restrict {CI_KEY}\nrestrict {CONSOLE_KEY}\n# owner note\n")

    def harden(self, *arguments, control="yes", tailnet="yes", session=TAILNET_SESSION):
        steps = "harden_arguments " + " ".join(arguments) + "; harden_run"
        return subprocess.run(["bash", "-c", HOST + steps], capture_output=True, text=True,
                              env={**os.environ, "KIT": str(KIT), "STATE": str(self.state), "CONTROL": control,
                                   "TAILNET": tailnet, "SESSION": session})

    def added(self):
        return (self.state / "added").read_text().splitlines()

    def test_check_on_an_unhardened_host_names_every_change_and_changes_nothing(self):
        result = self.harden("--check")
        self.assertEqual(1, result.returncode, result.stderr)
        lines = result.stdout.splitlines()
        for expected in ("change  firewall: enable it", "change  default policy: deny in, allow out",
                         "change  add: allow in on tailscale0 to any port 22 proto tcp comment 'ssh over the tailnet'",
                         "change  add: allow in on tailscale0 to any port 443,8443 proto tcp comment 'consoles over the tailnet'",
                         "change  add: allow from 172.30.1.0/24 to 172.30.0.1 port 22 proto tcp comment 'console runners'",
                         "change  add: allow 41641/udp comment 'tailscale direct'",
                         # Enabling the firewall must never cut the session that runs it: public SSH is kept.
                         "change  add: allow 22/tcp comment 'public ssh'",
                         "change  sshd passwordauthentication no", "change  unattended security updates, no reboots"):
            self.assertIn(expected, lines)
        self.assertEqual([], self.added())
        self.assertFalse((self.state / "active").exists())
        self.assertFalse((self.state / "calls").read_text().count("allow"))

    def test_the_first_run_turns_the_firewall_on_with_public_ssh_kept_and_then_converges(self):
        self.assertEqual(0, self.harden().returncode)
        self.assertTrue((self.state / "active").exists())
        self.assertIn("ufw allow 22/tcp comment 'public ssh'", self.added())
        self.assertIn("ufw allow from 172.30.1.0/24 to 172.30.0.1 port 22 proto tcp comment 'console runners'",
                      self.added())
        check = self.harden("--check")
        self.assertEqual(0, check.returncode, check.stdout)
        before = self.added()
        self.assertEqual(0, self.harden().returncode)
        self.assertEqual(before, self.added(), "a rerun changes nothing")
        # Keys stay as they are until public SSH closes: CI's key still works through the trial.
        self.assertIn(f"restrict {CI_KEY}", self.keys.read_text())

    def test_closing_public_ssh_needs_the_tailnet_and_a_session_over_it(self):
        self.harden()
        for tailnet, session, refusal in (("no", TAILNET_SESSION, "Tailscale is not running"),
                                          ("yes", PUBLIC_SESSION, "Open an SSH session over the tailnet first"),
                                          ("yes", "", "Open an SSH session over the tailnet first")):
            with self.subTest(refusal=refusal):
                result = self.harden("--tailnet-only-ssh", tailnet=tailnet, session=session)
                self.assertEqual(1, result.returncode)
                self.assertIn(refusal, result.stderr)
                self.assertIn("ufw allow 22/tcp comment 'public ssh'", self.added())
                self.assertNotIn("from=", self.keys.read_text())

    def test_closing_public_ssh_pins_the_runner_keys_and_a_plain_rerun_keeps_it_closed(self):
        self.harden()
        self.assertEqual(1, self.harden("--check", "--tailnet-only-ssh").returncode)
        self.assertEqual(0, self.harden("--tailnet-only-ssh").returncode)
        self.assertNotIn("ufw allow 22/tcp comment 'public ssh'", self.added())
        self.assertIn("ufw allow in on tailscale0 to any port 22 proto tcp comment 'ssh over the tailnet'", self.added())
        self.assertEqual(f'restrict,from="172.30.1.0/24" {CI_KEY}\nrestrict,from="172.30.1.0/24" {CONSOLE_KEY}\n'
                         "# owner note\n", self.keys.read_text())
        check = self.harden("--check", "--tailnet-only-ssh")
        self.assertEqual(0, check.returncode, check.stdout)
        self.assertIn("ok      runner keys pinned to 172.30.1.0/24", check.stdout.splitlines())
        # A rerun pins nothing twice, and a plain run never reopens public SSH.
        self.harden("--tailnet-only-ssh")
        self.assertEqual(2, self.keys.read_text().count("from="))
        self.harden()
        self.assertNotIn("ufw allow 22/tcp comment 'public ssh'", self.added())
        self.assertEqual(0, self.harden("--check").returncode)

    def test_public_ssh_reopens_from_the_kvm_console_without_the_tailnet(self):
        self.harden()
        self.harden("--tailnet-only-ssh")
        result = self.harden("--public-ssh", tailnet="no", session="")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("ufw allow 22/tcp comment 'public ssh'", self.added())

    def test_it_refuses_a_host_that_is_not_the_control_host(self):
        result = self.harden("--check", control="no")
        self.assertEqual(1, result.returncode)
        self.assertIn("not a Wheelhouse control host", result.stderr)

    def test_an_owner_rule_it_does_not_manage_is_kept_and_named(self):
        self.harden()
        with (self.state / "added").open("a") as stream:
            stream.write("ufw allow 9100/tcp comment 'node exporter'\n")
        result = self.harden("--check")
        self.assertEqual(0, result.returncode)
        self.assertIn("note    other rule kept: allow 9100/tcp comment 'node exporter'", result.stdout.splitlines())

    def test_it_takes_one_ssh_mode_and_needs_root(self):
        for arguments in (["--tailnet-only-ssh", "--public-ssh"], ["--bogus"]):
            with self.subTest(arguments=arguments):
                result = subprocess.run(["bash", str(KIT / "harden-control.sh"), *arguments], capture_output=True,
                                        text=True)
                self.assertEqual(2, result.returncode)
        if os.geteuid() != 0:
            result = subprocess.run(["bash", str(KIT / "harden-control.sh"), "--check"], capture_output=True, text=True)
            self.assertEqual((2, ""), (result.returncode, result.stdout))

    def test_sourcing_it_defines_its_steps_without_running_them(self):
        result = subprocess.run(["bash", "-c", '. "$1"; declare -F | awk "{print \\$3}"', "_",
                                 str(KIT / "harden-control.sh")], capture_output=True, text=True, check=True)
        self.assertLessEqual({"harden_main", "harden_run", "report", "apply_firewall", "pin_runner_keys",
                              "require_tailnet_session", "harden_ssh", "enable_security_upgrades"},
                             set(result.stdout.split()))


if __name__ == "__main__":
    unittest.main()
