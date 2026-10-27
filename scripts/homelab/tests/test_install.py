"""Install into a temporary filesystem with fake service tools, never the host."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SOURCE = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == "posix" and hasattr(os, "geteuid") and os.geteuid() == 0,
                     "Run as root only in disposable Linux CI/container.")
class InstallTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="helpdesk-demo-install-tests-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.files = self.root / "source"
        self.files.mkdir()
        self.app = self.root / "opt/helpdesk-demo"
        self.config = self.root / "etc/helpdesk-demo"
        self.unit = self.root / "etc/systemd/system/helpdesk-demo.service"
        self.unit.parent.mkdir(parents=True)
        self.log = self.root / "commands.log"
        self.log.touch()
        for name in ("quick_tunnel.py", "helpdesk-demo.service", "demo.env.example"):
            (self.files / name).write_text((SOURCE / name).read_text())
        self.fake("hostname", 'echo "${TEST_HOSTNAME:-homelab-server}"')
        self.fake("systemctl", 'echo "$*" >> "$TEST_LOG"')
        self.fake("systemd-analyze", 'echo "verify $*" >> "$TEST_LOG"')
        self.fake("cloudflared", "exit 0")
        body = (SOURCE / "install.sh").read_text()
        for old, new in (
            ("/opt/helpdesk-demo", str(self.app)),
            ("/etc/helpdesk-demo", str(self.config)),
            ("/etc/systemd/system/helpdesk-demo.service", str(self.unit)),
            ("/usr/bin/python3", sys.executable),
            ("/usr/bin/cloudflared", str(self.bin / "cloudflared")),
            ("/usr/bin/systemctl", str(self.bin / "systemctl")),
            ("/usr/bin/systemd-analyze", str(self.bin / "systemd-analyze")),
        ):
            self.assertIn(old, body)
            body = body.replace(old, new)
        self.script = self.files / "install.sh"
        self.script.write_text(body)
        self.env = dict(os.environ, PATH=str(self.bin) + ":" + os.environ["PATH"], TEST_LOG=str(self.log))

    def fake(self, name, body):
        path = self.bin / name
        path.write_text("#!/bin/bash\n" + body + "\n")
        path.chmod(0o755)

    def install(self):
        return subprocess.run(["/bin/bash", str(self.script)], env=self.env,
                              capture_output=True, text=True, timeout=10)

    def test_install_permissions_and_no_service_start_or_token_creation(self):
        result = self.install()
        self.assertEqual(result.returncode, 0, result.stderr)
        for path, mode in ((self.app, 0o755), (self.config, 0o700),
                           (self.config / "config", 0o600), (self.unit, 0o644)):
            self.assertEqual(path.stat().st_mode & 0o777, mode)
            self.assertEqual(path.stat().st_uid, 0)
        self.assertEqual((self.app / "quick_tunnel.py").read_bytes(), (self.files / "quick_tunnel.py").read_bytes())
        self.assertFalse((self.config / "github-token").exists())
        log = self.log.read_text()
        self.assertIn("is-enabled --quiet helpdesk-web-boot.service", log)
        self.assertIn("daemon-reload", log)
        self.assertNotIn("enable --now", log)
        self.assertNotIn("restart", log)
        self.assertNotIn("project1", log)

    def test_existing_private_configuration_is_not_overwritten(self):
        self.config.mkdir(mode=0o700)
        token = self.config / "github-token"
        token.write_text("private-test-fixture")
        self.assertNotEqual(self.install().returncode, 0)
        self.assertEqual(token.read_text(), "private-test-fixture")
        self.assertFalse(self.app.exists())
        self.assertEqual(self.log.read_text(), "")

    def test_symlinked_target_is_refused(self):
        self.config.symlink_to(self.root / "missing")
        self.assertNotEqual(self.install().returncode, 0)
        self.assertFalse(self.app.exists())
        self.assertEqual(self.log.read_text(), "")

    def test_wrong_host_and_missing_binary_cannot_install(self):
        self.env["TEST_HOSTNAME"] = "another-server"
        self.assertNotEqual(self.install().returncode, 0)
        self.env.pop("TEST_HOSTNAME")
        (self.bin / "cloudflared").unlink()
        self.assertNotEqual(self.install().returncode, 0)
        self.assertFalse(self.app.exists())
        self.assertFalse(self.config.exists())


if __name__ == "__main__":
    unittest.main()
