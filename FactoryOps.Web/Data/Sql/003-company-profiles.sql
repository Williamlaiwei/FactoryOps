SELECT pg_advisory_xact_lock(839472521);

DO $$ DECLARE t text; BEGIN
    FOREACH t IN ARRAY ARRAY['Products','Customers','ConstructionSites','WorkOrders'] LOOP
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "CompanyKey" varchar(64) NOT NULL DEFAULT ''fti-demo''', t);
        EXECUTE format('ALTER TABLE %I ALTER COLUMN "CompanyKey" SET DEFAULT ''fti-demo''', t);
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_' || t || '_CompanyProfiles_CompanyKey' AND conrelid = format('%I',t)::regclass) THEN
            EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY ("CompanyKey") REFERENCES "CompanyProfiles" ("Key") ON DELETE RESTRICT', t, 'FK_' || t || '_CompanyProfiles_CompanyKey');
        END IF;
        EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %I ("CompanyKey")', 'IX_' || t || '_CompanyKey', t);
    END LOOP;
END $$;
DROP INDEX IF EXISTS "IX_Products_Sku";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Products_CompanyKey_Sku" ON "Products" ("CompanyKey", "Sku");
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SourceUrl" varchar(500) NOT NULL DEFAULT '';
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT false;
ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SkuIsVerified" boolean NOT NULL DEFAULT true;
INSERT INTO "SchemaUpdates" ("Name") VALUES ('003-company-profiles') ON CONFLICT DO NOTHING;
