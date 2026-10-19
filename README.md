# FactoryOps

ASP.NET Core MVC (.NET 8), Entity Framework Core and PostgreSQL 16 order management.

Create a local `.env` file containing `POSTGRES_PASSWORD=<your local password>`, then run:

```powershell
python tools/configure_ai.py
docker compose up -d --build
python tools/load_demo_knowledge.py --with-brochure
```

Open http://localhost:8080/WorkOrders. Docker keeps PostgreSQL records in the existing
`postgres_data` volume. Do not delete this volume to apply application changes.

## Phase 1: order editing

Choose **Edit** beside an order. Change the customer, active construction site or
company delivery address, products, quantities, requested date and instructions.
Add or remove lines and choose **Save Changes**. The order keeps its original ID and
status, and retained lines keep their IDs. Older single-product orders are loaded
from their header values and acquire item rows when saved.

A past delivery date can be retained on an older order; a changed delivery date
must be today or later. Phase 2 now supplies the products through the database catalog.

Validation keeps submitted values visible and rejects missing customers, inactive
or unrelated sites, addresses without delivery information, unknown products,
nonpositive or invalid quantities, excessive total quantities and empty orders.

Three implementation decisions:

- Creation and editing share a typed form, Razor partial, JavaScript and validation
  rules, keeping destination and item behavior consistent. Only editable fields
  bind from requests; address snapshots are resolved from database records.
- Header changes, retained item updates, new lines and removed lines use one EF Core
  `SaveChangesAsync` transaction. A failed save rolls everything back. The legacy
  header product and total quantity remain synchronized with the items.
- PostgreSQL's existing `xmin` system column provides optimistic concurrency
  checking without a new database column. Even an item-only edit updates the
  header. Stale edits and repeated saved edit submissions are rejected; reload the
  order before retrying. Submit buttons are disabled while saving, and successful
  requests redirect to the list. Creation does not provide server-side idempotency
  across independent requests or browser tabs.

The Phase 1 schema contained the required order tables, relationships and item
quantity constraint. Phase 2 adds the catalog through the SQL update below.
`EnsureCreated` initializes an empty database; it does not upgrade an existing schema.

## Phase 2: configurable product catalog

Open **Products** in the navigation or visit http://localhost:8080/Products.
Create, inspect, edit and delete unused products. Fields include SKU/product code,
name, description, category, ordering unit, optional dimensions/specifications/
packaging, aliases and active status. SKUs are normalized to uppercase and must
be unique. Categories and units are company-defined text, so no application code
changes are needed to populate a different company's catalog.

The order form loads database products and filters the choices by SKU, name or
alias. Inactive products cannot be added to new lines, but may remain on their
original order lines. Used products cannot be deleted; deactivate them instead.
The SKU, name and unit on an existing order line stay unchanged when its catalog
product is renamed, recoded, deactivated or given a different ordering unit. An
explicit change to another product takes a new snapshot. Quantities are whole
numbers, as in the existing order system; fractional quantities are outside Phase 2.
Once stock tracking starts, keep that product's ordering unit fixed; use a separate
SKU for another unit so inventory quantities are never silently converted.

**Import CSV** includes a downloadable template. Files must be UTF-8 CSV, at most
5 MB and at most 1,000 data rows. Required headers: `Sku,Name,UnitOfMeasure`.
Optional headers: `Description,Category,Dimensions,PackingSize,RoughIn,HeatingMethod,FlushingMethod,CeramicBody,Color,DisplayMode,NetWeight,GrossWeight,Specifications,Packaging,IsActive,Aliases`.
Headers ignore case and surrounding whitespace. `IsActive` accepts `true`/`false`
and defaults to true if blank. Separate aliases with `|`; quote fields containing
commas or newlines. Import creates new products only. Duplicate SKUs (including
existing products), malformed rows and validation failures reject the whole file.
Use the edit page to change an existing product.

`/Products/Lookup?q=...` matches an active product by exact SKU first, then exact
name or alias, ignoring case. Shared aliases return every candidate. Catalog
search includes saved product information. The knowledge candidate matcher also
uses size, packing size, rough-in, heating and flushing methods, ceramic body,
color, display mode and weights when suggesting catalog candidates; suggestions
still require human review.

Three catalog decisions:

- PostgreSQL `Products` and `ProductAliases` are the authoritative structured
  catalog. The single-company `ProductCatalog` service shares normalization,
  validation and deterministic matching across manual entry and CSV import.
  Document knowledge and AI retrieval remain outside the catalog.
- `WorkOrderItem.ProductId` links to the catalog, while the existing `ProductType`
  column keeps the ordered name and new SKU/unit snapshot columns keep ordered
  values. The product foreign key restricts deletion. Product edits and imports
  save their products and aliases in one transaction; product `xmin` checks reject
  stale edits without an extra schema column.
- Startup applies the additive SQL files in `FactoryOps.Web/Data/Sql` inside a
  transaction with a PostgreSQL advisory lock. `006-product-information.sql` adds
  the structured product fields without changing existing records or orders. It adds tables/columns/indexes and
  a one-time initialization marker. No EF migrations, database resets or volume
  removal are used. Repeated application preserves catalog changes and deletions.

The one-time initializer imports the original three choices and names already
present in orders. These compatibility products receive generated `LEGACY-...`
codes and an `each` default unit: **review and replace these placeholders with
your actual company codes and units**. Existing order-line names and quantities
are preserved. Known product links are populated, while unknown historical SKU
and unit snapshots remain blank instead of inventing historical values.

The Company screen starts with an FTI demo profile and an initially empty company.
Use **+ Create Company** to add more profiles with a name and an optional HTTPS
website. Creation selects the new profile and saves its default AI settings in the
same transaction. New profiles start with empty products, customers, sites, orders
and knowledge; they do not copy demo data or another company's settings. A per-form
token prevents repeated submissions from creating duplicates. Existing names and
websites can be edited on the Company screen. Data is scoped to the selected
profile; the same SKU may exist in different profiles. Company selection is a
local operator feature, not user authentication or an enterprise tenant security
system. Interface labels and messages are English, including the names of reply
language options; user data and document contents keep their original language.

## Inventory and replenishment planning

Open **Inventory** at http://localhost:8080/Inventory. Each company has separate
stock counts, movement history and replenishments. No demo stock is invented.
Products start as **Not tracked** until an operator enters an opening physical
count in the catalog ordering unit. Counts are whole quantities, including zero.
Later stock corrections require a reason and retain an immutable movement record.
Set a reorder point and calendar-day lead time on each tracked product.

Use **Inventory > Bulk Stock Count** to enter opening counts or update counts and
settings for up to 50 products per page. Search by SKU/name or filter **Not tracked
only**. Editing a number selects that row; only selected rows save. Counts replace
on-hand balances (including reserved stock), rather than adding quantities. Leave a
tracked count blank for settings-only updates. A shared reason is required for
changes to existing counts, and each nonzero adjustment retains a movement record.
All selected rows save atomically; stale stock/catalog versions reject the entire
batch. Replayed forms cannot apply adjustments again. Existing holds are preserved,
so a lower count can expose shortages. Use replenishment receipt for arriving goods.

**Inventory > Import Stock CSV** accepts up to 1,000 rows / 5 MB, with a visible
example and matching downloadable UTF-8 CSV. Required headings are `Sku`,
`UnitOfMeasure`, `CountedQuantity`; optional headings are `ReorderPoint`,
`LeadTimeDays`, `Reason`. SKUs must already exist in the active company and units
must match the catalog. Preserve leading zeros in SKUs. Counts are replacement
physical totals, including held stock; zero is valid. A blank count keeps tracked
stock, while a new stock record requires an opening count. Blank settings preserve
existing values or default to zero for new stock. Recounts require a reason.
Upload validates every row and shows changes without writing stock. Confirming
the preview saves the whole batch atomically. The protected preview is scoped to
the active company, expires after 15 minutes, and rejects stale stock/catalog
versions or repeated confirmation. Each nonzero adjustment retains its reason in
movement history. Existing holds remain unchanged and may expose shortages.

**Download Current Stock CSV** exports the active company's tracked products,
including zero balances and inactive products, in the same format accepted by the
importer. Untracked products are excluded. Edit counts/settings, supply reasons for
changed counts, and upload the entire file (up to 1,000 rows per import). Preserve
SKUs as text and leave `StockSnapshot` unchanged. This protected per-row snapshot
checks company, product identity, catalog version and stock version at upload;
changes since download reject the entire file before preview. A second version
check protects confirmation. The plain example remains available for opening
counts. CSV export/import events are written to application logs; nonzero stock
adjustments retain dated movement records and reasons.

Before enabling controlled access, add user identities and separate permissions
for viewing/exporting stock, importing counts, and confirming adjustments. Full
audit history is still pending: record the operator, action, timestamp, company,
batch, old/new counts and settings, and reason for export/import and manual edits.
Current application logs and movement records do not identify an authenticated
operator or provide a complete audit trail for settings-only changes.

Saving a new order confirms **outbound holds** for its requested delivery date.
Availability on a date is actual on-hand stock plus **incoming holds** expected by
that date, minus existing outbound holds due by that date. Unknown stock or a new
or worsened shortage triggers a **Stock Warning** dialog. **Go Back** saves nothing;
**Continue Anyway** explicitly accepts the displayed warning and saves the order
with its full outbound demand held. Physical stock remains unchanged; forecasts can
be negative. Untracked stock stays unknown until counted. Consent expires after 15
minutes and is bound to the company, order details and displayed warnings; changed
quantities or shortages require fresh consent. Accepted warnings are logged after
a successful save. Product/unit validation stays mandatory. Editing
an active order replaces its previous holds; cancellation releases them. Neither
operation changes actual on-hand stock. Confirmation retries from the same form do
not duplicate the order. Company transaction locks keep each availability check and
reservation atomic; overbooking requires explicit consent.

The overview and **Stock Schedule** show first shortage dates, lowest projected
balances, suggested quantities and order-by dates allowing for lead time. A late
arrival cannot cover an earlier delivery. Same-day incoming supply precedes outbound
holds for planning. Overdue incoming holds are excluded until received or rescheduled;
past-due outbound holds are treated as due today. Report actual losses and supplier
delays truthfully: existing commitments remain held and become shortage warnings;
new or worsened shortages require explicit consent when saving an order.

**Schedule Replenishment** records quantity, expected arrival, supplier and reference.
Plans can be edited, rescheduled or cancelled. **Receive** records a full physical
receipt once and increases on-hand stock atomically. Split partial deliveries into
separate planned receipts before receiving them. Planned supply is never actual stock.

Production status (**Pending**, **In Progress**, **Completed**) does not move stock.
Use **Ship Order** for physical dispatch: it requires actual received stock, protects
other dated holds, releases this order's outbound holds and deducts actual stock
atomically once. Changing production status afterward never returns stock. Shipped
and historical product lines/dates are fixed; delivery notes remain editable. A
customer return needs a real return operation, not a production-status toggle.

Open **Dashboard** at http://localhost:8080/Dashboard (also the home page). Select a
product and the next **1 or 3 calendar months**. The blue reference line is today's
actual stock held flat, green adds cumulative incoming holds, and red also subtracts
dated outbound holds. Pointer/keyboard inspection and a daily data table expose the
numbers. Untracked products show unknown stock instead of fabricated zeroes. Failed
requests clear stale series. Dates consistently use `BusinessTimeZone`, default
`Australia/Sydney`, independently of the container's UTC clock.

Repeatable `007-inventory.sql` and `008-stock-reservations.sql` add the inventory and
hold tables. The one-time reservation upgrade preserves valid existing open order
commitments as holds without inventing physical stock. Old completed orders are
marked **Historical** and never re-issued; old unlinked/unit-unknown orders remain
**Unconfirmed** for review. Explicitly confirm current units when correcting those
lines. The upgrade does not change existing quantities, production status or stock
movement history. Products with inventory history must be deactivated rather than
deleted. All mutation forms use antiforgery and company-tab checks.

## CSV import examples

Customer Management offers **Download Customer CSV Example** at
`/Customers/CsvTemplate`, containing all 12 customer/site columns and one fictional
row. Keep all column headings; repeat CompanyName for multiple sites. The catalog
import screen offers **Download Product CSV Example** at `/Products/CsvTemplate`,
with the supported product columns and a fictional `04412` SKU. Both pages show
the same CSV as a preview. Downloads use UTF-8 with a BOM. Keep SKU, phone and
postcode columns as text when editing in a spreadsheet to preserve leading zeroes.
Replace sample rows with real data before importing; downloading does not create
records. The browser regression imports both downloaded files into the temporary
database and checks their customer/site fields, numeric SKU and aliases.

## FTI demo and empty company

The sourced pack contains 13 demonstration entries based on
[FTI's public range](https://ftigroup.com.au/products/),
[Fast Tread](https://ftigroup.com.au/products/concrete-stairs-formwork/),
[BlueDeck](https://ftigroup.com.au/products/suspended-slab-formwork/),
[SureFit](https://ftigroup.com.au/products/steel-door-frames/),
[BluePly](https://ftigroup.com.au/products/f17-formply/) and
[published product FAQs](https://ftigroup.com.au/faqs/), reviewed 18 October 2026.
The pack covers stair formwork, three decking examples, frames, temporary treads,
film, handrails, hob/beam formwork, formply and door ranges. It is not a complete
manufacturer SKU list or a warehouse inventory feed. **DEMO codes, ordering units
and aliases are examples. No actual stock levels or prices are invented.**

`demo/fti-group/catalog.json` records the sources and data notices.
`python tools/build_demo_pack.py` regenerates the reviewable
`Data/Sql/004-fti-demo.sql` seed and `demo/fti-group/knowledge.csv` from it.
The seed runs once for `fti-demo`; it never seeds `blank-company` and never
recreates deleted demo products on restart. Existing records are assigned to
`fti-demo` without changing their original IDs or values. New additive SQL adds
company keys and changes SKU uniqueness to `(CompanyKey, Sku)`.

Select **Company → Empty Company** to configure another company's name and website,
then add/import its own products and knowledge documents. Its initial product,
customer, site, order and document lists are empty. Switching back preserves the
demo and existing records. Forms carry their company key so an old tab cannot
accidentally save into a newly selected company.

## Phase 3: company catalog knowledge

Open **AI Knowledge** at http://localhost:8080/Knowledge. Upload PDF, CSV or XLSX
reference documents, then search a product description, alias or SKU. Text-based
PDF extraction retains page and table metadata; spreadsheets retain sheet/row
metadata; CSV retains row metadata and optional HTTPS source links. Text is
chunked with overlap and embedded locally with `BAAI/bge-small-en-v1.5` (384
dimensions). FastAPI stores original files, metadata, chunks and vectors in a
separate PostgreSQL 16/pgvector service and persistent `knowledge_data` volume.
The original PostgreSQL container and `postgres_data` volume are retained.

Retrieval returns cited source passages and candidates from the active company's
live SQL catalog. Exact SKUs take precedence over exact names/aliases; broader
descriptions return candidates for review. Unknown document SKUs cannot become
catalog products. Every result requires review; retrieved text is data and cannot
approve or create an order. This phase supplies retrieval context, not LLM-generated
technical advice or purchase-order extraction. No paid AI API key is required.

The web app calls the internal FastAPI boundary with a generated service credential
and a server-selected company key. Python reads catalog data through the existing
app's read-only `/internal/catalog` endpoint; it has no order-writing API or direct
credentials for the order database. Every knowledge query includes its company key,
and document/chunk foreign keys also preserve company associations.

Upload deduplicates identical files within a company. **Replace Document** retains
the document ID and replaces the original/index atomically. **Re-index** rebuilds
its chunks and embeddings. Version checks reject stale changes. **Delete** removes
the document, original bytes and chunks together. Failed parsing, embedding or
database writes preserve the previous version. File limits are 20 MB, 200 PDF pages,
20 workbook sheets, 20,000 rows, 100 columns and 2,000 chunks per document. Scanned
PDFs need OCR before upload. The default embedding model targets English; no
multilingual retrieval accuracy is claimed. HNSW is available for vector indexing;
small-company filtered searches may use PostgreSQL's exact scan.

`tools/configure_ai.py` generates only missing local service credentials in ignored
`.env`; it never prints them or replaces the existing PostgreSQL password. The first
AI image build downloads pinned Python packages and the local embedding model.
The AI and knowledge database ports stay inside the Compose network.

`python tools/load_demo_knowledge.py` uploads the sourced CSV into the FTI profile
through the normal web upload form. `--with-brochure` additionally downloads and
indexes the public [2021 FTI brochure](https://ftigroup.com.au/wp-content/uploads/2021/07/Product-Brochure-2021_REV4_spreads.pdf).
That brochure is dated historical reference, not current availability or engineering
approval. Its downloaded file stays in ignored `artifacts/`; the source link is
retained with citations. Running the loader again deduplicates unchanged files.

The order-import integration contract is `POST /v1/retrieve` in Python, exposed to
the MVC workflow as antiforgery-protected `POST /Knowledge/Retrieve`. It returns
the company key, query, embedding model, review requirement, SQL product candidates
and document/version/page/sheet/row citations. Phase 4 can consume this boundary
without building a second catalog or order system. Order extraction and human
approval remain the next phases.

| Action | Function | Changes products/orders? |
| --- | --- | --- |
| Products → Import Product CSV | Validate and create structured products | Products only |
| AI Knowledge → Upload and Index | Store/extract/index reference documents | Neither |
| AI Knowledge → Search Knowledge | Return citations and SQL candidates | Neither |
| Work Orders → Create / Edit | Existing validated order workflow | Orders only |
| Company → Use This Company | Select a separate data/knowledge profile | Neither |

There are no duplicate work-order controllers or overlapping import actions. The
original order workflow still handles actual order persistence; knowledge content
cannot override it.

## AI and workflow settings (preparation for the next stage)

Open **Settings** at http://localhost:8080/Settings. Preferences are saved in
`AiWorkflowSettings`, one row per company, using the same active-company context
as orders and products. Additive, repeatable `005-ai-workflow-settings.sql` seeds
missing rows without resetting saved settings. Saving a stale version or a form
from a different company is rejected. No existing business records are changed.

The planned AI provider is **OpenAI**, with the fixed official
`https://api.openai.com/v1` endpoint. Optional model IDs cover order extraction and
reply generation, phone transcription and knowledge embeddings. They start blank;
the settings page does not invent or verify model access. Optional
`OPENAI_API_KEY` is supplied to the web server through Compose's environment, in
line with [OpenAI's server-side authentication guidance](https://developers.openai.com/api/reference/overview).
The page reports only whether a value is present, never its value or connection
success. There is no browser key field and no OpenAI request on save.

These are stored **preferences for upcoming features**:

- Include phone conversation records and email orders in the future AI page.
- An order inbox address, without mailbox credentials or a live connection.
- Default: human review before creating an order. Alternative: create an
  AI-labelled draft inside the system, then review. Both planned policies retain
  missing-field warnings and require human confirmation before fulfilment.
- Clarification reply instructions, English/Australian or Traditional Chinese,
  email/message/both preference, sender address and signature. Generated replies
  will be reviewed before sending. Email and messaging interfaces are reserved;
  provider authentication and sending adapters remain to be implemented.

This stage implements settings only. It does not implement intake, AI-labelled
drafts, review, reply generation, email syncing or message sending. Existing
knowledge search still uses its local embeddings and existing index. Connecting
OpenAI embeddings requires a deliberate re-indexing step before switching search;
storing a different embedding model here does not mix embedding spaces or change
the live index. The next stage is to connect the OpenAI-backed workflow using
these preferences and the existing catalog/retrieval boundary.

## Verification

Prerequisites: running Docker Desktop, the Compose database service, `.env` password,
and Python 3.10+ (the HTTP/database suite uses only the Python standard library).

```powershell
dotnet build FactoryOps.Web/FactoryOps.Web.csproj
python tests/order_editing.py
python tests/order_editing.py --catalog
python tests/order_editing.py --inventory --browser
python tests/order_editing.py --companies
python tests/order_editing.py --companies --browser
python tests/order_editing.py --knowledge
python tests/order_editing.py --settings --browser
```

The regression runner builds a separate web container on port 18080 and creates a
uniquely named temporary PostgreSQL database. It tests three-product creation,
quantity/product edits, line addition/removal, date and instruction persistence,
destination/customer changes, relationship validation, invalid binding, legacy
orders, stale/repeated submissions, status preservation and antiforgery handling.
An injected database failure verifies atomic rollback. Customer/site fixtures are
checked for changes. Development-table fingerprints are compared before and after.
The test container and temporary database are removed afterward; the development
database and volume remain intact. Run one instance of the runner at a time. Set
`FACTORYOPS_TEST_PORT` if port 18080 is already in use.

For the optional real-browser suite, install the test dependency in the ignored
artifacts folder. Microsoft Edge must be installed:

```powershell
npm install --prefix artifacts/browser-tools --no-save --package-lock=false playwright@1.64.1
python tests/order_editing.py --browser
python tests/order_editing.py --catalog --browser
python tests/order_editing.py --knowledge --browser
```

The headless Edge check exercises customer search, three-line creation, dynamic
addition/removal and field renumbering, delivery changes, saving/reloading,
duplicate-submit suppression and adding a line after removing all lines. It saves
an edit-page screenshot to `artifacts/order-editing.png`.

The catalog suite tests CRUD, normalization, aliases and ambiguous matches, CSV
validation and atomic rejection, case-insensitive headers and Unicode content,
inactive product rules, snapshot preservation, used-product deletion protection,
concurrent-edit detection and repeatable upgrades of the old order schema. Its
browser check exercises catalog management, alias filtering, CSV import/error
display and ordering an inactive historical product. It saves
`artifacts/product-catalog.png`. Both suites use temporary databases and verify
that development customer, site, order and item records remain untouched.

The company suite checks the empty slot, scoped SKUs, cross-company product IDs,
stale-tab rejection and repeatable SQL initialization. The knowledge suite checks
all three file formats, chunking, real embeddings, retrieval/citations, document
deduplication, updates/reindex/deletion, cross-company access, untrusted document
content and rollback on injected embedding/database failures. Its Edge check uses
the real MVC/Python boundary and saves `artifacts/ai-knowledge.png`.

The settings suite checks defaults, save/reload, validation, company isolation,
identity overposting, concurrent/repeated saves, antiforgery protection and
repeatable SQL initialization in a temporary database. The Edge check verifies
the actual form, source toggles, stale tabs and mobile layout, and saves
`artifacts/ai-settings.png`. It uses a clearly fake test credential to verify that
configured-key status never exposes a key. No OpenAI or messaging calls are made.
