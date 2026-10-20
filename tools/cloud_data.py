"""Binary-safe PostgreSQL 16 backup/restore for both FactoryOps databases.

Restore refuses nonempty targets and never drops databases, tables or volumes.
Run from the repository/release root. Python 3.10+ and Docker Compose are required.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import subprocess

DATABASES = (("db", "factoryops", "factoryops"), ("knowledge-db", "knowledge", "knowledge"))


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest() if hasattr(hashlib, "file_digest") else hashlib.sha256(source.read()).hexdigest()


def compose(args):
    result = ["docker", "compose"]
    if args.env_file: result += ["--env-file", str(args.env_file)]
    if args.project: result += ["--project-name", args.project]
    return result + ["-f", str(args.compose)]


def execute(command, **kwargs):
    # Compose exec otherwise inherits and can consume a provisioning script's stdin.
    if "stdin" not in kwargs and "input" not in kwargs:
        kwargs["stdin"] = subprocess.DEVNULL
    result = subprocess.run(command, stderr=subprocess.PIPE, **kwargs)
    if result.returncode:
        # Do not echo expanded configuration, passwords, SQL data or document contents.
        raise RuntimeError("Docker database command failed. Check service health and PostgreSQL logs.")
    return result


def validate_backup(directory):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("format") != "factoryops-pg16-v1": raise ValueError("Unsupported backup format.")
    for _, database, _ in DATABASES:
        path = directory / (database + ".dump")
        expected = manifest["databases"][database]
        if path.is_symlink() or path.stat().st_size != expected["bytes"] or digest(path) != expected["sha256"]:
            raise ValueError("Backup checksum verification failed: " + database)
    return manifest


def backup(args):
    directory = args.directory.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    manifest = {"format": "factoryops-pg16-v1", "createdUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "databases": {}}
    for service, database, user in DATABASES:
        target = directory / (database + ".dump")
        with target.open("xb") as destination:
            execute(compose(args) + ["exec", "-T", service, "pg_dump", "-U", user, "-d", database,
                    "--format=custom", "--no-owner", "--no-acl"], stdout=destination)
        manifest["databases"][database] = {"bytes": target.stat().st_size, "sha256": digest(target)}
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("Backed up business records and knowledge originals/chunks/vectors to", directory)
    print("Separate consistent database snapshots; pause web/ai writes for the final cutover backup.")


def restore(args):
    directory = args.directory.resolve()
    validate_backup(directory)
    # Validate BOTH empty databases and archives before restoring either one.
    for service, database, user in DATABASES:
        query = "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND c.relkind IN ('r','p','v','m','S','f');"
        result = execute(compose(args) + ["exec", "-T", service, "psql", "-U", user, "-d", database,
                         "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", query], stdout=subprocess.PIPE)
        if result.stdout.strip() != b"0": raise RuntimeError("Restore refused: target database is not empty: " + database)
        with (directory / (database + ".dump")).open("rb") as archive:
            execute(compose(args) + ["exec", "-T", service, "pg_restore", "--list"], stdin=archive, stdout=subprocess.DEVNULL)
    for service, database, user in DATABASES:
        with (directory / (database + ".dump")).open("rb") as archive:
            execute(compose(args) + ["exec", "-T", service, "pg_restore", "-U", user, "-d", database,
                    "--no-owner", "--no-acl", "--exit-on-error", "--single-transaction"], stdin=archive, stdout=subprocess.DEVNULL)
    print("Restored both databases. Start web/ai and verify company data and knowledge searches.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("backup", "restore", "verify"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--compose", type=Path, default=Path("compose.yaml"))
    parser.add_argument("--env-file", type=Path)
    parser.add_argument("--project")
    args = parser.parse_args()
    try:
        if args.action == "verify": validate_backup(args.directory); print("Both backup checksums verified.")
        else: {"backup": backup, "restore": restore}[args.action](args)
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__": main()
