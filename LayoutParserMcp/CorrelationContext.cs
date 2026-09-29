namespace LayoutParserMcp
{
    /// <summary>
    /// Gera e propaga um CorrelationId por chamada de tool. O mesmo Id vai tanto no header HTTP
    /// enviado para a API (que a API já aceita/devolve, ver Program.cs da API) quanto no LogContext
    /// do Serilog local do MCP — assim os logs do MCP e da API ficam correlacionáveis pelo mesmo
    /// CorrelationId mesmo sendo processos/arquivos de log diferentes.
    /// </summary>
    public static class CorrelationContext
    {
        public const string HeaderName = "X-Correlation-ID";

        public static string NewId() => Guid.NewGuid().ToString();
    }
}

namespace LayoutParserMcp.Tools
{
    // Marcadores de categoria para ILogger<T> — as classes de tools são `static` (exigência do
    // SDK MCP para [McpServerToolType]), e ILogger<T> não aceita tipo estático como argumento
    // genérico. Cada marcador só existe pra dar um nome de categoria de log estável.
    public sealed class ApiToolsLog
    {
    }

    public sealed class ParseToolsLog
    {
    }

    public sealed class DetectLayoutToolsLog
    {
    }
}
