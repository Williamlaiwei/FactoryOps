SELECT pg_advisory_xact_lock(839472521);
ALTER TABLE "WorkOrders" ADD COLUMN IF NOT EXISTS "FulfillmentStatus" varchar(24) NOT NULL DEFAULT 'Unconfirmed';
ALTER TABLE "WorkOrders" ADD COLUMN IF NOT EXISTS "ShippedAt" timestamptz;
ALTER TABLE "WorkOrders" ADD COLUMN IF NOT EXISTS "CreationToken" uuid;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkOrders_CompanyKey_CreationToken" ON "WorkOrders" ("CompanyKey","CreationToken");
CREATE TABLE IF NOT EXISTS "StockReservations" (
    "WorkOrderId" integer NOT NULL REFERENCES "WorkOrders" ("Id") ON DELETE RESTRICT,
    "ProductId" integer NOT NULL REFERENCES "Products" ("Id") ON DELETE RESTRICT,
    "CompanyKey" varchar(64) NOT NULL DEFAULT 'fti-demo' REFERENCES "CompanyProfiles" ("Key") ON DELETE RESTRICT,
    "Quantity" bigint NOT NULL,
    "UnitOfMeasure" varchar(32) NOT NULL,
    "NeedDate" date NOT NULL,
    "Status" varchar(16) NOT NULL,
    PRIMARY KEY ("WorkOrderId","ProductId"),
    CONSTRAINT "CK_StockReservations_Values" CHECK ("Quantity" > 0 AND "Status" IN ('Held','Shipped','Released'))
);
CREATE INDEX IF NOT EXISTS "IX_StockReservations_ProductId" ON "StockReservations" ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_StockReservations_CompanyKey_ProductId_NeedDate" ON "StockReservations" ("CompanyKey","ProductId","NeedDate");
-- Preserve valid existing open commitments, without changing physical stock or issuing old completed orders.
DO $$ BEGIN
IF NOT EXISTS (SELECT 1 FROM "SchemaUpdates" WHERE "Name"='008-stock-reservations') THEN
    UPDATE "WorkOrders" SET "FulfillmentStatus"='Historical' WHERE "Status"='Completed';
    UPDATE "WorkOrders" o SET "FulfillmentStatus"='Reserved'
    WHERE o."Status" <> 'Completed' AND o."Status" <> 'Cancelled'
      AND EXISTS (SELECT 1 FROM "WorkOrderItems" i WHERE i."WorkOrderId"=o."Id")
      AND NOT EXISTS (
        SELECT 1 FROM "WorkOrderItems" i LEFT JOIN "Products" p ON p."Id"=i."ProductId"
        WHERE i."WorkOrderId"=o."Id" AND (p."Id" IS NULL OR p."CompanyKey"<>o."CompanyKey"
          OR btrim(i."UnitOfMeasureSnapshot")='' OR lower(btrim(i."UnitOfMeasureSnapshot"))<>lower(btrim(p."UnitOfMeasure")))
      );
    INSERT INTO "StockReservations" ("WorkOrderId","ProductId","CompanyKey","Quantity","UnitOfMeasure","NeedDate","Status")
    SELECT o."Id",i."ProductId",o."CompanyKey",sum(i."Quantity"),p."UnitOfMeasure",o."DueDate",'Held'
    FROM "WorkOrders" o JOIN "WorkOrderItems" i ON i."WorkOrderId"=o."Id" JOIN "Products" p ON p."Id"=i."ProductId"
    WHERE o."FulfillmentStatus"='Reserved'
    GROUP BY o."Id",i."ProductId",o."CompanyKey",p."UnitOfMeasure",o."DueDate"
    ON CONFLICT DO NOTHING;
    INSERT INTO "SchemaUpdates" ("Name") VALUES ('008-stock-reservations');
END IF;
END $$;
