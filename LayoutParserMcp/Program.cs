using System.Security.Cryptography;
using LayoutParserMcp.Hub;
using LayoutParserMcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

// =============================================================================
// LayoutParser MCP Server (stdio | http)
//
// Servidor MCP que expõe operações do LayoutParser API como *tools* para agentes.
// É um CLIENTE FINO sobre a API HTTP — a API continua sendo a fonte da verdade.
//
// IMPORTANTE (protocolo stdio): a comunicação MCP usa STDOUT. Todo log DEVE ir
// para STDERR, senão corrompe o protocolo. Por isso o sink de console do Serilog
// abaixo é forçado para stderr (standardErrorFromLevel: LogEventLevel.Verbose).
//
// Configuração via env var:
//   LAYOUTPARSER_API_URL   base da API (default http://localhost:5000)
//   LAYOUTPARSER_LOG_DIR   diretório do arquivo de log (default "Logs" relativo ao cwd do MCP)
//   LAYOUTPARSER_MCP_TRANSPORT  stdio (default) | http  (ou argumento --transport=http)
//   Modo http (Hub): LAYOUTPARSER_MCP_URL (default http://0.0.0.0:5210), MCP_HUB_SQL_CONNECTION,
//     MCP_HUB_TOKEN_PEPPER, MCP_HUB_RATE_LIMIT_PER_MINUTE (default 60). Ver README.
//   Subcomando: `hash-token` (lê token do stdin, imprime o hash para cadastro em mcp.Chat).
// =============================================================================

// ✅ Subcomando utilitário: gera o hash de um token (stdin) para cadastro em mcp.Chat. Nunca ecoa o token.
if (args.Length > 0 && args[0] == "hash-token")
{
    var pepperCli = Environment.GetEnvironmentVariable("MCP_HUB_TOKEN_PEPPER");
    if (string.IsNullOrWhiteSpace(pepperCli))
    {
        Console.Error.WriteLine("Defina MCP_HUB_TOKEN_PEPPER (mínimo 16 caracteres).");
        return 1;
    }
    var tokenCli = (Console.In.ReadLine() ?? string.Empty).Trim();
    if (tokenCli.Length == 0)
    {
        Console.Error.WriteLine("Informe o token no stdin.");
        return 1;
    }
    Console.WriteLine(new TokenHasher(pepperCli).HashToSqlLiteral(tokenCli));
    return 0;
}

var transport = (args.FirstOrDefault(a => a.StartsWith("--transport=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
                 ?? Environment.GetEnvironmentVariable("LAYOUTPARSER_MCP_TRANSPORT")
                 ?? "stdio").Trim().ToLowerInvariant();
var isHttp = transport == "http";

// stdio: host genérico (sem Kestrel). http: WebApplication. Ambos são IHostApplicationBuilder.
WebApplicationBuilder? webBuilder = isHttp ? WebApplication.CreateBuilder(args) : null;
IHostApplicationBuilder builder = isHttp ? webBuilder! : Host.CreateApplicationBuilder(args);

// ✅ Mesmo padrão de logging da API (ver Program.cs da API): Serilog com outputTemplate
// idêntico e Source fixo ("MCP", análogo a "Backend"/"Frontend"), pra que os 3 arquivos
// fiquem correlacionáveis pelo mesmo formato de linha (UnifiedLogReaderService já parseia
// esse formato via ApiLinePattern).
var logDirectory = Environment.GetEnvironmentVariable("LAYOUTPARSER_LOG_DIR")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs");

try
{
    if (!Directory.Exists(logDirectory))
        Directory.CreateDirectory(logDirectory);
}
catch (Exception ex)
{
    // Não pode derrubar o MCP por falha ao criar o diretório de log — cai pro cwd atual
    // (Serilog.Sinks.File também cria o diretório sozinho, isso aqui é só best-effort de log).
    Console.Error.WriteLine($"[BOOTSTRAP] WARNING: falha ao criar diretório de log '{logDirectory}': {ex.Message}");
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Source", "MCP")
    // stdout é o canal do protocolo MCP — forçar TODO log de console pra stderr, não só acima
    // de um nível mínimo.
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [Corr:{CorrelationId}] [Src:{Source}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: LogEventLevel.Verbose)
    .WriteTo.File(
        Path.Combine(logDirectory, "layoutparsermcp.log"),
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [Corr:{CorrelationId}] [Src:{Source}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// HttpClient nomeado apontando para a API.
var apiBaseUrl = Environment.GetEnvironmentVariable("LAYOUTPARSER_API_URL") ?? "http://localhost:5000";
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
});

// Tools de proxy da API (valem nos dois transportes). Lista explícita em vez de
// WithToolsFromAssembly: as tools do Hub só entram no modo http (dependem de identidade por token).
Type[] proxyTools = [typeof(ApiTools), typeof(ParseTools), typeof(DetectLayoutTools)];

WebApplication? app = null;
IHost host;

if (!isHttp)
{
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools((IEnumerable<Type>)proxyTools, serializerOptions: null);
    host = ((HostApplicationBuilder)builder).Build();
}
else
{
    // ✅ Configuração do Hub só por env — nada hardcoded (nunca 172.31.249.51/Database:*).
    var sqlConnection = Environment.GetEnvironmentVariable("MCP_HUB_SQL_CONNECTION");
    var pepper = Environment.GetEnvironmentVariable("MCP_HUB_TOKEN_PEPPER");
    if (string.IsNullOrWhiteSpace(sqlConnection) || string.IsNullOrWhiteSpace(pepper) || pepper.Length < 16)
    {
        Log.Fatal("Modo http exige MCP_HUB_SQL_CONNECTION e MCP_HUB_TOKEN_PEPPER (>= 16 caracteres) nas variáveis de ambiente.");
        Log.CloseAndFlush();
        return 1;
    }
    var rateLimit = int.TryParse(Environment.GetEnvironmentVariable("MCP_HUB_RATE_LIMIT_PER_MINUTE"), out var rl) && rl > 0 ? rl : 60;

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<IHubRepository>(_ => new SqlHubRepository(sqlConnection));
    builder.Services.AddSingleton(new TokenHasher(pepper));
    builder.Services.AddSingleton(new RateLimiter(rateLimit, TimeSpan.FromMinutes(1)));
    builder.Services.AddSingleton<HubService>();
    builder.Services.AddSingleton<ICallerAccessor, HttpCallerAccessor>();

    // Stateless: cada requisição é autenticada por token; sem afinidade de sessão.
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(o => o.Stateless = true)
        .WithTools((IEnumerable<Type>)[.. proxyTools, typeof(HubTools)], serializerOptions: null);

    var url = Environment.GetEnvironmentVariable("LAYOUTPARSER_MCP_URL") ?? "http://0.0.0.0:5210";
    webBuilder!.WebHost.UseUrls(url);
    app = webBuilder.Build();
    app.UseMiddleware<TokenAuthMiddleware>();
    app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    app.MapMcp("/mcp");
    host = app;
}

Log.Information("LayoutParser MCP Server iniciando ({Transport}). API base: {ApiBaseUrl}. Log directory: {LogDirectory}", transport, apiBaseUrl, logDirectory);

try
{
    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}

return 0;
