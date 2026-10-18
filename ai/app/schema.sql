SELECT pg_advisory_xact_lock(839472522);
CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE IF NOT EXISTS knowledge_documents (
    id uuid PRIMARY KEY,
    company_key varchar(64) NOT NULL,
    filename varchar(255) NOT NULL,
    content_type text NOT NULL,
    source_url varchar(500) NOT NULL DEFAULT '',
    sha256 char(64) NOT NULL,
    original bytea NOT NULL,
    version integer NOT NULL DEFAULT 1,
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (company_key, sha256),
    UNIQUE (company_key, id)
);
CREATE TABLE IF NOT EXISTS knowledge_chunks (
    id bigserial PRIMARY KEY,
    company_key varchar(64) NOT NULL,
    document_id uuid NOT NULL,
    ordinal integer NOT NULL,
    content text NOT NULL,
    metadata jsonb NOT NULL,
    embedding vector(384) NOT NULL,
    embedding_model text NOT NULL,
    FOREIGN KEY (company_key, document_id) REFERENCES knowledge_documents(company_key, id) ON DELETE CASCADE,
    UNIQUE (document_id, ordinal)
);
CREATE INDEX IF NOT EXISTS knowledge_company_idx ON knowledge_documents(company_key);
CREATE INDEX IF NOT EXISTS chunks_company_idx ON knowledge_chunks(company_key, document_id);
CREATE INDEX IF NOT EXISTS chunks_embedding_idx ON knowledge_chunks USING hnsw(embedding vector_cosine_ops);
