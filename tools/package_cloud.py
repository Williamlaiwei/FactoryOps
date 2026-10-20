"""Build a reviewable source/SQL deployment ZIP; never include live credentials.

Optional --images builds Linux/amd64 application images and adds a Docker image
archive so a small VPS can load the release without building it on the server.
"""
import argparse
from contextlib import nullcontext
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile
from cloud_data import validate_backup, digest

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {"bin", "obj", "__pycache__", ".git", ".vs", ".venv", "venv", ".pytest_cache", "node_modules", "tests"}
SINGLE_FILES = ("dockerfile", ".dockerignore", "compose.cloud.yaml", "compose.vm.yaml", "README.md", "tools/cloud_data.py", "tools/package_cloud.py", "tools/create_cloud_env.py", "tools/set_cloud_openai_key.py", "tools/configure_demo_access.py")


def sources():
    paths = [ROOT / name for name in SINGLE_FILES]
    for directory in ("FactoryOps.Web", "ai", "deploy"):
        for path in (ROOT / directory).rglob("*"):
            relative = path.relative_to(ROOT)
            if path.is_file() and not path.is_symlink() and not EXCLUDED.intersection(relative.parts):
                if path.name.startswith(".env") or path.name == "appsettings.Development.json" or path.suffix.lower() in (".log", ".pyc", ".pfx", ".key"):
                    continue
                paths.append(path)
    return sorted(set(paths))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, help="Verified cloud_data.py backup directory to include")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--tag", default="demo")
    parser.add_argument("--images", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}", args.tag): parser.error("Use a simple Docker release tag.")
    if args.data: validate_backup(args.data)
    timestamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    target = (args.output or ROOT / "artifacts" / "releases" / f"factoryops-{timestamp}.zip").resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    manifest = {"createdUtc": timestamp, "releaseTag": args.tag, "platform": "linux/amd64", "includesDatabaseData": bool(args.data), "files": {}}
    # Source-only packaging needs no temporary directory. Keep optional image
    # staging beside the artifact, avoiding dependence on Windows TEMP ACLs.
    temporary_context = tempfile.TemporaryDirectory(prefix="factoryops-package-", dir=target.parent) if args.images else nullcontext(str(target.parent))
    with temporary_context as temporary:
        image_path = Path(temporary) / "images.tar"
        if args.images:
            for image, context, dockerfile in (("factoryops-web", ".", "dockerfile"), ("factoryops-ai", "ai", "ai/Dockerfile")):
                subprocess.run(["docker", "build", "--platform", "linux/amd64", "-t", f"{image}:{args.tag}", "-f", dockerfile, context], cwd=ROOT, check=True)
            subprocess.run(["docker", "image", "save", "-o", str(image_path), f"factoryops-web:{args.tag}", f"factoryops-ai:{args.tag}"], check=True)
        with zipfile.ZipFile(target, "x", zipfile.ZIP_DEFLATED) as archive:
            for path in sources():
                name = path.relative_to(ROOT).as_posix()
                archive.write(path, name)
                manifest["files"][name] = hashlib.sha256(path.read_bytes()).hexdigest()
            if args.data:
                for name in ("factoryops.dump", "knowledge.dump", "manifest.json"):
                    path = args.data / name
                    archive.write(path, "data/" + name)
                    manifest["files"]["data/" + name] = hashlib.sha256(path.read_bytes()).hexdigest()
            if args.images:
                archive.write(image_path, "images.tar")
                manifest["files"]["images.tar"] = digest(image_path)
            archive.writestr("release-manifest.json", json.dumps(manifest, indent=2) + "\n")
    print("Created", target)
    print("Includes both database dumps." if args.data else "Source only; no existing database records included.")
    print("Live .env files, API keys and local build/cache files are excluded.")


if __name__ == "__main__": main()
