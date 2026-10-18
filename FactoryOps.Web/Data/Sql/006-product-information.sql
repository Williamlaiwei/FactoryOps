-- Additive product information fields. Existing product and order data stay intact.
SELECT pg_advisory_xact_lock(839472521);

ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "PackingSize" varchar(500) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "RoughIn" varchar(300) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "HeatingMethod" varchar(200) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "FlushingMethod" varchar(200) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "CeramicBody" varchar(300) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "Color" varchar(300) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "DisplayMode" varchar(200) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "NetWeight" varchar(100) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "GrossWeight" varchar(100) NOT NULL DEFAULT '';

INSERT INTO "SchemaUpdates" ("Name") VALUES ('006-product-information') ON CONFLICT DO NOTHING;
