using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog.Context;

namespace LayoutParserMcp.Tools;

/// <summary>
/// Tools genéricas de introspecção e acesso à API. Permitem descobrir e chamar
/// qualquer endpoint sem hardcodar rotas — robusto a uma API em evolução.
/// </summary>
[McpServerToolType]
public static class ApiTools
{
    /// <summary>
    /// Lista os endpoints da API lendo o documento OpenAPI (Swagger) em runtime.
    /// Funciona quando a API roda em Development (Swagger habilitado).
    /// </summary>
    [McpServerTool(Name = "list_endpoints")]
    [Description("Lista os endpoints da API a partir do Swagger/OpenAPI (método + rota + resumo). " +
                 "Lists the API endpoints from the live Swagger/OpenAPI document.")]
    public static async Task<string> ListEndpointsAsync(
        IHttpClientFactory httpClientFactory,
        ILogger<ApiToolsLog> logger,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.NewId();
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        var client = httpClientFactory.CreateClient("api");
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationContext.HeaderName, correlationId);
        try
        {
            logger.LogInformation("Tool list_endpoints: lendo Swagger de {BaseAddress}", client.BaseAddress);
            var json = await client.GetStringAsync("/swagger/v1/swagger.json", cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("paths", out var paths))
                return "Nenhum 'paths' encontrado no documento OpenAPI.";

            var sb = new StringBuilder();
            foreach (var path in paths.EnumerateObject())
            {
                foreach (var op in path.Value.EnumerateObject())
                {
                    var method = op.Name.ToUpperInvariant();
                    var summary = op.Value.TryGetProperty("summary", out var s) ? s.GetString() : "";
                    sb.AppendLine($"{method,-6} {path.Name}{(string.IsNullOrEmpty(summary) ? "" : $"  — {summary}")}");
                }
            }
            return sb.Length > 0 ? sb.ToString() : "Nenhum endpoint encontrado.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool list_endpoints falhou ao ler Swagger de {BaseAddress}", client.BaseAddress);
            return $"ERRO ao ler o Swagger em {client.BaseAddress}swagger/v1/swagger.json: {ex.Message} " +
                   "(a API está rodando em Development?)";
        }
    }

    /// <summary>
    /// Faz um GET genérico em um endpoint da API e retorna o corpo da resposta.
    /// </summary>
    [McpServerTool(Name = "api_get")]
    [Description("Faz um GET em um caminho da API (ex.: /api/MapperDatabase) e retorna o corpo. " +
                 "Performs a GET on an API path and returns the response body.")]
    public static async Task<string> ApiGetAsync(
        IHttpClientFactory httpClientFactory,
        ILogger<ApiToolsLog> logger,
        [Description("Caminho relativo do endpoint, ex.: /api/LayoutDatabase")] string path,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.NewId();
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        var client = httpClientFactory.CreateClient("api");
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationContext.HeaderName, correlationId);
        try
        {
            logger.LogInformation("Tool api_get: {Path}", path);
            var response = await client.GetAsync(NormalizePath(path), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Tool api_get: {Path} retornou HTTP {StatusCode}", path, (int)response.StatusCode);
            return response.IsSuccessStatusCode ? body : $"ERRO HTTP {(int)response.StatusCode}: {body}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool api_get: falha ao chamar {Path}", path);
            return $"ERRO ao chamar GET {path}: {ex.Message}";
        }
    }

    /// <summary>
    /// Faz um POST genérico (corpo JSON) em um endpoint da API.
    /// </summary>
    [McpServerTool(Name = "api_post")]
    [Description("Faz um POST com corpo JSON em um caminho da API e retorna o corpo da resposta. " +
                 "Performs a POST with a JSON body on an API path.")]
    public static async Task<string> ApiPostAsync(
        IHttpClientFactory httpClientFactory,
        ILogger<ApiToolsLog> logger,
        [Description("Caminho relativo do endpoint, ex.: /api/Transformation/generate")] string path,
        [Description("Corpo da requisição em JSON (string). Use '{}' se não houver corpo.")] string jsonBody,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.NewId();
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        var client = httpClientFactory.CreateClient("api");
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationContext.HeaderName, correlationId);
        try
        {
            logger.LogInformation("Tool api_post: {Path}", path);
            using var content = new StringContent(jsonBody ?? "{}", Encoding.UTF8, "application/json");
            var response = await client.PostAsync(NormalizePath(path), content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Tool api_post: {Path} retornou HTTP {StatusCode}", path, (int)response.StatusCode);
            return response.IsSuccessStatusCode ? body : $"ERRO HTTP {(int)response.StatusCode}: {body}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool api_post: falha ao chamar {Path}", path);
            return $"ERRO ao chamar POST {path}: {ex.Message}";
        }
    }

    private static string NormalizePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "/" : (path.StartsWith('/') ? path : "/" + path);
}
