#!/usr/bin/env bash
# Stage a new, empty release on an existing VM. The public proxy starts separately.
set -euo pipefail
archive="$1"
release="$2"
expected_sha="$3"
tag="$4"
site="$5"
test -d "$release"
test ! -e "$release/.env.cloud"
python3 - "$archive" "$release" "$expected_sha" <<'PY'
import hashlib,json,pathlib,sys,zipfile
source,target,expected=pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2]),sys.argv[3]
with source.open('rb') as f:
    assert hashlib.file_digest(f,'sha256').hexdigest()==expected, 'Transfer checksum mismatch'
assert not any(target.iterdir()), 'Release directory must be empty'
with zipfile.ZipFile(source) as z:
    for name in z.namelist():
        p=pathlib.PurePosixPath(name)
        assert not p.is_absolute() and '..' not in p.parts, 'Invalid archive path'
    z.extractall(target)
manifest=json.loads((target/'release-manifest.json').read_text())
for name,checksum in manifest['files'].items():
    with (target/name).open('rb') as f:
        assert hashlib.file_digest(f,'sha256').hexdigest()==checksum, 'Release file checksum mismatch'
print('Verified transfer and release file checksums.')
PY
cd "$release"
docker image load --input images.tar
python3 tools/create_cloud_env.py --site "$site" --http
python3 - "$tag" <<'PY'
from pathlib import Path
import sys
p=Path('.env.cloud')
p.write_text(p.read_text().replace('RELEASE_TAG=demo\n','RELEASE_TAG='+sys.argv[1]+'\n'))
p.chmod(0o600)
PY
dc=(docker compose -p factoryops-demo --env-file .env.cloud -f compose.cloud.yaml -f compose.vm.yaml)
"${dc[@]}" config --quiet
"${dc[@]}" up -d --wait --wait-timeout 180 db knowledge-db
python3 tools/cloud_data.py restore data --compose compose.cloud.yaml --env-file .env.cloud --project factoryops-demo
"${dc[@]}" up -d --wait --wait-timeout 180 web ai
"${dc[@]}" ps --format '{{.Service}} {{.State}} {{.Health}}'
systemctl is-active scribd-catalog.service || true
echo 'FactoryOps databases and private application services are staged. Public proxy has not started.'
