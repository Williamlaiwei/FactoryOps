"""Install the tested supplier/risk release without restoring or replacing SQL data."""
import base64
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import urllib.request
import uuid
import zipfile


def main():
    root = Path("/opt/factoryops/current").resolve(strict=True)
    incoming = Path("/opt/factoryops/incoming")
    image = incoming / "factoryops-web-suppliers-20261020.tar.gz"
    source = incoming / "factoryops-suppliers-20261020-source.zip"
    checksums = {image: "bd1e32cc3dedf26ed905b2244447102840d493ad989331b5375f23f8b352f07b",
                 source: "99d77e89a80e07cc82247298bac6bc39b61c38c47ebc1ca4299f9571775313ab"}
    for path, expected in checksums.items():
        with path.open("rb") as file:
            assert hashlib.file_digest(file, "sha256").hexdigest() == expected, "Transfer checksum mismatch"
    dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", ".env.cloud", "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml"]

    def run(command, *, input=None, check=True):
        result = subprocess.run(command, cwd=root, input=input, stdin=subprocess.DEVNULL if input is None else None,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if check and result.returncode:
            raise RuntimeError("Release command failed; secret output suppressed.")
        return result

    def sql(query, service="db", database="factoryops", user="factoryops"):
        return run(dc + ["exec", "-T", service, "psql", "-U", user, "-d", database, "-At", "-v", "ON_ERROR_STOP=1"], input=query).stdout.strip()

    tables = {}
    for service, database, user in (("db", "factoryops", "factoryops"), ("knowledge-db", "knowledge", "knowledge")):
        tables[(service, database, user)] = sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;", service, database, user).splitlines()

    def fingerprint():
        result = {}
        for (service, database, user), names in tables.items():
            for name in names:
                quoted = '"' + name.replace('"', '""') + '"'
                value = "to_jsonb(t) - 'SupplierId'" if name == "Replenishments" else "to_jsonb(t)"
                where = " WHERE t.\"Name\" <> '009-product-suppliers'" if name == "SchemaUpdates" else ""
                query = f"SELECT md5(coalesce(string_agg(value::text,'' ORDER BY value::text),'')) FROM (SELECT {value} AS value FROM {quoted} t{where}) q;"
                result[database + "." + name] = sql(query, service, database, user)
        return result

    backup = Path("/opt/factoryops/backups") / ("suppliers-20261020-" + uuid.uuid4().hex[:8])
    backup.mkdir(parents=True, mode=0o700)
    shutil.copyfile(root / ".env.cloud", backup / ".env.cloud")
    (backup / ".env.cloud").chmod(0o600)
    run(["python3", "tools/cloud_data.py", "backup", str(backup / "databases"), "--compose", "compose.cloud.yaml",
         "--env-file", ".env.cloud", "--project", "factoryops-demo"])
    before = fingerprint()
    original_env = (root / ".env.cloud").read_text()
    original_files = {}
    switched = False
    try:
        run(["docker", "image", "load", "--input", str(image)])
        with zipfile.ZipFile(source) as archive:
            manifest = json.loads(archive.read("release-manifest.json"))
            for name, checksum in manifest["files"].items():
                if not name.startswith("FactoryOps.Web/"):
                    continue
                path = PurePosixPath(name)
                assert not path.is_absolute() and ".." not in path.parts
                target = (root / name).resolve()
                assert target.is_relative_to(root / "FactoryOps.Web")
                content = archive.read(name)
                assert hashlib.sha256(content).hexdigest() == checksum
                original_files[target] = target.read_bytes() if target.exists() else None
                if target.exists():
                    saved = backup / name
                    saved.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(target, saved)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
        ai = run(dc + ["ps", "-q", "ai"]).stdout.strip()
        ai_image = run(["docker", "inspect", "--format", "{{.Image}}", ai]).stdout.strip()
        run(["docker", "image", "tag", ai_image, "factoryops-ai:suppliers-20261020"])
        # Keep a matching alias for the unchanged AI image so future full up works.
        updated = re.sub(r"^RELEASE_TAG=.*$", "RELEASE_TAG=suppliers-20261020", original_env, flags=re.MULTILINE)
        assert updated != original_env
        with tempfile.NamedTemporaryFile(mode="w", dir=root, prefix=".release-setting-", delete=False) as file:
            file.write(updated)
            temporary = Path(file.name)
        temporary.chmod(0o600)
        temporary.replace(root / ".env.cloud")
        switched = True
        print("Verified uploads, backed up SQL/configuration and installed application sources. Starting tested image...", flush=True)
        run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "web"])
        credentials = dict(line.split(": ", 1) for line in Path("/opt/factoryops/demo-access.txt").read_text().splitlines())
        authorization = "Basic " + base64.b64encode((credentials["Username"] + ":" + credentials["Password"]).encode()).decode()
        for route in ("/healthz", "/Dashboard", "/Suppliers", "/Inventory/Schedule", "/Inventory/Replenishments",
                      "/js/stock-product-picker.js", "/js/replenishment-suppliers.js"):
            request = urllib.request.Request("https://45.79.238.119" + route, headers={"Authorization": authorization})
            with urllib.request.urlopen(request, timeout=30) as response:
                body = response.read()
                assert response.status == 200
                if route == "/Dashboard":
                    assert b'stockProductButton' in body and b'data-risk=' in body
            print("PASS deployed HTTPS " + route, flush=True)
        after = fingerprint()
        if before != after:
            raise RuntimeError("Existing records changed during deployment; inspect concurrent writes. No SQL restore was performed.")
        supplier_count = int(sql('SELECT count(*) FROM "Suppliers";'))
        link_count = int(sql('SELECT count(*) FROM "ProductSuppliers";'))
        run(["systemctl", "is-active", "--quiet", "scribd-catalog.service"])
        state = {"releaseTag": "suppliers-20261020", "backup": str(backup),
                 "existingDataPreserved": True, "schema": "009-product-suppliers", "authenticationPreserved": True}
        Path("/opt/factoryops/suppliers-update.json").write_text(json.dumps(state, indent=2) + "\n")
        print(f"PASS: existing SQL records preserved; supplier tables ready ({supplier_count} suppliers, {link_count} links); login unchanged; crawler active.", flush=True)
    except Exception:
        (root / ".env.cloud").write_text(original_env)
        (root / ".env.cloud").chmod(0o600)
        for path, content in original_files.items():
            if content is None:
                path.unlink(missing_ok=True)
            else:
                path.write_bytes(content)
        if switched:
            rollback = run(dc + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "120", "web"], check=False)
            print("Previous application restored:", rollback.returncode == 0, flush=True)
        raise


if __name__ == "__main__":
    main()
