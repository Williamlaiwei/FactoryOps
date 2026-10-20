# Remote demo verification — 19 October 2026

Host: `45.79.238.119`; deployed image: `factoryops-web:vm-demo-20261019`.

## Update — 20 October 2026

- Public URL is now `https://45.79.238.119`, with a trusted Let's Encrypt IP
  certificate and automatic renewal through Caddy's persistent storage.
- External HTTP redirects to HTTPS with 308; anonymous HTTPS receives 401.
- Shared demo login is active. Read its credentials on the VM using the private
  file described in [VM.md](VM.md), not through source control or release packages.
- All 24 live routes, both forecast ranges and knowledge original/retrieval
  checks passed again with the demo login over HTTPS.
- `OPENAI_API_KEY` is present in web. Actual API/model access remains untested.
- Configuration was backed up before activation; the existing crawler stayed active.
- Repeat live checks with `/opt/factoryops/checks/20261020/vm_smoke.py`.

## Functional regression

Tests ran on the VM against its exact running web-image digest, in Production
mode, with five disposable databases and a loopback-only test listener. The test
containers were limited to 0.5 CPU and 384 MB. No OpenAI requests were made.

| Suite | Passed |
| --- | ---: |
| Inventory, reservations, replenishment, shipping, stock CSV | 37/37 |
| Order editing and validation | 12/12 |
| Product CRUD, lookup and imports | 12/12 |
| Company creation and separation | 10/10 |
| AI workflow preference persistence and validation | 6/6 |
| Total | 77/77 |

After the run, fingerprints of all 13 business and two knowledge tables were
unchanged. All disposable test containers and databases were removed.

To repeat the suite on this VM:

```sh
python3 /opt/factoryops/checks/20261019/vm_regression.py
```

## Actual deployed instance

- 24 principal routes, CSV templates/download and search endpoints returned 200
  through the actual Caddy listener on the VM.
- One- and three-month projection endpoints returned valid results.
- The authenticated internal SQL catalog callback returned 16 products.
- Both knowledge documents' original-file SHA256 checksums matched their stored
  metadata. Retrieval for `Fast Tread` returned five citations and required review.
- Edge through a temporary SSH tunnel opened five desktop and five mobile pages,
  with no JavaScript page errors or mobile page overflow. Business CSV files were
  not saved locally; CSV roundtrip mutations used the disposable remote databases.
- All 157 local application/AI source files match the deployed package manifest.
- The existing crawler stayed active throughout testing.
- Direct external TCP 22 connected; TCP 80 timed out. Public browser access is
  not verified, despite successful VM-local and tunnel checks.

Repeat the live read-only checks:

```sh
python3 /opt/factoryops/checks/20261019/vm_smoke.py
```

## Outstanding items recorded at the initial deployment

1. Permit inbound TCP 80 in Linode Firewall `RMB-SCRIBD-W01-FW`, confirm its
   attachment to the VM, then retest `http://45.79.238.119` from outside.
2. Configure the remote `OPENAI_API_KEY` using [VM.md](VM.md). It is currently
   empty. Presence in Settings is not proof of API validity or model access.
3. Enter physical inventory through bulk count or stock CSV. FTI demo currently
   has 16 products, zero tracked stock records, two orders and no replenishments.
   Unknown stock and an empty measured-stock chart are expected until counts exist.
4. Phone records, email ingestion, OpenAI order extraction/review and generated
   replies/sending remain planned. Settings save preferences; the current knowledge
   index still uses local embeddings. A key alone does not implement these flows.
5. Choose a backup schedule, retention and off-VM destination. Deployment provides
   manual backup/restore tools but installs no automatic backup schedule.
6. Decide where ongoing data entry happens. Local and cloud SQL are independent;
   subsequent local orders, stock changes and settings do not sync automatically.

These checks validate the existing features and deployment paths, not every
possible browser interaction or the planned AI intake workflow.

The network rule and key-presence items above are resolved by the 20 October
update. Initial stock counts, AI intake/reply development, API validation and
backup/data-entry decisions remain outstanding.
