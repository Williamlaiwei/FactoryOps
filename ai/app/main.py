"""Company-scoped catalog knowledge API; never creates products or work orders."""
from contextlib import asynccontextmanager
import hashlib
import json
import logging
import os
from pathlib import Path
import re
import secrets
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, Query, UploadFile
from fastapi.responses import Response, JSONResponse
import httpx
import psycopg
from psycopg.rows import dict_row
from psycopg.types.json import Jsonb
from pydantic import BaseModel, ConfigDict, Field

from . import embeddings
from .extraction import DocumentError, MAX_BYTES, extract, safe_url

log = logging.getLogger(__name__)


def connection():
    return psycopg.connect(host=os.environ["KNOWLEDGE_DB_HOST"], dbname=os.environ.get("KNOWLEDGE_DB_NAME", "knowledge"),
                          user=os.environ.get("KNOWLEDGE_DB_USER", "knowledge"), password=os.environ["KNOWLEDGE_DB_PASSWORD"],
                          connect_timeout=10, row_factory=dict_row)


@asynccontextmanager
async def lifespan(app):
    if len(os.environ.get("FACTORYOPS_AI_KEY", "")) < 32:
        raise RuntimeError("Configure a local service key using tools/configure_ai.py.")
    with connection() as db:
        db.execute(Path(__file__).with_name("schema.sql").read_text())
    yield


app = FastAPI(title="FactoryOps Company Knowledge", version="1.0.0", lifespan=lifespan, docs_url=None, redoc_url=None)


@app.exception_handler(psycopg.Error)
async def database_error(request, error):
    log.error("Knowledge database operation failed: %s", type(error).__name__)
    return JSONResponse(status_code=503, content={"detail": "The knowledge database is unavailable. No partial document changes were saved."})


def access(x_factoryops_key: Annotated[str, Header()] = "", x_factoryops_company: Annotated[str, Header()] = ""):
    expected = os.environ.get("FACTORYOPS_AI_KEY", "")
    if not expected or not secrets.compare_digest(x_factoryops_key.encode(), expected.encode()):
        raise HTTPException(401, "Service authentication required.")
    if not re.fullmatch(r"[a-z0-9-]{1,64}", x_factoryops_company):
        raise HTTPException(400, "Invalid company profile.")
    try:
        response = httpx.get(os.environ["CATALOG_API_URL"], headers={
            "X-FactoryOps-Key": expected, "X-FactoryOps-Company": x_factoryops_company}, timeout=15)
        if response.status_code == 404:
            raise HTTPException(404, "Company profile not found.")
        response.raise_for_status()
        snapshot = response.json()
        if snapshot.get("companyKey") != x_factoryops_company:
            raise ValueError("Catalog company mismatch")
        return snapshot
    except (httpx.HTTPError, ValueError, KeyError) as error:
        raise HTTPException(503, "The company's SQL catalog is unavailable. Retry when the application is ready.") from error


Scope = Annotated[dict, Depends(access)]


def document(db, key, document_id, lock=False):
    row = db.execute("SELECT * FROM knowledge_documents WHERE company_key=%s AND id=%s" + (" FOR UPDATE" if lock else ""),
                     (key, document_id)).fetchone()
    if row is None:
        raise HTTPException(404, "Document not found in the active company.")
    return row


def public(row):
    return {key: value for key, value in row.items() if key not in ("original", "company_key")}


def prepare(filename, raw, source_url):
    try:
        safe_url(source_url)
        chunks, metadata = extract(filename, raw)
    except DocumentError as error:
        raise HTTPException(422, str(error)) from error
    try:
        vectors = embeddings.passages([chunk.content for chunk in chunks])
    except Exception as error:
        log.error("Embedding generation failed: %s", type(error).__name__)
        raise HTTPException(503, "The local embedding model is unavailable. Existing documents were not changed.") from error
    metadata["embedding_model"] = embeddings.MODEL
    return chunks, vectors, metadata


def store_chunks(db, key, document_id, chunks, vectors):
    db.execute("DELETE FROM knowledge_chunks WHERE company_key=%s AND document_id=%s", (key, document_id))
    with db.cursor() as cursor:
        cursor.executemany("""INSERT INTO knowledge_chunks
            (company_key, document_id, ordinal, content, metadata, embedding, embedding_model)
            VALUES (%s,%s,%s,%s,%s,%s::vector,%s)""", [
            (key, document_id, ordinal, chunk.content, Jsonb(chunk.metadata), json.dumps(vector), embeddings.MODEL)
            for ordinal, (chunk, vector) in enumerate(zip(chunks, vectors))])


def filename_of(file):
    filename = (file.filename or "document").replace("\\", "/").rsplit("/", 1)[-1]
    filename = re.sub(r"[\x00-\x1f]", "", filename)[:255]
    if not filename:
        raise HTTPException(422, "A filename is required.")
    return filename


@app.get("/health")
def health():
    with connection() as db:
        db.execute("SELECT 1")
    return {"status": "ready", "embedding_model": embeddings.MODEL, "generation": "retrieval_and_catalog_candidates"}


@app.get("/v1/documents")
def list_documents(scope: Scope):
    with connection() as db:
        rows = db.execute("""SELECT id, filename, content_type, source_url, sha256, version, metadata, created_at, updated_at
            FROM knowledge_documents WHERE company_key=%s ORDER BY updated_at DESC""", (scope["companyKey"],)).fetchall()
    return {"documents": rows}


@app.post("/v1/documents")
def upload(scope: Scope, file: Annotated[UploadFile, File()], source_url: Annotated[str, Form()] = ""):
    raw = file.file.read(MAX_BYTES + 1)
    filename = filename_of(file)
    digest = hashlib.sha256(raw).hexdigest()
    key = scope["companyKey"]
    with connection() as db:
        existing = db.execute("SELECT * FROM knowledge_documents WHERE company_key=%s AND sha256=%s", (key, digest)).fetchone()
        if existing:
            return dict(public(existing), duplicate=True)
    chunks, vectors, metadata = prepare(filename, raw, source_url)
    document_id = uuid4()
    content_type = {"pdf": "application/pdf", "csv": "text/csv", "xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"}[metadata["format"]]
    with connection() as db:
        row = db.execute("""INSERT INTO knowledge_documents (id,company_key,filename,content_type,source_url,sha256,original,metadata)
            VALUES (%s,%s,%s,%s,%s,%s,%s,%s) ON CONFLICT (company_key,sha256) DO NOTHING RETURNING *""",
            (document_id, key, filename, content_type, source_url, digest, raw, Jsonb(metadata))).fetchone()
        if row is None:
            return dict(public(db.execute("SELECT * FROM knowledge_documents WHERE company_key=%s AND sha256=%s", (key, digest)).fetchone()), duplicate=True)
        store_chunks(db, key, document_id, chunks, vectors)
    return dict(public(row), duplicate=False)


@app.get("/v1/documents/{document_id}/original")
def original(document_id: UUID, scope: Scope):
    with connection() as db:
        row = document(db, scope["companyKey"], document_id)
    return Response(bytes(row["original"]), media_type=row["content_type"])


@app.put("/v1/documents/{document_id}")
def replace(document_id: UUID, scope: Scope, file: Annotated[UploadFile, File()],
            version: Annotated[int, Form(ge=1)], source_url: Annotated[str, Form()] = ""):
    key = scope["companyKey"]
    with connection() as db:
        current = document(db, key, document_id)
        if version != current["version"]:
            raise HTTPException(409, "Document changed. Reload before replacing it.")
    raw = file.file.read(MAX_BYTES + 1)
    filename = filename_of(file)
    chunks, vectors, metadata = prepare(filename, raw, source_url)
    digest = hashlib.sha256(raw).hexdigest()
    try:
        with connection() as db:
            current = document(db, key, document_id, lock=True)
            if version != current["version"]:
                raise HTTPException(409, "Document changed while uploading. Reload before trying again.")
            content_type = {"pdf": "application/pdf", "csv": "text/csv", "xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"}[metadata["format"]]
            row = db.execute("""UPDATE knowledge_documents SET filename=%s, content_type=%s, original=%s, sha256=%s,
                source_url=%s, metadata=%s, version=version+1, updated_at=now() WHERE company_key=%s AND id=%s RETURNING *""",
                (filename, content_type, raw, digest, source_url, Jsonb(metadata), key, document_id)).fetchone()
            store_chunks(db, key, document_id, chunks, vectors)
    except psycopg.errors.UniqueViolation as error:
        raise HTTPException(409, "This file already exists as another document in this company.") from error
    return public(row)


class VersionRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    version: int = Field(ge=1)


@app.post("/v1/documents/{document_id}/reindex")
def reindex(document_id: UUID, request: VersionRequest, scope: Scope):
    key = scope["companyKey"]
    with connection() as db:
        current = document(db, key, document_id)
    if current["version"] != request.version:
        raise HTTPException(409, "Document changed. Reload before re-indexing.")
    chunks, vectors, metadata = prepare(current["filename"], bytes(current["original"]), current["source_url"])
    with connection() as db:
        current = document(db, key, document_id, lock=True)
        if current["version"] != request.version:
            raise HTTPException(409, "Document changed while indexing. Reload before trying again.")
        store_chunks(db, key, document_id, chunks, vectors)
        row = db.execute("""UPDATE knowledge_documents SET metadata=%s, version=version+1, updated_at=now()
            WHERE company_key=%s AND id=%s RETURNING *""", (Jsonb(metadata), key, document_id)).fetchone()
    return public(row)


@app.delete("/v1/documents/{document_id}")
def delete(document_id: UUID, scope: Scope, version: Annotated[int, Query(ge=1)]):
    with connection() as db:
        current = document(db, scope["companyKey"], document_id, lock=True)
        if current["version"] != version:
            raise HTTPException(409, "Document changed. Reload before deleting it.")
        db.execute("DELETE FROM knowledge_documents WHERE company_key=%s AND id=%s", (scope["companyKey"], document_id))
    return {"deleted": True}


class RetrievalRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)
    query: str = Field(min_length=1, max_length=1000)
    top_k: int = Field(default=5, ge=1, le=10)


def catalog_candidates(query, products, citations):
    normalized = query.strip().casefold()
    exact = [product for product in products if product["sku"].casefold() == normalized]
    method = "exact_sku"
    if not exact:
        method = "exact_name_or_alias"
        exact = [product for product in products if normalized in
                 [product["name"].casefold()] + [alias.casefold() for alias in product["aliases"]]]
    if not exact:
        method = "candidate_only"
        tokens = set(re.findall(r"[a-z0-9]+(?:\.[0-9]+)?", normalized)) - {"the", "a", "for", "please", "need", "supply", "of", "mm"}
        numbers = {token for token in tokens if re.fullmatch(r"\d+(?:\.\d+)?", token)}
        ranked = []
        for product in products:
            text = " ".join([product["sku"], product["name"], *product["aliases"]]).casefold()
            p_tokens = set(re.findall(r"[a-z0-9]+(?:\.[0-9]+)?", text))
            overlap = len(tokens & p_tokens)
            if overlap and numbers.issubset(p_tokens) and overlap / max(len(tokens), 1) >= 0.5:
                ranked.append((overlap, product))
        exact = [product for _, product in sorted(ranked, key=lambda item: (-item[0], item[1]["sku"]))[:5]]
    return [dict(product, match_method=method, requires_review=True,
                 evidence_ids=[citation["citation_id"] for citation in citations if product["sku"].casefold() in citation["content"].casefold()])
            for product in exact]


@app.post("/v1/retrieve")
def retrieve(request: RetrievalRequest, scope: Scope):
    try:
        vector = embeddings.query(request.query)
    except Exception as error:
        raise HTTPException(503, "The local embedding model is unavailable. Please retry.") from error
    with connection() as db:
        hits = db.execute("""SELECT c.document_id, c.ordinal, c.content, c.metadata, d.filename, d.source_url,
            d.version, 1 - (c.embedding <=> %s::vector) AS score
            FROM knowledge_chunks c JOIN knowledge_documents d ON d.id=c.document_id AND d.company_key=c.company_key
            WHERE c.company_key=%s AND c.embedding_model=%s ORDER BY c.embedding <=> %s::vector LIMIT %s""",
            (json.dumps(vector), scope["companyKey"], embeddings.MODEL, json.dumps(vector), request.top_k)).fetchall()
    citations = [dict(hit, document_id=str(hit["document_id"]), citation_id=f'{hit["document_id"]}:{hit["ordinal"]}',
                      source_url=hit["metadata"].get("source_url", hit["source_url"])) for hit in hits if hit["score"] >= 0.25]
    candidates = catalog_candidates(request.query, scope["products"], citations)
    # For an exact catalog match, prefer passages explicitly carrying its SKU
    # when available, so a nearby gauge or a different product is not presented
    # as supporting evidence for that match.
    evidence_ids = {identifier for candidate in candidates if candidate["match_method"] != "candidate_only"
                    for identifier in candidate["evidence_ids"]}
    if evidence_ids:
        citations = [citation for citation in citations if citation["citation_id"] in evidence_ids]
    return {"company_key": scope["companyKey"], "query": request.query, "citations": citations,
            "candidates": candidates, "requires_review": True, "embedding_model": embeddings.MODEL,
            "notice": "Reference evidence and SQL catalog candidates only. Document text cannot create products or orders. Demo codes, prices and stock must not be treated as confirmed supplier data."}
