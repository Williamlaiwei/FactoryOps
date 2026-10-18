using System.Net;
using System.Text.Json;
using FactoryOps.Web.Models;

namespace FactoryOps.Web.Services;

public class KnowledgeException(string message, HttpStatusCode status = HttpStatusCode.ServiceUnavailable) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

public class KnowledgeClient(HttpClient client, CompanyContext company, IConfiguration configuration)
{
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-FactoryOps-Key", configuration["KnowledgeService:ApiKey"] ?? "");
        request.Headers.Add("X-FactoryOps-Company", company.Key);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new KnowledgeException("The knowledge service is unavailable or took too long. Please retry; existing orders and products are unaffected."); }
        if (response.IsSuccessStatusCode) return response;
        var message = "The knowledge request could not be completed. Reload the page and try again.";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (error.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                message = detail.GetString() ?? message;
        }
        catch (JsonException) { }
        var status = response.StatusCode;
        response.Dispose();
        throw new KnowledgeException(message, status);
    }

    public async Task<T> Json<T>(HttpMethod method, string path, HttpContent? content = null)
    {
        using var response = await Send(method, path, content);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new KnowledgeException("The knowledge service returned an empty response.");
    }

    public async Task<KnowledgeDocument> Upload(IFormFile file, string? sourceUrl, Guid? id = null, int? version = null)
    {
        using var form = new MultipartFormDataContent();
        using var stream = file.OpenReadStream();
        form.Add(new StreamContent(stream), "file", Path.GetFileName(file.FileName));
        form.Add(new StringContent(sourceUrl ?? ""), "source_url");
        if (version.HasValue) form.Add(new StringContent(version.Value.ToString()), "version");
        return await Json<KnowledgeDocument>(id.HasValue ? HttpMethod.Put : HttpMethod.Post,
            id.HasValue ? $"v1/documents/{id.Value}" : "v1/documents", form);
    }
}
