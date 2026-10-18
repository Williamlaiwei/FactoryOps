using System.Text.Json.Serialization;

namespace FactoryOps.Web.Models;

public class KnowledgePage
{
    public string Query { get; set; } = "";
    public string? Error { get; set; }
    public List<KnowledgeDocument> Documents { get; set; } = new();
    public KnowledgeSearchResult? Result { get; set; }
}

public class KnowledgeDocumentList { public List<KnowledgeDocument> Documents { get; set; } = new(); }

public class KnowledgeDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public int Version { get; set; }
    [JsonPropertyName("source_url")] public string SourceUrl { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
    public KnowledgeMetadata Metadata { get; set; } = new();
    public bool Duplicate { get; set; }
}

public class KnowledgeMetadata
{
    public string Format { get; set; } = "";
    public int Chunks { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class KnowledgeSearchResult
{
    [JsonPropertyName("company_key")] public string CompanyKey { get; set; } = "";
    [JsonPropertyName("requires_review")] public bool RequiresReview { get; set; } = true;
    [JsonPropertyName("embedding_model")] public string EmbeddingModel { get; set; } = "";
    public string Query { get; set; } = "";
    public string Notice { get; set; } = "";
    public List<KnowledgeCitation> Citations { get; set; } = new();
    public List<KnowledgeCandidate> Candidates { get; set; } = new();
}

public class KnowledgeCitation
{
    [JsonPropertyName("document_id")] public Guid DocumentId { get; set; }
    [JsonPropertyName("citation_id")] public string CitationId { get; set; } = "";
    public string FileName { get; set; } = "";
    [JsonPropertyName("source_url")] public string SourceUrl { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
    public int Version { get; set; }
    public int Ordinal { get; set; }
    public CitationMetadata Metadata { get; set; } = new();
}

public class CitationMetadata
{
    public int? Page { get; set; }
    public string? Sheet { get; set; }
    [JsonPropertyName("row_start")] public int? RowStart { get; set; }
    [JsonPropertyName("row_end")] public int? RowEnd { get; set; }
    public string Kind { get; set; } = "";
}

public class KnowledgeCandidate
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string PackingSize { get; set; } = "";
    public string RoughIn { get; set; } = "";
    public string HeatingMethod { get; set; } = "";
    public string FlushingMethod { get; set; } = "";
    public string CeramicBody { get; set; } = "";
    public string Color { get; set; } = "";
    public string DisplayMode { get; set; } = "";
    public string NetWeight { get; set; } = "";
    public string GrossWeight { get; set; } = "";
    public bool IsDemo { get; set; }
    public bool SkuIsVerified { get; set; }
    public string SourceUrl { get; set; } = "";
    [JsonPropertyName("match_method")] public string MatchMethod { get; set; } = "";
    [JsonPropertyName("requires_review")] public bool RequiresReview { get; set; }
    [JsonPropertyName("evidence_ids")] public List<string> EvidenceIds { get; set; } = new();
}
