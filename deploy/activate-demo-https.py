"""First HTTPS activation on the existing demo VM, with configuration rollback."""
import base64
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import urllib.error
import urllib.request
import uuid


def main():
    root = Path("/opt/factoryops/current").resolve(strict=True)
    stage = Path(__file__).resolve().parent
    credentials = Path("/opt/factoryops/demo-access.txt")
    if credentials.exists():
        raise RuntimeError("Demo credentials already exist; inspect current state before changing them.")
    backup = Path("/opt/factoryops/config-backups") / ("20261020-https-" + uuid.uuid4().hex[:8])
    backup.mkdir(parents=True, mode=0o700)
    for filename in (".env.cloud", "compose.cloud.yaml"):
        shutil.copyfile(root / filename, backup / filename)
        (backup / filename).chmod(0o600)
    dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", ".env.cloud",
          "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml"]

    def run(command, *, check=True):
        result = subprocess.run(command, cwd=root, stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if check and result.returncode:
            raise RuntimeError("Demo configuration command failed; secret output suppressed.")
        return result

    changed = False
    try:
        shutil.copyfile(stage / "compose.cloud.yaml", root / "compose.cloud.yaml")
        changed = True
        shutil.copyfile(stage / "Caddyfile.protected", root / "deploy/Caddyfile.protected")
        helper = Path("/opt/factoryops/tools/configure_demo_access.py")
        shutil.copyfile(stage / "configure_demo_access.py", helper)
        helper.chmod(0o600)
        run(["python3", str(helper), "--site", "45.79.238.119", "--generate"])
        print("Prepared private credentials and configuration backup. Values hidden.", flush=True)
        run(dc + ["config", "--quiet"])
        run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "web", "proxy"])
        print("Services started. Waiting for a publicly trusted IP certificate...", flush=True)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        deadline = time.monotonic() + 150
        while True:
            try:
                opener.open("https://45.79.238.119/healthz", timeout=5)
                raise RuntimeError("HTTPS unexpectedly allows anonymous access.")
            except urllib.error.HTTPError as error:
                if error.code != 401:
                    raise
                error.close()
                break
            except (OSError, urllib.error.URLError):
                if time.monotonic() >= deadline:
                    raise RuntimeError("Trusted HTTPS certificate did not become ready in time.")
                time.sleep(2)
        print("PASS: system trust validates the IP certificate; anonymous access is blocked.", flush=True)
        values = dict(line.split(": ", 1) for line in credentials.read_text().splitlines())
        authorization = "Basic " + base64.b64encode((values["Username"] + ":" + values["Password"]).encode()).decode()
        for route in ("/healthz", "/Dashboard", "/WorkOrders", "/Inventory", "/Products", "/Settings", "/Knowledge"):
            request = urllib.request.Request("https://45.79.238.119" + route, headers={"Authorization": authorization})
            with opener.open(request, timeout=30) as response:
                assert response.status == 200
                response.read()
            print("PASS authenticated HTTPS " + route, flush=True)

        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, req, fp, code, msg, headers, newurl):
                return None
        plain = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        try:
            plain.open("http://45.79.238.119/Dashboard", timeout=5)
        except urllib.error.HTTPError as response:
            assert response.code == 308
            assert response.headers["Location"] == "https://45.79.238.119/Dashboard"
            assert not response.headers.get("WWW-Authenticate")
            response.close()
        else:
            raise RuntimeError("HTTP did not redirect to HTTPS.")
        print("PASS: HTTP redirects to HTTPS before authentication.", flush=True)
        run(["systemctl", "is-active", "--quiet", "scribd-catalog.service"])
        state = {"status": "active", "url": "https://45.79.238.119", "username": values["Username"],
                 "credentialsFile": str(credentials), "configurationBackup": str(backup)}
        Path("/opt/factoryops/https-state.json").write_text(json.dumps(state, indent=2) + "\n")
        print("HTTPS and demo password activated; crawler still active. Password remains only on VM.", flush=True)
    except Exception:
        if changed:
            for filename in (".env.cloud", "compose.cloud.yaml"):
                shutil.copyfile(backup / filename, root / filename)
            (root / ".env.cloud").chmod(0o600)
            rollback = run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "web", "proxy"], check=False)
            print("Restored previous entry configuration. Service restart succeeded:", rollback.returncode == 0, flush=True)
        raise


if __name__ == "__main__":
    main()
