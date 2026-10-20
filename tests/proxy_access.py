"""VM-only HTTPS/auth check on disposable loopback listeners; no business writes."""
import base64
import json
from pathlib import Path
import re
import secrets
import ssl
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid


def run(command, **kwargs):
    if "input" not in kwargs:
        kwargs["stdin"] = subprocess.DEVNULL
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, **kwargs)
    if result.returncode:
        raise RuntimeError("Proxy test command failed: " + command[0])
    return result.stdout


def main():
    source = Path(__file__).with_name("Caddyfile.protected")
    name = "factoryops-access-check-" + uuid.uuid4().hex
    password = secrets.token_urlsafe(24)
    dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", ".env.cloud",
          "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml"]
    hashed = run(dc + ["exec", "-T", "proxy", "caddy", "hash-password"], input=password + "\n").strip()
    assert re.fullmatch(r"\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}", hashed)
    with tempfile.TemporaryDirectory(prefix="factoryops-access-check-") as temporary:
        folder = Path(temporary)
        env = folder / "test.env"
        env.write_text("SITE_ADDRESS=https://45.79.238.119\nDEMO_TLS_NAME=45.79.238.119\nDEMO_AUTH_USER=demo\nDEMO_AUTH_HASH=" + hashed + "\n")
        env.chmod(0o600)
        adapted = json.loads(run(["docker", "run", "--rm", "--network", "none", "--env-file", str(env),
                                 "-v", str(source) + ":/etc/caddy/Caddyfile:ro", "caddy:2-alpine",
                                 "caddy", "adapt", "--config", "/etc/caddy/Caddyfile", "--validate"]))
        policies = adapted["apps"]["tls"]["automation"]["policies"]
        assert any(issuer.get("profile") == "shortlived" and issuer.get("module") == "acme"
                   for policy in policies for issuer in policy["issuers"])
        print("PASS: public IP config validates with explicit short-lived ACME issuer.", flush=True)
        # Test trust uses a private throwaway CA; this never becomes the live site CA.
        local = folder / "Caddyfile"
        text = source.read_text()
        start = text.index("    tls {")
        end = text.index("    basic_auth {")
        local_text = text[:start] + "    tls internal\n" + text[end:]
        # Exercise the authentication boundary independently of the live app's
        # trusted-proxy list and HTTPS redirects. vm_smoke checks the real app.
        local_text = local_text.replace('    reverse_proxy web:8080 {\n        header_up -Authorization\n    }',
                                        '    respond "Authorized proxy test" 200')
        local.write_text(local_text)
        env.write_text("SITE_ADDRESS=https://localhost\nDEMO_TLS_NAME=localhost\nDEMO_AUTH_USER=demo\nDEMO_AUTH_HASH=" + hashed + "\n")
        started = False
        try:
            started = True
            run(["docker", "run", "-d", "--name", name, "--network", "factoryops-demo_default",
                 "--cpus", "0.25", "--memory", "128m", "--env-file", str(env),
                 "-p", "127.0.0.1:18443:443", "-p", "127.0.0.1:18082:80",
                 "-v", str(local) + ":/etc/caddy/Caddyfile:ro", "caddy:2-alpine"])
            for attempt in range(30):
                try:
                    root = run(["docker", "exec", name, "cat", "/data/caddy/pki/authorities/local/root.crt"])
                    break
                except RuntimeError:
                    if attempt == 29:
                        raise
                    time.sleep(1)
            context = ssl.create_default_context(cadata=root)
            # The root can exist before Caddy has finished issuing the leaf cert.
            for attempt in range(30):
                try:
                    urllib.request.urlopen("https://localhost:18443/healthz", context=context, timeout=3)
                except urllib.error.HTTPError as error:
                    assert error.code == 401
                    error.close()
                    break
                except (OSError, urllib.error.URLError):
                    if attempt == 29:
                        raise RuntimeError("Disposable TLS proxy did not become ready")
                    time.sleep(1)
            for path in ("/Dashboard", "/Products", "/healthz", "/Settings"):
                for credential in (None, "demo:incorrect-test-password", "demo:" + password):
                    request = urllib.request.Request("https://localhost:18443" + path)
                    if credential is not None:
                        request.add_header("Authorization", "Basic " + base64.b64encode(credential.encode()).decode())
                    try:
                        response = urllib.request.urlopen(request, context=context, timeout=30)
                    except urllib.error.HTTPError as error:
                        response = error
                    with response:
                        assert response.status == (200 if credential == "demo:" + password else 401)
                        if response.status == 401:
                            assert response.headers["WWW-Authenticate"].startswith("Basic ")
                        response.read()
                print("PASS: " + path + " rejects missing/wrong credentials and accepts correct credentials over trusted test TLS.", flush=True)

            class NoRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, req, fp, code, msg, headers, newurl):
                    return None
            opener = urllib.request.build_opener(NoRedirect())
            try:
                response = opener.open(urllib.request.Request("http://127.0.0.1:18082/Dashboard", headers={"Host": "localhost"}), timeout=10)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                assert response.status == 308 and response.headers["Location"].startswith("https://")
                assert not response.headers.get("WWW-Authenticate")
            print("PASS: plain HTTP redirects to HTTPS before asking for credentials.", flush=True)
        finally:
            if started:
                assert re.fullmatch(r"factoryops-access-check-[a-f0-9]{32}", name)
                run(["docker", "rm", "-f", "-v", name])
    print("PASS: disposable proxy removed; live proxy and application data unchanged.", flush=True)


if __name__ == "__main__":
    main()
