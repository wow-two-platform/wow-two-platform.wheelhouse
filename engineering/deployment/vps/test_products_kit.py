"""The products-host kit: shell syntax, the rendered Compose file, the tailnet policy and the SSH-session guard.
Never contacts a host; the Compose check uses the local Docker CLI when there is one."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

import prepare

KIT = Path(__file__).resolve().parent


def hujson(text):
    """The policy as JSON: line comments and trailing commas removed (the policy has no // inside a string)."""
    text = re.sub(r"^\s*//.*$|\s//.*$", "", text, flags=re.M)
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", text))


class ScriptTests(unittest.TestCase):
    def test_every_script_parses(self):
        for script in sorted(KIT.glob("*.sh")):
            with self.subTest(script=script.name):
                subprocess.run(["bash", "-n", str(script)], check=True)

    def test_sourcing_the_products_bootstrap_defines_its_steps_without_running_them(self):
        result = subprocess.run(["bash", "-c", '. "$1"; declare -F | awk "{print \\$3}"', "_",
                                 str(KIT / "bootstrap-products.sh")], capture_output=True, text=True, check=True)
        steps = set(result.stdout.split())
        self.assertLessEqual({"ensure_deploy_account", "lay_out_kit", "ensure_platform_network", "write_ssh_policy",
                              "harden_ssh", "enable_security_upgrades", "join_tailnet", "configure_firewall"}, steps)

    def tailnet_session(self, lines):
        return subprocess.run(["bash", "-c", '. "$1"; tailnet_session', "_", str(KIT / "bootstrap-products.sh")],
                              input=lines, capture_output=True, text=True).returncode == 0

    def test_public_ssh_closes_only_beside_an_open_tailnet_session(self):
        public = "0 0 203.0.113.10:22 198.51.100.7:51544\n"
        self.assertFalse(self.tailnet_session(""))
        self.assertFalse(self.tailnet_session(public))
        self.assertTrue(self.tailnet_session(public + "0 0 100.101.102.103:22 100.64.0.9:50122\n"))
        self.assertTrue(self.tailnet_session("0 36 [fd7a:115c:a1e0::5]:22 [fd7a:115c:a1e0::9]:61000\n"))
        self.assertFalse(self.tailnet_session("0 0 [2001:db8::1]:22 [2001:db8::2]:61000\n"))

    def test_the_products_bootstrap_keeps_80_and_443_closed_and_passwords_off(self):
        script = (KIT / "bootstrap-products.sh").read_text()
        self.assertNotRegex(script, r"ufw allow[^\n]*\b(80|443)\b")
        for line in ("PasswordAuthentication no", "KbdInteractiveAuthentication no", "PermitRootLogin no"):
            self.assertIn("\n" + line + "\n", script)
        self.assertIn('--advertise-tags="$tailnet_tag"', script)
        self.assertIn("tailnet_tag=tag:products", script)


OWNER_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOwnerKeyOwnerKeyOwnerKeyOwnerKeyOwnerKey owner@laptop"
OTHER_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOtherKeyOtherKeyOtherKeyOtherKeyOtherK other@elsewhere"

# Stands in for the host the script changes: the account database, useradd, usermod, install and passwd are functions
# acting on a scratch directory (BSD install cannot read a pipe), so the steps run without root. $USER_UID sets an
# existing account's uid, and $PASSWORD_STATE is what `passwd --status` reports.
ADMIN_HOST = r"""
. "$KIT/create-admin.sh"
getent() {
    case "$1 $2" in
        "passwd root") echo "root:x:0:0::$ROOT_HOME:/bin/bash" ;;
        "passwd $ADMIN") [[ -e $STATE/user ]] && echo "$ADMIN:x:$USER_UID:$USER_UID::$HOMES/$ADMIN:/bin/bash" ;;
        "group sudo") echo "sudo:x:27:" ;;
        *) return 2 ;;
    esac
}
id() { case "$1" in -u) echo "$USER_UID" ;; -gn) echo "$ADMIN" ;; esac; }
have_sudo() { return 0; }
useradd() { echo "useradd $*" >> "$STATE/calls"; mkdir -p "$HOMES/$ADMIN"; touch "$STATE/user"; }
usermod() { echo "usermod $*" >> "$STATE/calls"; }
passwd() { echo "passwd $*" >> "$STATE/calls"; echo "$ADMIN ${PASSWORD_STATE:-L} 2026-10-19 0 99999 7 -1"; }
install() {
    local mode directory=false args=()
    while (($#)); do
        case "$1" in -d) directory=true; shift ;; -m) mode=$2; shift 2 ;; -o|-g) shift 2 ;; *) args+=("$1"); shift ;; esac
    done
    if $directory; then
        mkdir -p "${args[0]}"; chmod "$mode" "${args[0]}"
    else
        cat "${args[0]}" > "${args[1]}"; chmod "$mode" "${args[1]}"
    fi
}
"""


class CreateAdminTests(unittest.TestCase):
    """create-admin.sh on a root-only image: the named sudo user, root's keys, no password, nothing overwritten."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self.state, self.homes, self.root_home = base / "state", base / "home", base / "root"
        for folder in (self.state, self.homes, self.root_home / ".ssh"):
            folder.mkdir(parents=True)
        self.root_keys = self.root_home / ".ssh" / "authorized_keys"
        self.root_keys.write_text(f"# provider\n{OWNER_KEY}\n\n")

    def admin_keys(self, user="alice"):
        return self.homes / user / ".ssh" / "authorized_keys"

    def calls(self):
        calls = self.state / "calls"
        return calls.read_text().splitlines() if calls.exists() else []

    def run_script(self, user="alice", uid=1001, password_state="L", entry="create_admin"):
        steps = f'parse_arguments "{user}"; {entry}' if entry == "create_admin" else entry
        return subprocess.run(
            ["bash", "-c", ADMIN_HOST + steps], capture_output=True, text=True,
            env={**os.environ, "KIT": str(KIT), "STATE": str(self.state), "HOMES": str(self.homes),
                 "ROOT_HOME": str(self.root_home), "ADMIN": user, "USER_UID": str(uid),
                 "PASSWORD_STATE": password_state})

    def existing_user(self, keys=None, user="alice"):
        (self.homes / user / ".ssh").mkdir(parents=True)
        (self.state / "user").touch()
        if keys is not None:
            self.admin_keys(user).write_text(keys)

    def test_a_fresh_image_gets_a_sudo_admin_with_roots_keys_and_the_passwd_command(self):
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["useradd --create-home --shell /bin/bash alice", "usermod --append --groups sudo alice",
                          "passwd --status alice"], self.calls())
        self.assertEqual(OWNER_KEY + "\n", self.admin_keys().read_text())
        self.assertEqual(0o700, (self.homes / "alice" / ".ssh").stat().st_mode & 0o777)
        self.assertEqual(0o600, self.admin_keys().stat().st_mode & 0o777)
        self.assertIn("\n  passwd alice\n", result.stdout)
        self.assertIn("ssh alice@<public-ip>", result.stdout)

    def test_it_never_sets_a_password(self):
        self.run_script()
        self.assertEqual([], [call for call in self.calls() if call.startswith("passwd") and "--status" not in call])
        script = (KIT / "create-admin.sh").read_text()
        self.assertNotRegex(script, r"chpasswd|usermod[^\n]*--password|useradd[^\n]*--password|passwd\s+-d|--stdin")

    def test_a_rerun_changes_nothing_and_skips_the_command_once_a_password_exists(self):
        self.run_script()
        again = self.run_script(password_state="P")
        self.assertEqual(0, again.returncode, again.stderr)
        self.assertEqual(1, sum(call.startswith("useradd") for call in self.calls()))
        self.assertEqual(OWNER_KEY + "\n", self.admin_keys().read_text())
        self.assertIn("already has a password", again.stdout)
        self.assertNotIn("\n  passwd alice\n", again.stdout)

    def test_an_existing_user_without_keys_gets_roots_keys(self):
        self.existing_user()
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(OWNER_KEY + "\n", self.admin_keys().read_text())
        self.assertFalse(any(call.startswith("useradd") for call in self.calls()))

    def test_an_existing_user_with_other_keys_is_refused_untouched(self):
        self.existing_user(OTHER_KEY + "\n")
        result = self.run_script()
        self.assertEqual(1, result.returncode)
        self.assertIn("differ from root's", result.stderr)
        self.assertEqual(OTHER_KEY + "\n", self.admin_keys().read_text())
        self.assertEqual([], self.calls())

    def test_an_existing_user_with_only_some_of_roots_keys_is_refused(self):
        self.root_keys.write_text(f"{OWNER_KEY}\n{OTHER_KEY}\n")
        self.existing_user(OWNER_KEY + "\n")
        result = self.run_script()
        self.assertEqual(1, result.returncode)
        self.assertEqual([], self.calls())

    def test_a_user_holding_the_same_keys_in_another_order_is_left_alone(self):
        self.root_keys.write_text(f"{OWNER_KEY}\n{OTHER_KEY}\n")
        self.existing_user(f"{OTHER_KEY}\n{OWNER_KEY}\n")
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["usermod --append --groups sudo alice", "passwd --status alice"], self.calls())

    def test_it_refuses_when_root_has_no_authorized_key(self):
        for content in (None, "", "# only a comment\n\n"):
            with self.subTest(content=content):
                if content is None:
                    self.root_keys.unlink()
                else:
                    self.root_keys.write_text(content)
                result = self.run_script()
                self.assertEqual(1, result.returncode)
                self.assertIn("ssh-copy-id root@<public-ip>", result.stderr)
                self.assertEqual([], self.calls())
                self.assertFalse((self.homes / "alice").exists())

    def test_it_refuses_a_system_account_and_a_symlinked_ssh_folder(self):
        self.existing_user(OWNER_KEY + "\n")
        self.assertIn("system account", self.run_script(uid=999).stderr)
        (self.homes / "alice" / ".ssh" / "authorized_keys").unlink()
        (self.homes / "alice" / ".ssh").rmdir()
        (self.homes / "alice" / ".ssh").symlink_to(self.root_home / ".ssh")
        self.assertIn("symlink", self.run_script().stderr)
        self.assertEqual([], self.calls())

    def test_it_takes_only_a_plain_lowercase_name_that_is_not_root_or_the_deploy_account(self):
        for name in ("", "Root", "root", "wheelhouse-deploy", "1admin", "a b", "x" * 40, "ops;id"):
            with self.subTest(name=name):
                result = self.run_script(user=name)
                self.assertEqual(2, result.returncode)
                self.assertEqual([], self.calls())
        self.assertEqual(0, self.run_script(user="ops-admin").returncode)

    @unittest.skipIf(os.geteuid() == 0, "running as root would create a real account")
    def test_run_without_root_it_stops_before_changing_anything(self):
        for arguments in ([], ["alice"]):
            with self.subTest(arguments=arguments):
                result = subprocess.run(["bash", str(KIT / "create-admin.sh"), *arguments], capture_output=True, text=True)
                self.assertEqual(2, result.returncode)

    def test_sourcing_it_defines_its_steps_without_running_them(self):
        result = subprocess.run(["bash", "-c", '. "$1"; declare -F | awk "{print \\$3}"', "_",
                                 str(KIT / "create-admin.sh")], capture_output=True, text=True, check=True)
        self.assertLessEqual({"parse_arguments", "require_root", "have_sudo", "key_lines", "keys_relation",
                              "create_admin", "main"}, set(result.stdout.split()))


class ComposeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "wheelhouse"
        (self.root / "platform").mkdir(parents=True, mode=0o700)
        self.root.chmod(0o700)
        shutil.copy(KIT / "products.compose.yml", self.root / "platform" / "products.compose.yml")
        shutil.copy(KIT / "tailscale-serve-products.json", self.root / "platform" / "tailscale-serve.json")
        prepare.prepare_products(self.root, "ocharo-platform", sites=[("app", "ocharo-desk")],
                                 tailnet="tail1234.ts.net")
        self.compose = (self.root / "platform" / "compose.yml").read_text()

    def test_the_products_platform_runs_no_console_and_publishes_only_loopback(self):
        services = re.findall(r"^  ([a-z-]+):$", self.compose.split("\nservices:\n", 1)[1], re.M)
        self.assertEqual(["postgres", "ingress", "ocharo-desk-dev", "ocharo-desk"], services)
        definitions = re.sub(r"#.*", "", self.compose)
        self.assertNotIn("console", definitions)
        self.assertNotIn("control", definitions)
        published = re.findall(r"^      - ([0-9.]+):[0-9]+:[0-9]+$", self.compose, re.M)
        self.assertEqual(["127.0.0.1"], published)
        self.assertIn("ipv4_address: 172.30.0.3\n        aliases: [wheelhouse-postgres]", self.compose)
        self.assertIn("--entryPoints.private.address=:8080", self.compose)
        self.assertEqual(1, self.compose.count("--entryPoints.private.address"))

    def test_site_nodes_proxy_to_the_private_entry_point(self):
        serve = json.loads((KIT / "tailscale-serve-products.json").read_text())
        self.assertEqual({"${TS_CERT_DOMAIN}:443": {"Handlers": {"/": {"Proxy": "http://172.30.0.2:8080"}}}},
                         serve["Web"])
        self.assertIn("ipv4_address: 172.30.0.2", self.compose)
        self.assertIn("--advertise-tags=tag:product-site", self.compose)

    @unittest.skipUnless(shutil.which("docker"), "needs the Docker CLI")
    def test_the_rendered_file_is_valid_compose(self):
        result = subprocess.run(["docker", "compose", "-f", str(self.root / "platform" / "compose.yml"),
                                 "config", "--quiet"], capture_output=True, text=True,
                                env={**os.environ, "WHEELHOUSE_DEPLOY_UID": "1000", "WHEELHOUSE_DEPLOY_GID": "1000"})
        self.assertEqual(0, result.returncode, result.stderr)


class TailnetPolicyTests(unittest.TestCase):
    def setUp(self):
        self.policy = hujson((KIT / "tailnet-policy.hujson").read_text())

    def test_the_only_tagged_path_is_control_to_products_ssh(self):
        tagged = [grant for grant in self.policy["grants"] if any(src.startswith("tag:") for src in grant["src"])]
        self.assertEqual([{"src": ["tag:control"], "dst": ["tag:products"], "ip": ["tcp:22"]}], tagged)
        self.assertEqual({"tag:control", "tag:canary", "tag:products", "tag:product-site"},
                         set(self.policy["tagOwners"]))

    def test_owner_devices_reach_products_ssh_and_the_product_sites(self):
        owner = {grant["dst"][0]: grant["ip"] for grant in self.policy["grants"]
                 if grant["src"] == ["autogroup:member"]}
        self.assertEqual(["tcp:22"], owner["tag:products"])
        self.assertEqual(["tcp:443"], owner["tag:product-site"])

    def test_the_policy_tests_keep_tagged_nodes_off_the_control_host(self):
        tests = {test["src"]: test for test in self.policy["tests"]}
        self.assertEqual(["tag:products:22"], tests["tag:control"]["accept"])
        for source in ("tag:canary", "tag:products", "tag:product-site"):
            self.assertLessEqual({"tag:control:443", "tag:control:8443", "tag:control:22"},
                                 set(tests[source]["deny"]), source)
        self.assertIn("tag:product-site:443", tests["tag:control"]["deny"])


if __name__ == "__main__":
    unittest.main()
