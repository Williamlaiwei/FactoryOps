# FactoryOps — Order, Inventory & Supplier Management

**Live demo:** [https://45.79.238.119](https://45.79.238.119)

FactoryOps is a full-stack operations application for managing customer orders,
stock commitments, supplier replenishments and product reference documents. It
projects future availability using delivery dates and expected receipts, while
keeping planned stock separate from physical inventory.

## Resume entry

**FactoryOps | Full-Stack Operations Application**  
*C#, ASP.NET Core MVC, Entity Framework Core, PostgreSQL, Python, FastAPI,
pgvector, JavaScript, Docker, Linux*

- Built and deployed a full-stack order and inventory application with company-specific catalogs, customer delivery sites, supplier management and CSV data imports.
- Implemented date-based stock reservations, replenishment scheduling and shipment transactions, with shortage warnings and interactive one- and three-month inventory forecasts.
- Developed a document ingestion and semantic search service using Python, FastAPI, local text embeddings and pgvector, returning source citations and matching catalog products.
- Protected data consistency with atomic database transactions, optimistic concurrency checks, duplicate-submission safeguards and validated stock CSV export/import workflows.
- Containerized the web application, search service and two PostgreSQL databases with Docker Compose; deployed to a Linux VM with HTTPS, persistent storage, health checks and backup tooling.

## Short version

Developed and deployed FactoryOps, a C#/ASP.NET Core and PostgreSQL operations
application combining order management, date-based inventory reservations,
supplier replenishment planning and document semantic search. Built a Python
FastAPI/pgvector search service and deployed the Docker-based application to a
Linux VM with HTTPS.

## Main features

| Area | Implemented functionality |
| --- | --- |
| Work orders | Create and edit orders with multiple products, customer/site delivery details, delivery dates, production status, stock holds, cancellation and shipment. |
| Inventory | Record physical counts, maintain stock movement history, set reorder levels and lead times, and manage opening counts or adjustments individually and in bulk. |
| Availability planning | Calculate future availability from physical stock, dated incoming receipts and outbound commitments; display shortage warnings with explicit operator confirmation when overriding availability. |
| Forecast dashboard | Switch products and one- or three-month ranges; inspect actual stock, planned incoming supply and projected balances, with red risk labels in the product picker. |
| Suppliers and replenishment | Maintain supplier details, link multiple suppliers to each product, set a default, schedule or reschedule deliveries, and receive stock. |
| Catalog and customers | Manage product SKUs, aliases, specifications and ordering units; import product and customer/site CSVs using downloadable examples. |
| Stock CSV workflow | Download current inventory, edit it and upload it for preview and confirmation; validate units, stale versions and adjustment reasons before applying changes atomically. |
| Document knowledge | Upload PDF, CSV and XLSX references; extract, chunk and index content; search with local embeddings and return source citations alongside catalog candidates. |
| Company profiles | Create and switch company profiles with separate business records, document knowledge and workflow settings. |

## Engineering highlights

- ASP.NET Core MVC and Razor provide the web interface; EF Core accesses the business PostgreSQL database.
- A separate FastAPI service stores reference documents and vectors in PostgreSQL with pgvector, using local `BAAI/bge-small-en-v1.5` embeddings.
- Product snapshots preserve the names, SKUs and units recorded on existing order lines after catalog changes.
- Stock is reserved when orders are confirmed and deducted on shipment; planned receipts increase physical stock only when received.
- Transactional, repeatable SQL upgrades extend existing databases without resetting business records.
- Automated HTTP/SQL regressions and Playwright browser flows cover order editing, catalog operations, inventory, suppliers and company switching. The latest inventory/supplier run passed 46 HTTP/SQL cases and two browser flows.
- The Linux deployment runs five services through Docker Compose, with Caddy-managed HTTPS, automatic restart policies, persistent volumes and database backup/restore tools.

## Current scope

Semantic document search is implemented. OpenAI model/provider preferences are
configurable; automated phone/email order intake, AI order extraction, human
review workflows and generated replies remain planned work.

The public demo uses company profiles for data organization. User authentication,
role-based permissions and a complete operator audit trail remain future work.
