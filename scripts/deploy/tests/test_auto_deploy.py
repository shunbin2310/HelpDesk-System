"""Exercise deployment against fake Docker/HTTP in an isolated root-owned Linux fixture."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts/deploy/auto-deploy.sh"
SHA = "a" * 40
API = "sha256:" + "b" * 64
WEB = "sha256:" + "c" * 64


@unittest.skipUnless(os.name == "posix" and hasattr(os, "geteuid") and os.geteuid() == 0,
                     "Run all cases as root inside disposable Linux CI/container, never on the live server.")
class DeploymentTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="helpdesk-deploy-tests-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.opt = self.root / "opt"
        self.opt.mkdir(mode=0o755)
        self.app = self.opt / "helpdesk-auto"
        self.app.mkdir(mode=0o700)
        (self.app / ".env").write_text("PUBLIC_FIXTURE_ONLY=yes\n")
        (self.app / ".env").chmod(0o600)
        (self.app / "compose.yml").write_text("services: {}\n")
        self.log = self.root / "commands.log"
        self.log.touch()
        self.env = dict(os.environ, TEST_LOG=str(self.log),
                        PATH=str(self.bin) + ":/usr/sbin:/usr/bin:/sbin:/bin")
        self.command("hostname", '#!/bin/bash\necho "${TEST_HOSTNAME:-homelab-server}"\n')
        self.command("docker", '''#!/bin/bash
echo "$*" >> "$TEST_LOG"
if [[ $1 == update ]]; then
    [[ ${FAIL_STAGE:-} != update ]] || exit 12
elif [[ $1 == compose ]]; then
    [[ ! ( "$*" == *" config "* && ${FAIL_STAGE:-} == config ) ]] || exit 12
    [[ ! ( "$*" == *" pull" && ${FAIL_STAGE:-} == pull ) ]] || exit 12
    [[ ! ( "$*" == *" up "* && ${FAIL_STAGE:-} == up ) ]] || exit 12
    if [[ "$*" == *" ps -q api" ]]; then echo fixture-api; fi
    if [[ "$*" == *" port web 8080" ]]; then echo "${TEST_ENDPOINT:-127.0.0.1:8085}"; fi
else
    exit 99
fi
''')
        self.command("curl", '#!/bin/bash\necho "curl $*" >> "$TEST_LOG"\n[[ ${FAIL_STAGE:-} != curl ]]\n')
        body = SCRIPT.read_text(encoding="utf-8")
        replacements = {
            "APP=/opt/helpdesk-auto": "APP=" + str(self.app),
            'for directory in / /opt "$APP"; do': f'for directory in / "{self.opt}" "$APP"; do',
            "export PATH=/usr/sbin:/usr/bin:/sbin:/bin": "export PATH=" + str(self.bin) + ":/usr/sbin:/usr/bin:/sbin:/bin",
        }
        for old, new in replacements.items():
            self.assertEqual(body.count(old), 1, "Update the isolated fixture when production guards change.")
            body = body.replace(old, new)
        self.script = self.root / "deploy.sh"
        self.script.write_text(body)

    def command(self, name, body):
        path = self.bin / name
        path.write_text(body)
        path.chmod(0o755)

    def deploy(self, *arguments):
        return subprocess.run(["/bin/bash", str(self.script), *(arguments or (SHA, API, WEB))],
                              env=self.env, text=True, capture_output=True, timeout=15)

    def test_success_pulls_before_stopping_and_records_exact_images(self):
        result = self.deploy()
        self.assertEqual(result.returncode, 0, result.stderr)
        log = self.log.read_text()
        self.assertLess(log.index(" pull"), log.index(" stop api"))
        self.assertLess(log.index(" stop api"), log.index(" up "))
        self.assertIn(" up -d --force-recreate --no-build --pull never --wait", log)
        self.assertIn("http://127.0.0.1:8085/api/health", log)
        self.assertLess(log.index("curl"), log.index("update --restart unless-stopped"))
        self.assertFalse((self.app / "needs-attention").exists())
        release = (self.app / "release.env").read_text()
        self.assertIn("COMMIT_SHA=" + SHA, release)
        self.assertIn("helpdesk-api@" + API, release)
        self.assertIn("helpdesk-web@" + WEB, release)
        self.assertEqual((self.app / "release.env").stat().st_mode & 0o777, 0o600)
        self.assertNotIn("project1", log)

    def test_success_retains_previous_version(self):
        self.assertEqual(self.deploy().returncode, 0)
        previous = (self.app / "release.env").read_bytes()
        self.assertEqual(self.deploy("d" * 40, API, WEB).returncode, 0)
        self.assertEqual((self.app / "previous-release.env").read_bytes(), previous)

    def test_preflight_failure_preserves_existing_application(self):
        for stage in ("config", "pull"):
            with self.subTest(stage=stage):
                self.log.write_text("")
                self.env["FAIL_STAGE"] = stage
                self.assertNotEqual(self.deploy().returncode, 0)
                self.assertNotIn(" stop ", self.log.read_text())
                self.assertFalse((self.app / "needs-attention").exists())

    def test_failed_start_stops_candidate_keeps_old_release_and_blocks_retry(self):
        (self.app / "release.env").write_text("PREVIOUS_VERSION=yes\n")
        self.env["FAIL_STAGE"] = "up"
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertTrue((self.app / "needs-attention").exists())
        self.assertEqual(self.log.read_text().count(" stop api"), 2)
        self.assertEqual((self.app / "release.env").read_text(), "PREVIOUS_VERSION=yes\n")
        before = self.log.read_bytes()
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(self.log.read_bytes(), before)

    def test_proxy_health_failure_never_enables_restart(self):
        self.env["FAIL_STAGE"] = "curl"
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertTrue((self.app / "needs-attention").exists())
        self.assertNotIn("update --restart", self.log.read_text())
        self.assertFalse((self.app / "release.env").exists())

    def test_restart_configuration_failure_does_not_record_success(self):
        self.env["FAIL_STAGE"] = "update"
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertTrue((self.app / "needs-attention").exists())
        self.assertFalse((self.app / "release.env").exists())

    def test_invalid_endpoint_refuses_request(self):
        self.env["TEST_ENDPOINT"] = "example.com:8085"
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertNotIn("curl", self.log.read_text())

    def test_arguments_reject_shell_injection_tags_and_missing_values(self):
        for args in ((SHA, API + "; echo bad", WEB), (SHA, "latest", WEB), (SHA, API), ("main", API, WEB)):
            with self.subTest(args=args):
                self.assertEqual(self.deploy(*args).returncode, 2)
                self.assertEqual(self.log.read_text(), "")

    def test_wrong_server_is_refused(self):
        self.env["TEST_HOSTNAME"] = "another-server"
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_readable_secret_file_is_refused(self):
        (self.app / ".env").chmod(0o644)
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_writable_parent_is_refused(self):
        for mode in (0o775, 0o777):
            with self.subTest(mode=oct(mode)):
                self.opt.chmod(mode)
                try:
                    self.assertNotEqual(self.deploy().returncode, 0)
                    self.assertEqual(self.log.read_text(), "")
                finally:
                    self.opt.chmod(0o755)

    def test_symlink_secret_file_is_refused(self):
        path = self.app / ".env"
        path.rename(self.app / "real.env")
        path.symlink_to(self.app / "real.env")
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_symlink_lock_is_refused_without_touching_target(self):
        target = self.root / "outside.lock"
        target.write_text("keep")
        (self.app / "deploy.lock").symlink_to(target)
        self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(target.read_text(), "keep")
        self.assertEqual(self.log.read_text(), "")

    def test_concurrent_deploy_is_refused(self):
        import fcntl
        with (self.app / "deploy.lock").open("w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertNotEqual(self.deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), "")


if __name__ == "__main__":
    unittest.main()
