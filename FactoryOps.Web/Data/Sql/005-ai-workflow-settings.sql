SELECT pg_advisory_xact_lock(839472521);

CREATE TABLE IF NOT EXISTS "AiWorkflowSettings" (
    "CompanyKey" varchar(64) PRIMARY KEY REFERENCES "CompanyProfiles" ("Key") ON DELETE RESTRICT,
    "OrderModel" varchar(100) NOT NULL,
    "TranscriptionModel" varchar(100) NOT NULL,
    "EmbeddingModel" varchar(100) NOT NULL,
    "IntakeMode" varchar(32) NOT NULL,
    "UsePhoneRecords" boolean NOT NULL,
    "UseEmailOrders" boolean NOT NULL,
    "MailboxAddress" varchar(254) NOT NULL,
    "ReplyChannel" varchar(16) NOT NULL,
    "ReplyLanguage" varchar(16) NOT NULL,
    "ReplyFromAddress" varchar(254) NOT NULL,
    "ReplySignature" varchar(2000) NOT NULL,
    "MissingInfoInstructions" varchar(2000) NOT NULL
);

-- Seed only missing company settings. Never reset an operator's saved preferences.
INSERT INTO "AiWorkflowSettings" (
    "CompanyKey", "OrderModel", "TranscriptionModel", "EmbeddingModel", "IntakeMode",
    "UsePhoneRecords", "UseEmailOrders", "MailboxAddress", "ReplyChannel", "ReplyLanguage",
    "ReplyFromAddress", "ReplySignature", "MissingInfoInstructions"
)
SELECT "Key", '', '', '', 'ReviewBeforeImport', true, true, '', 'Email', 'en-AU', '', '', ''
FROM "CompanyProfiles" ON CONFLICT ("CompanyKey") DO NOTHING;

INSERT INTO "SchemaUpdates" ("Name") VALUES ('005-ai-workflow-settings') ON CONFLICT DO NOTHING;
