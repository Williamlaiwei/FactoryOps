# Local deployment rehearsal — 19 October 2026

Validated with `python tests/cloud_deployment.py artifacts/cloud-backup-demo-20261019`:

- Both full PostgreSQL backups restore into fresh isolated containers.
- Every restored business/knowledge table matches its source table checksum.
- Repeated restore refuses populated targets and preserves their contents.
- Application startup preserves the restored SQL records.
- Production mode serves Dashboard, orders, inventory and knowledge through HTTPS.
- Forwarded HTTPS scheme and the database readiness probe work.
- Protection keys survive web-container recreation; a previously downloaded
  stock CSV remains importable through its preview.
- The private catalog callback works over the internal container network.
- Source databases are unchanged; only UUID-scoped test resources are removed.

HTTPS was rehearsed with Caddy's local test CA and no published host ports.
Public DNS, a purchased VPS, public certificates and provider performance remain
deployment-time checks. The prepared ZIP uses source plus verified SQL dumps;
application-image inclusion is optional via `package_cloud.py --images`.

## Existing VM deployment

See [VM.md](VM.md) for the actual release paths and operating commands. Both
databases have been restored and all 15 table fingerprints matched. Internal
HTTP routes, browser pages and knowledge retrieval passed. The external HTTP
check initially timed out. On 20 October, public HTTPS, HTTP redirects and a
shared demo password were activated and verified externally; authenticated live
route and knowledge checks passed. See [VM.md](VM.md) for login/password handling.
