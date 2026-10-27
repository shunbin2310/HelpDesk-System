"""Run the unit's commands with fake IP/Docker/HTTP in a disposable Linux fixture."""
import os
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest


UNIT = Path(__file__).resolve().parents[3] / "scripts/deploy/helpdesk-web-boot.service"


@unittest.skipUnless(os.name == "posix", "Requires Bash, timeout and flock in Linux CI/container.")
class WebBootTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="helpdesk-web-boot-tests-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.app = self.root / "helpdesk-auto"
        self.app.mkdir()
        self.log = self.root / "commands.log"
        self.log.touch()
        self.env = dict(os.environ, HELPDESK_BIND_IP="192.168.1.125", FIXTURE=str(self.root))
        self.command("ip", '''#!/bin/bash
count=$(cat "$FIXTURE/ip-count" 2>/dev/null || echo 0)
count=$((count + 1))
echo "$count" > "$FIXTURE/ip-count"
if (( count >= ${IP_READY_AFTER:-1} )); then
    echo '2: fixture inet 192.168.1.125/24 scope global fixture'
fi
''')
        self.command("docker", '''#!/bin/bash
echo "$*" >> "$FIXTURE/commands.log"
[[ ${DOCKER_FAIL:-0} == 0 ]] || exit 12
touch "$FIXTURE/recovered"
''')
        self.command("curl", '''#!/bin/bash
echo "curl $*" >> "$FIXTURE/commands.log"
[[ ${HTTP_FAIL:-0} == 0 ]] || exit 7
[[ ${ALREADY_HEALTHY:-0} == 1 || -f "$FIXTURE/recovered" ]]
''')
        self.unit = UNIT.read_text().replace("\\\n", "")
        self.unit = self.unit.replace("/opt/helpdesk-auto", str(self.app))
        for command in ("docker", "curl"):
            self.unit = self.unit.replace("/usr/bin/" + command, str(self.root / command))
        self.unit = self.unit.replace("/usr/sbin/ip", str(self.root / "ip"))
        self.unit = self.unit.replace("/usr/bin/timeout 180", "/usr/bin/timeout 0.3")
        self.unit = self.unit.replace("/usr/bin/sleep 2", "/usr/bin/sleep 0.02")
        self.unit = self.unit.replace("/usr/bin/flock -w 180", "/usr/bin/flock -w 0.1")

    def command(self, name, body):
        path = self.root / name
        path.write_text(body)
        path.chmod(0o755)

    def run_directive(self, name):
        line = next(line for line in self.unit.splitlines() if line.startswith(name + "="))
        # systemd turns $$ into a literal dollar before invoking Bash.
        return subprocess.run(shlex.split(line.split("=", 1)[1].replace("$$", "$")),
                              env=self.env, text=True, capture_output=True, timeout=5)

    def test_waits_for_exact_address_then_restores_only_web(self):
        self.env["IP_READY_AFTER"] = "3"
        self.assertEqual(self.run_directive("ExecStartPre").returncode, 0)
        self.assertEqual((self.root / "ip-count").read_text().strip(), "3")
        result = self.run_directive("ExecStart")
        self.assertEqual(result.returncode, 0, result.stderr)
        log = self.log.read_text()
        self.assertIn("--project-name helpdesk-auto", log)
        self.assertIn("--env-file " + str(self.app / "release.env"), log)
        self.assertIn("up -d --no-deps --force-recreate --no-build --pull never --wait --wait-timeout 90 web", log)
        self.assertIn("http://192.168.1.125:8085/health", log)
        self.assertNotIn("project1", log)
        self.assertFalse((self.app / "release.env").exists())

    def test_missing_or_prefix_only_address_times_out_without_docker(self):
        self.command("ip", "#!/bin/bash\necho '2: fixture inet 192.168.1.1250/24 scope global fixture'\n")
        self.assertEqual(self.run_directive("ExecStartPre").returncode, 124)
        self.assertEqual(self.log.read_text(), "")

    def test_healthy_site_is_left_running(self):
        self.env["ALREADY_HEALTHY"] = "1"
        self.assertEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertNotIn("compose", self.log.read_text())

    def test_failure_marker_is_rechecked_after_waiting(self):
        (self.app / "needs-attention").touch()
        self.assertNotEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_active_deployment_lock_prevents_recovery(self):
        with (self.app / "deploy.lock").open("w") as lock:
            import fcntl
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertNotEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_dangling_failure_marker_also_blocks_recovery(self):
        (self.app / "needs-attention").symlink_to(self.root / "missing")
        self.assertNotEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertEqual(self.log.read_text(), "")

    def test_recreate_failure_is_reported_without_touching_api(self):
        self.env["DOCKER_FAIL"] = "1"
        self.assertNotEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertEqual(self.log.read_text().count("curl "), 1)
        self.assertNotIn(" stop ", self.log.read_text())

    def test_missing_host_port_after_recreate_is_reported(self):
        self.env["HTTP_FAIL"] = "1"
        self.assertNotEqual(self.run_directive("ExecStart").returncode, 0)
        self.assertEqual(self.log.read_text().count("curl "), 2)

    def test_unit_runs_once_at_boot_and_respects_failure_marker(self):
        unit = UNIT.read_text()
        self.assertIn("After=network-online.target docker.service", unit)
        self.assertIn("ConditionPathExists=!/opt/helpdesk-auto/needs-attention", unit)
        self.assertIn("Type=oneshot", unit)
        self.assertIn("WantedBy=multi-user.target", unit)
        self.assertNotIn("Restart=", unit)
        self.assertNotIn("ExecStop=", unit)


if __name__ == "__main__":
    unittest.main()
