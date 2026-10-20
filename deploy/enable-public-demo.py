"""Switch the existing demo to public HTTPS, preserving app/data and rollback settings."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.error
import urllib.request
import uuid


def main():
    root = Path("/opt/factoryops/current").resolve(strict=True)
    source = Path(__file__).with_name("Caddyfile.public")
    target = root / "deploy/Caddyfile.public"
    env = root / ".env.cloud"
    original = env.read_bytes()
    text = original.decode("utf-8-sig")
    assert re.search(r"^SITE_ADDRESS=https://45\.79\.238\.119\s*$", text, re.MULTILINE)
    assert re.search(r"^REQUIRE_HTTPS=true\s*$", text, re.MULTILINE)
    updated, count = re.subn(r"^CADDY_CONFIG_FILE=.*$", "CADDY_CONFIG_FILE=./deploy/Caddyfile.public", text, flags=re.MULTILINE)
    assert count == 1
    backup = Path("/opt/factoryops/config-backups") / ("public-demo-" + uuid.uuid4().hex[:8])
    backup.mkdir(parents=True, mode=0o700)
    shutil.copyfile(env, backup / ".env.cloud")
    (backup / ".env.cloud").chmod(0o600)
    prior_config = target.read_bytes() if target.exists() else None
    dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", ".env.cloud",
          "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml"]

    def run(command, *, check=True):
        result = subprocess.run(command, cwd=root, stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if check and result.returncode:
            raise RuntimeError("Demo proxy command failed; secret output suppressed.")
        return result

    def write_env(content):
        descriptor, temporary = tempfile.mkstemp(prefix=".public-demo-", dir=root)
        try:
            with os.fdopen(descriptor, "wb") as output:
                output.write(content)
            os.chmod(temporary, 0o600)
            os.replace(temporary, env)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)

    switched = False
    try:
        shutil.copyfile(source, target)
        run(["docker", "run", "--rm", "--network", "none",
             "-e", "SITE_ADDRESS=https://45.79.238.119", "-e", "DEMO_TLS_NAME=45.79.238.119",
             "-v", str(target) + ":/etc/caddy/Caddyfile:ro", "caddy:2-alpine",
             "caddy", "adapt", "--config", "/etc/caddy/Caddyfile", "--validate"])
        write_env(updated.encode())
        run(dc + ["config", "--quiet"])
        switched = True
        print("Proxy configuration validated; activating public HTTPS access...", flush=True)
        run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "proxy"])
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        for route in ("/healthz", "/Dashboard", "/WorkOrders", "/Inventory", "/Products", "/Settings", "/Knowledge"):
            with opener.open("https://45.79.238.119" + route, timeout=30) as response:
                assert response.status == 200
                assert not response.headers.get("WWW-Authenticate")
                response.read()
            print("PASS anonymous trusted HTTPS " + route, flush=True)

        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, req, fp, code, msg, headers, newurl):
                return None

        plain = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        try:
            plain.open("http://45.79.238.119/Dashboard", timeout=10)
        except urllib.error.HTTPError as response:
            with response:
                assert response.code == 308
                assert response.headers["Location"] == "https://45.79.238.119/Dashboard"
                assert not response.headers.get("WWW-Authenticate")
        else:
            raise RuntimeError("HTTP must redirect to HTTPS.")
        run(["systemctl", "is-active", "--quiet", "scribd-catalog.service"])
        state = {"status": "active", "access": "public", "url": "https://45.79.238.119",
                 "configurationBackup": str(backup), "configuration": "deploy/Caddyfile.public"}
        Path("/opt/factoryops/https-state.json").write_text(json.dumps(state, indent=2) + "\n")
        print("PASS public HTTPS; HTTP redirect preserved; only proxy recreated; crawler active.", flush=True)
    except Exception:
        write_env(original)
        if prior_config is None:
            target.unlink(missing_ok=True)
        else:
            target.write_bytes(prior_config)
        if switched:
            result = run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "proxy"], check=False)
            print("Previous proxy restored:", result.returncode == 0, flush=True)
        raise


if __name__ == "__main__":
    main()
