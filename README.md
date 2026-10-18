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

**Import CSV** includes a downloadable template. Files must be UTF-8 CSV, at most
5 MB and at most 1,000 data rows. Required headers: `Sku,Name,UnitOfMeasure`.
Optional headers: `Description,Category,Dimensions,Specifications,Packaging,IsActive,Aliases`.
Headers ignore case and surrounding whitespace. `IsActive` accepts `true`/`false`
and defaults to true if blank. Separate aliases with `|`; quote fields containing
commas or newlines. Import creates new products only. Duplicate SKUs (including
existing products), malformed rows and validation failures reject the whole file.
Use the edit page to change an existing product.

`/Products/Lookup?q=...` matches an active product by exact SKU first, then exact
name or alias, ignoring case. Shared aliases return every candidate; no fuzzy or
AI matching is performed. Catalog search also supports partial text.

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
- Startup applies `FactoryOps.Web/Data/Sql/002-product-catalog.sql` inside a
  transaction with a PostgreSQL advisory lock. It adds tables/columns/indexes and
  a one-time initialization marker. No EF migrations, database resets or volume
  removal are used. Repeated application preserves catalog changes and deletions.

The one-time initializer imports the original three choices and names already
present in orders. These compatibility products receive generated `LEGACY-...`
codes and an `each` default unit: **review and replace these placeholders with
your actual company codes and units**. Existing order-line names and quantities
are preserved. Known product links are populated, while unknown historical SKU
and unit snapshots remain blank instead of inventing historical values.

The Company screen now selects an FTI demo profile or an initially empty company.
Company names and websites can be edited there. Products, customers, sites and
orders are scoped to the selected profile; the same SKU may exist in different
profiles. Company selection is a local operator feature, not user authentication
or an enterprise tenant security system.

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

## Verification

Prerequisites: running Docker Desktop, the Compose database service, `.env` password,
and Python 3.10+ (the HTTP/database suite uses only the Python standard library).

```powershell
dotnet build FactoryOps.Web/FactoryOps.Web.csproj
python tests/order_editing.py
python tests/order_editing.py --catalog
python tests/order_editing.py --companies
python tests/order_editing.py --knowledge
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
