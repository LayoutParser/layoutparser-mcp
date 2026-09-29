using System.ComponentModel;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog.Context;

namespace LayoutParserMcp.Tools;

/// <summary>
/// Tool tipada para <c>POST /api/parse/auto</c> (issue #216) — detecção automática de layout
/// para documentos MQSeries/IDoc. Cliente fino: não reinterpreta o resultado nem inventa
/// formato — devolve o JSON de <c>AutomaticParseResponse</c> tal como a API o produz, com os
/// 3 status possíveis (<c>unique</c> / <c>ambiguous</c> / <c>not_found</c>), até 5 candidatos
/// ranqueados (teto da própria API — <c>AutomaticLayoutDetectionService.MaximumRankedCandidates</c>),
/// evidências/conflitos/limitações por candidato e o indicador de truncamento/empate.
/// </summary>
[McpServerToolType]
public static class DetectLayoutTools
{
    /// <summary>
    /// Detecta o layout de um documento MQSeries/IDoc usando o catálogo interno da API
    /// (<c>POST /api/parse/auto</c>). Sem <paramref name="layoutGuidOverride"/>, a API decide:
    /// aplica o layout automaticamente só quando <c>status=unique</c>. Com override explícito,
    /// o candidato escolhido precisa pertencer ao ranking desta MESMA detecção — a API rejeita
    /// (422) GUID de fora do conjunto retornado, e esta tool não tenta contornar isso nem repete
    /// a chamada trocando de layout sozinha.
    /// </summary>
    [McpServerTool(Name = "detect_layout")]
    [Description("Detecta automaticamente o layout de um documento MQSeries/IDoc (POST /api/parse/auto). " +
                 "Retorna o resultado fiel da API: status unique/ambiguous/not_found, até 5 candidatos " +
                 "ranqueados com score/evidências/conflitos/limitações, e o parse já aplicado quando " +
                 "houver seleção (automática ou por override explícito de layoutGuid). " +
                 "Detects the layout of an MQSeries/IDoc document automatically. Returns the API's " +
                 "faithful result: unique/ambiguous/not_found status, up to 5 ranked candidates with " +
                 "score/evidence/conflicts/limitations, and the applied parse when a layout is selected " +
                 "(automatic or via explicit layoutGuid override).")]
    public static async Task<string> DetectLayoutAsync(
        IHttpClientFactory httpClientFactory,
        ILogger<DetectLayoutToolsLog> logger,
        [Description("Caminho local do documento MQSeries/IDoc a analisar.")] string documentPath,
        [Description("GUID de layout (opcional) para forçar a seleção entre os candidatos ranqueados " +
                     "desta mesma detecção — não escolhe por conta própria; a API rejeita (422) GUID " +
                     "fora do ranking atual. Omita para deixar a API decidir (só aplica automaticamente " +
                     "quando o status for 'unique').")]
        string? layoutGuidOverride = null,
        CancellationToken cancellationToken = default)
    {
        // ✅ CorrelationId próprio da chamada — vai no header pra API e no LogContext local, pra
        // que os logs do MCP e da API fiquem correlacionáveis mesmo em arquivos/processos
        // diferentes (mesmo padrão de ParseTools.ParseDocumentAsync).
        var correlationId = CorrelationContext.NewId();
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        if (!File.Exists(documentPath))
        {
            logger.LogWarning("Tool detect_layout: documento não encontrado em {DocumentPath}", documentPath);
            return $"ERRO: documento não encontrado: {documentPath}";
        }

        var client = httpClientFactory.CreateClient("api");
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationContext.HeaderName, correlationId);

        using var form = new MultipartFormDataContent();

        // ⚠️ Nenhum log abaixo referencia conteúdo do documento — só nome de arquivo local e
        // metadados de detecção (status/rank/guid), nunca o texto/documento decifrado.
        var docBytes = await File.ReadAllBytesAsync(documentPath, cancellationToken);
        var docContent = new ByteArrayContent(docBytes);
        form.Add(docContent, "documentFile", Path.GetFileName(documentPath));

        if (!string.IsNullOrWhiteSpace(layoutGuidOverride))
            form.Add(new StringContent(layoutGuidOverride), "layoutGuidOverride");

        try
        {
            logger.LogInformation(
                "Tool detect_layout: {DocumentPath} (layoutGuidOverride={LayoutGuidOverride})",
                documentPath, layoutGuidOverride ?? "(nenhum)");

            var response = await client.PostAsync("/api/parse/auto", form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // A API ecoa/gera o correlationId no header de resposta (EnsureCorrelationId) — pode
            // divergir do enviado só se o middleware tiver decidido outro por algum motivo; expõe
            // os dois para quem for cruzar com o log da API.
            var responseCorrelationId = response.Headers.TryGetValues(CorrelationContext.HeaderName, out var values)
                ? values.FirstOrDefault()
                : null;

            if (!response.IsSuccessStatusCode)
            {
                // ✅ Erros/limites HTTP do endpoint (400, 422, 503, 500) são preservados como vieram —
                // não engolidos, não convertidos em sucesso, e sem retry automático que trocasse de
                // layout por conta própria (422 de override inválido tem que voltar ao chamador).
                logger.LogWarning(
                    "Tool detect_layout: HTTP {StatusCode} para {DocumentPath} (CorrelationId={CorrelationId})",
                    (int)response.StatusCode, documentPath, responseCorrelationId ?? correlationId);
                return $"ERRO HTTP {(int)response.StatusCode} (CorrelationId={responseCorrelationId ?? correlationId}): {body}";
            }

            logger.LogInformation(
                "Tool detect_layout concluída para {DocumentPath} (CorrelationId={CorrelationId})",
                documentPath, responseCorrelationId ?? correlationId);

            return body;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool detect_layout: falha ao chamar a API ({BaseAddress})", client.BaseAddress);
            return $"ERRO ao chamar a API ({client.BaseAddress}) (CorrelationId={correlationId}): {ex.Message}";
        }
    }
}
