SELECT pg_advisory_xact_lock(839472521);
CREATE TABLE IF NOT EXISTS "CompanyProfiles" (
    "Key" varchar(64) PRIMARY KEY, "Name" varchar(200) NOT NULL,
    "Website" varchar(500) NOT NULL DEFAULT '', "IsDemo" boolean NOT NULL DEFAULT false
);
INSERT INTO "CompanyProfiles" ("Key", "Name", "Website", "IsDemo") VALUES
    ('fti-demo', 'FTI Group Demo', 'https://ftigroup.com.au/', true),
    ('blank-company', 'Empty Company - ready to configure', '', false)
ON CONFLICT ("Key") DO NOTHING;
