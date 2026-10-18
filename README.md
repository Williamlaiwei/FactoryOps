# FactoryOps

ASP.NET Core MVC (.NET 8), Entity Framework Core and PostgreSQL 16 order management.

Create a local `.env` file containing `POSTGRES_PASSWORD=<your local password>`, then run:

```powershell
docker compose up -d --build
```

Open http://localhost:8080/WorkOrders. Docker keeps PostgreSQL records in the existing
`postgres_data` volume. Do not delete this volume to apply application changes.

## Phase 1: order editing

Choose **Edit** beside an order. Change the customer, active construction site or
company delivery address, products, quantities, requested date and instructions.
Add or remove lines and choose **Save Changes**. The order keeps its original ID and
status, and retained lines keep their IDs. Older single-product orders are loaded
from their header values and acquire item rows when saved.

The existing three products remain the Phase 1 choices. A configurable catalog and
AI functionality are outside this phase. A past delivery date can be retained on
an older order; a changed delivery date must be today or later.

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

The development schema was inspected before implementation and already contains
the required tables, relationships and item quantity constraint. No SQL schema
updates or new EF migrations are needed. `EnsureCreated` initializes an empty
database; it does not upgrade an existing schema.

## Verification

Prerequisites: running Docker Desktop, the Compose database service, `.env` password,
and Python 3.10+ (the HTTP/database suite uses only the Python standard library).

```powershell
dotnet build FactoryOps.Web/FactoryOps.Web.csproj
python tests/order_editing.py
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
```

The headless Edge check exercises customer search, three-line creation, dynamic
addition/removal and field renumbering, delivery changes, saving/reloading,
duplicate-submit suppression and adding a line after removing all lines. It saves
an edit-page screenshot to `artifacts/order-editing.png`.
