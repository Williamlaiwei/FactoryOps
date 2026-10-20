# Existing VM demo

Deployment prepared on 19 October 2026:

- Host: `root@45.79.238.119`, Ubuntu 24.04, 2 CPUs / 4 GB RAM.
- URL: `https://45.79.238.119` (public HTTPS demo, no login required).
- Release: `/opt/factoryops/releases/20261019-demo`.
- Current shortcut: `/opt/factoryops/current`.
- Compose project: `factoryops-demo`.
- Application image tag: `order-items-20261020` (web updated; AI image unchanged).
- Configuration: `.env.cloud` in the release directory, mode 0600. Never commit it.
- Uploaded archive: `/opt/factoryops/incoming/factoryops-vm-live-20261019.zip`.

Docker starts on boot; all five services use `restart: unless-stopped`.
The existing `scribd-catalog.service` crawler remains active. Optional VM resource
limits are applied through `compose.vm.yaml`.

## Start and inspect

```sh
cd /opt/factoryops/current
docker compose -p factoryops-demo --env-file .env.cloud -f compose.cloud.yaml -f compose.vm.yaml ps
docker compose -p factoryops-demo --env-file .env.cloud -f compose.cloud.yaml -f compose.vm.yaml up -d --wait
curl -sS -o /dev/null -w '%{http_code}\n' https://45.79.238.119/healthz
```

HTTPS was activated on 20 October 2026. The public certificate validates for the
IP address and HTTP redirects with 308. The shared password was subsequently
removed for portfolio sharing; anonymous HTTPS requests and the probe above
should return 200.

Keep inbound TCP 80 and 443 open in Linode Firewall `RMB-SCRIBD-W01-FW`. Caddy
automatically renews the short-lived Let's Encrypt IP certificate using HTTP-01
on port 80. Its persistent `caddy_data` volume must survive updates. SQL, AI and
web container ports remain private.

`CADDY_CONFIG_FILE=./deploy/Caddyfile.public` selects public HTTPS;
`DEMO_TLS_NAME` supplies the IP name for clients that omit TLS SNI.
Only the proxy was recreated when public access was enabled. Configuration
backup paths are recorded in `/opt/factoryops/https-state.json`.

For a cover letter, share only `https://45.79.238.119`; no credentials are needed.

## Optional shared password

To restore a shared password, run the following commands. These explicitly
switch back to `Caddyfile.protected` and recreate only the proxy:

```sh
cd /opt/factoryops/current
python3 /opt/factoryops/tools/configure_demo_access.py --site 45.79.238.119
docker compose -p factoryops-demo --env-file .env.cloud -f compose.cloud.yaml -f compose.vm.yaml up -d --no-deps --wait proxy
```

The credentials file `/opt/factoryops/demo-access.txt` is mode 0600 and excluded
from source packages. Only a bcrypt hash goes into `.env.cloud`. Treat
configuration backups as secrets too. While public access is selected, the
saved password does not restrict access.

To switch back to public access after restoring the password:

```sh
cd /opt/factoryops/current
python3 /opt/factoryops/incoming/enable-public-demo.py
```

## Data and backups

Both SQL databases were restored from `cloud-backup-vm-20261019`; all 13 business
tables and both knowledge tables matched their local snapshot fingerprints.
Local and cloud are independent copies; subsequent local changes do not sync.
Download fresh stock CSVs from the cloud instance before editing/reimporting.

```sh
cd /opt/factoryops/current
python3 tools/cloud_data.py backup /opt/factoryops/backups/demo-YYYYMMDD-HHMMSS --compose compose.cloud.yaml --env-file .env.cloud --project factoryops-demo
```

Choose a new backup directory each time and copy it off the VM. Preserve the
`factoryops-demo_protection_keys` volume with server backups as well. An automatic
backup schedule has not been installed.

## Updates

Back up first. Keep the same project name, configuration and named volumes. Load
new tagged images, update `RELEASE_TAG` in the existing `.env.cloud`, then run the
start command above. Keep old image tags for schema-compatible application
rollback. Never use `down -v` during updates.

To stop only this application, use the same Compose prefix with `stop`; do not
stop Docker itself because it may later host other applications.

For a domain, point its DNS to this VM and configure demo access again with
`--site demo.example.com`, then recreate web/proxy. Caddy manages the certificate.

## Configure OpenAI

The remote key was present in the web container on 20 October 2026; API access
has not been tested. To replace it, run these commands in an
interactive SSH session. The prompt hides the input and the tool preserves
`FACTORYOPS_AI_KEY`, SQL credentials and other settings. It atomically writes
the setting with file permissions 0600. Never paste the actual key into a shell
command, Git or chat.

```sh
ssh root@45.79.238.119
cd /opt/factoryops/current
python3 /opt/factoryops/tools/set_cloud_openai_key.py
docker compose -p factoryops-demo --env-file .env.cloud -f compose.cloud.yaml -f compose.vm.yaml up -d --no-deps --wait web
```

Paste the OpenAI secret key at the hidden prompt and press Enter. Open Settings
after recreation: it should say `Configured (not tested)`. This verifies only
the presence of the server setting, not API access or billing/model permissions.
Current knowledge embeddings run locally in the AI container. Phone/email order
intake, AI order review and automatic reply workflows remain planned features.

## Completed checks

- Five running services; web, AI and both databases passed health checks.
- Ten principal pages/download routes returned HTTP 200 through Caddy on the VM.
- Headless Edge opened Dashboard, Work Orders, Inventory, Settings and Knowledge
  through an SSH tunnel with no JavaScript page errors. Desktop/mobile
  screenshots are in local `artifacts/vm-dashboard-*.png`.
- The private catalog callback returned 16 products; knowledge retrieval
  returned five citations and one SQL catalog candidate for `Fast Tread`.
- About 2.2 GB RAM remained available after startup; the crawler stayed active.
- Public HTTPS and HTTP-to-HTTPS redirects passed external checks on 20 October.
  After removing the shared password, anonymous requests to the principal pages
  return 200 without an authentication challenge.
