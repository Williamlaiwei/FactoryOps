"""Run existing regression suites on the deployed VM image using disposable databases.

Copy this script and the five suite modules to a private directory on the VM.
Never runs mutations in either deployed application database. No public test port.
"""
import argparse
import importlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request
import uuid


def run(command, *, input=None, check=True):
    result = subprocess.run(command, input=input, stdin=subprocess.DEVNULL if input is None else None,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if check and result.returncode:
        raise RuntimeError("VM test command failed: " + command[0])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, default=Path("/opt/factoryops/current"))
    args = parser.parse_args()
    release = args.release.resolve(strict=True)
    dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", str(release / ".env.cloud"),
          "-f", str(release / "compose.cloud.yaml"), "-f", str(release / "compose.vm.yaml")]
    deployed = run(dc + ["ps", "-q", "web"]).stdout.strip()
    info = json.loads(run(["docker", "inspect", deployed]).stdout)[0]
    image = info["Image"]  # Exact running image, even if its tag is later moved.
    networks = list(info["NetworkSettings"]["Networks"])
    assert len(networks) == 1
    settings = dict(value.split("=", 1) for value in info["Config"]["Env"])
    original_connection = settings["ConnectionStrings__FactoryOps"]
    assert "Database=factoryops;" in original_connection

    def sql(statement, database, service="db", user="factoryops"):
        return run(dc + ["exec", "-T", service, "psql", "-U", user, "-d", database,
                         "-v", "ON_ERROR_STOP=1", "-At"], input=statement).stdout.strip()

    def fingerprints():
        result = {}
        for service, database, user in (("db", "factoryops", "factoryops"), ("knowledge-db", "knowledge", "knowledge")):
            tables = sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;", database, service, user).splitlines()
            result[database] = {}
            for table in tables:
                quoted = '"' + table.replace('"', '""') + '"'
                query = f"SELECT md5(coalesce(string_agg(row_to_json(t)::text,'' ORDER BY row_to_json(t)::text),'')) FROM {quoted} t;"
                result[database][table] = sql(query, database, service, user)
        return result

    baseline = fingerprints()
    token = uuid.uuid4().hex
    suites = [("inventory", "InventoryTests"), ("order_editing", "OrderEditingTests"),
              ("product_catalog", "ProductCatalogTests"), ("company_profiles", "CompanyProfileTests"),
              ("settings", "SettingsTests")]
    total = failures = 0
    try:
        for suite_name, class_name in suites:
            database = "factoryops_remotecheck_" + token + "_" + str(suites.index((suite_name, class_name)))
            container = "factoryops-remotecheck-" + token
            assert re.fullmatch(r"factoryops_remotecheck_[a-f0-9]{32}_[0-4]", database)
            assert re.fullmatch(r"factoryops-remotecheck-[a-f0-9]{32}", container)
            created = started = False
            env_path = None
            try:
                sql(f'CREATE DATABASE "{database}";', "postgres")
                created = True
                descriptor, env_path = tempfile.mkstemp(prefix="factoryops-remotecheck-")
                values = {
                    "ASPNETCORE_ENVIRONMENT": "Production", "ASPNETCORE_HTTP_PORTS": "8080",
                    "BusinessTimeZone": "Australia/Sydney", "ReverseProxy__RequireHttps": "false",
                    "ConnectionStrings__FactoryOps": original_connection.replace("Database=factoryops;", "Database=" + database + ";"),
                    "KnowledgeService__BaseUrl": "http://ai:8000",
                    "KnowledgeService__ApiKey": settings.get("KnowledgeService__ApiKey", ""),
                    "OPENAI_API_KEY": "test-key-not-a-real-openai-credential",
                }
                with os.fdopen(descriptor, "w") as file:
                    for key, value in values.items():
                        assert "\n" not in value and "\r" not in value
                        file.write(key + "=" + value + "\n")
                os.chmod(env_path, 0o600)
                started = True
                run(["docker", "run", "-d", "--name", container, "--network", networks[0],
                     "--cpus", "0.5", "--memory", "384m", "-p", "127.0.0.1:18081:8080",
                     "--env-file", env_path, "--label", "factoryops.remotecheck=" + token, image])
                os.unlink(env_path)
                env_path = None
                deadline = time.monotonic() + 90
                while True:
                    try:
                        with urllib.request.urlopen("http://127.0.0.1:18081/healthz", timeout=3) as response:
                            if response.status == 200:
                                break
                    except (OSError, urllib.error.URLError):
                        if time.monotonic() >= deadline:
                            raise RuntimeError("Disposable VM test app did not become ready")
                        time.sleep(1)
                spec = importlib.util.spec_from_file_location("vm_order_helpers", Path(__file__).with_name("order_editing.py"))
                runner = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(runner)
                runner.ROOT = release
                runner.BASE = "http://127.0.0.1:18081"
                runner.DATABASE = database
                runner.sql = lambda statement, database=database: sql(statement, database)
                sys.modules["__main__"] = runner
                if suite_name == "order_editing":
                    case = runner.OrderEditingTests
                else:
                    sys.modules.pop(suite_name, None)
                    case = getattr(importlib.import_module(suite_name), class_name)
                print("RUN deployed-image suite: " + suite_name, flush=True)
                result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(case))
                total += result.testsRun
                failures += len(result.failures) + len(result.errors)
            finally:
                if env_path and os.path.exists(env_path):
                    os.unlink(env_path)
                if started:
                    owned = run(["docker", "inspect", "--format", '{{index .Config.Labels "factoryops.remotecheck"}}', container], check=False)
                    if owned.returncode == 0 and owned.stdout.strip() == token:
                        run(["docker", "rm", "-f", container])
                if created:
                    sql(f'DROP DATABASE "{database}" WITH (FORCE);', "postgres")
        print(f"VM regression total: {total} tests; {failures} failures/errors.", flush=True)
        return 1 if failures else 0
    finally:
        if fingerprints() != baseline:
            raise RuntimeError("Deployed data changed during the test window; investigate concurrent writes.")
        print("PASS: both deployed databases unchanged; disposable test resources removed.", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
