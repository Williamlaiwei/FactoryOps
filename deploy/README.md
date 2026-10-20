# Cloud demo release

This deploys the existing app and both PostgreSQL 16 databases on one Linux
amd64 VPS. Caddy provides HTTPS; Docker volumes keep SQL records, documents,
vectors, application protection keys and certificates across container updates.
Use a domain, Ubuntu 24.04/other supported Linux, Python 3.10+, and Docker Engine
with the Compose v2 plugin. Start with 4 GB RAM and 40+ GB disk for a low-traffic
demo; this is a sizing estimate, not a load-test result. Build images locally to
avoid build-time memory pressure on a small VPS. ARM plans are not validated.

The price comparison is in [HOSTING.md](HOSTING.md). No cloud resources or charges
are created by packaging. This is the current demo with editable company data;
user accounts, per-user permissions and full operator audit history are pending.

## 1. Prepare a release on the current Windows machine

Run in the repository root, with Docker Desktop running:

```powershell
python tools/cloud_data.py backup artifacts/cloud-backup-demo
python tools/package_cloud.py --data artifacts/cloud-backup-demo --output artifacts/releases/factoryops-demo.zip
```

The ZIP includes current source, additive schema scripts, cloud configuration,
and both database dumps. It excludes real `.env` files, API keys, generated build
files and test fixtures. It contains existing business/document data, so transfer
it through your own SSH/SCP connection. A source ZIP needs internet access on the
server for Docker base images, NuGet/Python packages and the embedding model build.

To build Linux/amd64 application images into the ZIP on this machine, use a new
output filename and `--images` (larger file, longer build):

```powershell
python tools/package_cloud.py --images --tag demo --data artifacts/cloud-backup-demo --output artifacts/releases/factoryops-demo-images.zip
```

Backup folders and ZIPs are never overwritten. Use a new date/version in names
for subsequent releases. The manifest records SHA-256 checksums. `pg_dump`
captures all schemas/data/sequences in each database, including schema-update
markers, holds and knowledge document originals stored as bytea. These are
portable database archives, not copies of Docker volume directories.

## 2. Configure the server

Create a VPS and point a DNS A record, for example `demo.example.com`, at its
public IPv4. Open ports 80/TCP, 443/TCP and optionally 443/UDP, plus your SSH port.
The compose file publishes only Caddy; PostgreSQL and the knowledge API stay on
the container network. The bundle uses subnet `172.30.88.0/24`; if this conflicts
with another network, change the subnet, Caddy address and ReverseProxy address
together before starting.

Upload and extract the ZIP into a new release directory. Run from that directory:

```sh
python3 tools/create_cloud_env.py --site demo.example.com
docker compose --env-file .env.cloud -f compose.cloud.yaml config --quiet
```

This creates new random cloud database passwords and a matching internal service
key. It does not reuse or print the local `.env`. If OpenAI is needed later, set
the cloud key in `.env.cloud`; it is not currently required for knowledge search.
Keep this file between updates: changing the password variable does not rotate
the password of an existing PostgreSQL volume.

For an IP-address-only demo, create configuration with
`python3 tools/create_cloud_env.py --site YOUR_IP --http` instead. This selects
explicit HTTP and disables the app's HTTPS redirect. With a domain, keep the
default HTTPS configuration. On a shared VM, append `-f compose.vm.yaml` after
`-f compose.cloud.yaml` in Compose commands to apply CPU/memory limits. These are
maximums, not reserved resources; keep an eye on existing workloads.

If the package includes `images.tar`, load it:

```sh
docker image load --input images.tar
```

Otherwise build the two application images sequentially:

```sh
docker compose --env-file .env.cloud -f compose.cloud.yaml build web
docker compose --env-file .env.cloud -f compose.cloud.yaml build ai
```

## 3. Move SQL before starting the apps

Start only the two empty database services, then restore:

```sh
docker compose --env-file .env.cloud -f compose.cloud.yaml up -d --wait db knowledge-db
python3 tools/cloud_data.py restore data --compose compose.cloud.yaml --env-file .env.cloud
docker compose --env-file .env.cloud -f compose.cloud.yaml up -d --wait
```

Restore verifies both checksums and both targets before writing. It refuses any
target with user tables/views/sequences and never deletes or overwrites existing
databases. Each database restores in one transaction; there is no cross-database
transaction. If the second restore fails, retain the successful first restore and
investigate; do not bypass the nonempty check or delete volumes. Do not start web
or ai before restoring, because startup initializes/seeds empty databases.

For a completely new empty demo instead of migrating current records, omit the
restore and start the stack; normal application startup creates its schema/demo
profiles. These are alternative first-run paths.

## 4. Verify the cloud copy

- Open `https://demo.example.com/healthz`, then Dashboard, Products, Work Orders,
  Inventory, Companies, Settings and AI Knowledge.
- Compare company/customer/product/order counts and IDs with the source copy.
- Check actual counts, outbound holds, incoming plans and inventory history.
- Open a knowledge document and perform a search with source citations.
- Download current stock CSV, edit a test product and review its import preview.
- Try a short-stock order: Go Back saves nothing; Continue Anyway records demand;
  shipping still requires physical stock.
- Restart/recreate web and verify data and a recently exported StockSnapshot file
  remain usable. The cloud protection-key volume must survive updates.

Cloud keys start a new application protection scope. Download fresh stock CSVs
after moving from local to cloud; old local forms/exports are not a migration
format. SQL and document records move through the two full database backups.

Local and cloud are separate database copies. For the final cutover, briefly stop
web/ai writes on the source, take a fresh backup, restore to fresh cloud targets,
verify, then direct users to the cloud. A normal backup is consistent within each
database but does not freeze writes across both. Keep the old system and backup
until the cloud copy is checked; prevent new orders being entered in both copies.

## 5. Backups and updates

On the cloud server, save both databases regularly and copy the backup directory
off that VPS (a second disk on the same VPS is not an independent backup):

```sh
python3 tools/cloud_data.py backup /path/to/off-host-sync/backup-YYYYMMDD --compose compose.cloud.yaml --env-file .env.cloud
```

Back up the protection-key volume and Caddy data alongside normal server backups.
The database archive intentionally excludes server credentials and those volumes.
No automatic backup schedule is installed by this release; choose retention and
an off-host destination before relying on it beyond the demo.

For updates, back up first, keep the same project name, `.env.cloud` and volumes,
load/build a new release tag, then `up -d --wait`. Keep old application image tags.
Returning to an old image is safe only while its schema remains compatible with
the upgraded database. Database rollback means restoring the verified backup to
new empty targets; it can lose writes made after that backup. Never use
`docker compose down -v` to update the demo.

Sources: [PostgreSQL dump](https://www.postgresql.org/docs/16/app-pgdump.html),
[restore](https://www.postgresql.org/docs/16/app-pgrestore.html),
[Caddy HTTPS](https://caddyserver.com/docs/automatic-https),
[ASP.NET proxy handling](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
