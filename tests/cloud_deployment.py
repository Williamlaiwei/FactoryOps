"""Restore a prepared backup into an isolated cloud stack, then check HTTPS/keys.

No host ports are published. Only this script's UUID-named containers/volumes are
removed. The running local demo is read for comparisons and is never modified.
"""
import argparse
import json
from pathlib import Path
import re
import secrets
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]


def run(command, *, capture=False, input=None, check=True):
    result = subprocess.run(command, cwd=ROOT, input=input, stdout=subprocess.PIPE if capture else None,
                            stderr=subprocess.PIPE if capture else None)
    if check and result.returncode: raise RuntimeError("Cloud check command failed; inspect the isolated stack logs.")
    return result


def fingerprint(compose, service, database, user):
    prefix = compose + ["exec", "-T", service, "psql", "-U", user, "-d", database, "-v", "ON_ERROR_STOP=1", "-t", "-A"]
    names = run(prefix, capture=True, input=b"SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;").stdout.decode().splitlines()
    result = {}
    for name in names:
        quoted = '"' + name.replace('"', '""') + '"'
        sql = f"SELECT md5(coalesce(string_agg(row_to_json(t)::text,'' ORDER BY row_to_json(t)::text),'')) FROM {quoted} t;"
        result[name] = run(prefix, capture=True, input=sql.encode()).stdout.decode().strip()
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("backup", type=Path)
    args = parser.parse_args()
    project = "factoryops-cloud-check-" + uuid.uuid4().hex[:12]
    assert re.fullmatch(r"factoryops-cloud-check-[a-f0-9]{12}", project)
    directory = ROOT / "artifacts" / project
    directory.mkdir(parents=True)
    env_file = directory / "check.env"
    env_file.write_text("SITE_ADDRESS=localhost\nRELEASE_TAG=cloud-check\nPOSTGRES_PASSWORD=" + secrets.token_hex(32) +
                        "\nKNOWLEDGE_POSTGRES_PASSWORD=" + secrets.token_hex(32) + "\nFACTORYOPS_AI_KEY=" + secrets.token_hex(32) + "\n", encoding="utf-8")
    configure = ["docker", "compose", "--env-file", str(env_file), "-p", project, "-f", "compose.cloud.yaml"]
    local = ["docker", "compose"]
    databases = (("db", "factoryops", "factoryops"), ("knowledge-db", "knowledge", "knowledge"))
    before = [fingerprint(local, *item) for item in databases]
    run(configure + ["config", "--quiet"])
    run(configure + ["build", "web"])
    # The knowledge code is unchanged; reuse the already-built local image.
    config = json.loads(run(configure + ["config", "--format", "json"], capture=True).stdout)
    config["services"]["proxy"]["ports"] = []
    # Test-only names use Caddy's local CA; both localhost and the Docker DNS name
    # must have a certificate, because TLS SNI is separate from the Host header.
    test_caddy = directory / "Caddyfile.check"
    test_caddy.write_text((ROOT / "deploy/Caddyfile").read_text().replace("{$SITE_ADDRESS} {", "localhost, proxy {\n    tls internal", 1), encoding="utf-8")
    for mount in config["services"]["proxy"]["volumes"]:
        if mount["target"] == "/etc/caddy/Caddyfile": mount["source"] = str(test_caddy)
    config["services"]["ai"]["image"] = "factoryops-ai:latest"
    for service in config["services"].values(): service.pop("build", None)
    for volume in config["volumes"].values(): assert volume["name"].startswith(project + "_")
    runtime = directory / "compose.check.json"
    runtime.write_text(json.dumps(config), encoding="utf-8")
    target = ["docker", "compose", "-p", project, "-f", str(runtime)]
    started = False
    try:
        started = True
        run(target + ["up", "-d", "--wait", "--wait-timeout", "120", "db", "knowledge-db"])
        data_cli = [sys.executable, "tools/cloud_data.py", "restore", str(args.backup.resolve()), "--compose", str(runtime), "--project", project]
        run(data_cli)
        restored = [fingerprint(target, *item) for item in databases]
        assert restored == before, "Restored table contents differ from the source; check concurrent source writes."
        print("PASS: all business and knowledge tables match after full SQL restore.", flush=True)
        refused = run(data_cli, capture=True, check=False)
        assert refused.returncode != 0 and b"not empty" in refused.stderr
        assert [fingerprint(target, *item) for item in databases] == restored
        print("PASS: repeat restore refused without overwriting existing data.", flush=True)
        run(target + ["up", "-d", "--wait", "--wait-timeout", "180"])
        # No host certificate exceptions or host network ports: localhost is inside
        # this isolated Caddy container and uses its temporary development CA.
        for path in ("/healthz", "/Dashboard", "/WorkOrders", "/Inventory", "/Knowledge"):
            result = run(target + ["exec", "-T", "proxy", "wget", "--no-check-certificate", "-qO-", "https://localhost" + path], capture=True)
            assert result.stdout, path
        run(target + ["exec", "-T", "web", "dotnet", "FactoryOps.Web.dll", "--healthcheck"])
        print("PASS: Production HTTPS proxy, forwarded scheme, readiness and knowledge page.", flush=True)
        assert [fingerprint(target, *item) for item in databases] == restored
        print("PASS: application startup preserves restored SQL contents.", flush=True)
        sql = b'''INSERT INTO "InventoryStocks" ("ProductId","CompanyKey","OnHand","ReorderPoint","LeadTimeDays","UpdatedAt")
            SELECT "Id","CompanyKey",10,0,0,now() FROM "Products" ORDER BY "Id" LIMIT 1 ON CONFLICT ("ProductId") DO NOTHING;'''
        run(target + ["exec", "-T", "db", "psql", "-U", "factoryops", "-d", "factoryops", "-v", "ON_ERROR_STOP=1"], capture=True, input=sql)
        export = b'''import httpx
from pathlib import Path
with httpx.Client(base_url="https://proxy",headers={"Host":"localhost"},verify=False,timeout=20) as c:
    r=c.get("/Inventory/ExportStockCsv"); r.raise_for_status()
    assert b"StockSnapshot" in r.content and len(r.content.splitlines())>1
    Path("/tmp/cloud-stock.csv").write_bytes(r.content)
'''
        run(target + ["exec", "-T", "ai", "python", "-"], input=export)
        run(target + ["up", "-d", "--force-recreate", "--no-deps", "--wait", "--wait-timeout", "90", "web"])
        verify = b'''import httpx, re, html, os
from pathlib import Path
with httpx.Client(base_url="https://proxy",headers={"Host":"localhost"},verify=False,timeout=30) as c:
    r=c.get("/Inventory/ImportCsv"); r.raise_for_status()
    fields={k:html.unescape(v) for k,v in re.findall(r'<input\\b[^>]*name="([^"]+)"[^>]*value="([^"]*)"',r.text)}
    r=c.post("/Inventory/ImportCsv",data={k:fields[k] for k in ("__RequestVerificationToken","__CompanyKey")},files={"File":("stock.csv",Path("/tmp/cloud-stock.csv").read_bytes(),"text/csv")})
    r.raise_for_status(); assert "Review Stock Import" in r.text and 'name="token"' in r.text
with httpx.Client(timeout=15) as c:
    r=c.get("http://web:8080/internal/catalog",headers={"X-FactoryOps-Key":os.environ["FACTORYOPS_AI_KEY"],"X-FactoryOps-Company":"fti-demo"})
    r.raise_for_status(); assert r.json()["companyKey"]=="fti-demo"
'''
        run(target + ["exec", "-T", "ai", "python", "-"], input=verify)
        print("PASS: stock snapshot survives web recreation; private catalog stays on HTTP.", flush=True)
        assert [fingerprint(local, *item) for item in databases] == before
        print("PASS: source databases unchanged. No cloud accounts or servers created.", flush=True)
    finally:
        if started:
            assert re.fullmatch(r"factoryops-cloud-check-[a-f0-9]{12}", project)
            run(target + ["down", "--volumes", "--remove-orphans"], check=False)


if __name__ == "__main__": main()
