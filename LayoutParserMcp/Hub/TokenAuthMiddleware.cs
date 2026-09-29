using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LayoutParserMcp.Hub;

/// <summary>Expõe o chat autenticado da requisição HTTP corrente às tools.</summary>
public interface ICallerAccessor
{
    /// <summary>Chat autenticado; lança <see cref="HubException"/> se não houver (ex.: modo stdio).</summary>
    ChatInfo GetCaller();
}

public sealed class HttpCallerAccessor(IHttpContextAccessor http) : ICallerAccessor
{
    public const string ItemKey = "mcp.hub.chat";

    public ChatInfo GetCaller() =>
        http.HttpContext?.Items[ItemKey] as ChatInfo
        ?? throw new HubException("Chat não autenticado.");
}

/// <summary>
/// Autenticação por token (Authorization: Bearer). Resolve o chat pelo HASH do token, nega 401 sem
/// token válido/revogado, aplica rate limit por chat (429) e atualiza LastSeenAt. O token nunca é
/// logado. Falha de SQL vira 503 (degrada sem derrubar o processo).
/// </summary>
public sealed class TokenAuthMiddleware(RequestDelegate next, ILogger<TokenAuthMiddleware> logger)
{
    private const int MaxTokenLength = 256;

    public async Task InvokeAsync(HttpContext ctx, IHubRepository repo, TokenHasher hasher, RateLimiter limiter)
    {
        // Sonda de saúde do container não exige token (não expõe dado).
        if (ctx.Request.Path.StartsWithSegments("/healthz"))
        {
            await next(ctx);
            return;
        }

        var token = ExtractBearer(ctx.Request.Headers.Authorization.ToString());
        if (token is null)
        {
            await Unauthorized(ctx);
            return;
        }

        ChatInfo? chat;
        try
        {
            chat = await repo.FindActiveChatByTokenHashAsync(hasher.Hash(token), ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub: falha ao consultar o banco na autenticação");
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (chat is null)
        {
            logger.LogWarning("Hub: token inválido ou revogado ({Remote})", ctx.Connection.RemoteIpAddress);
            await Unauthorized(ctx);
            return;
        }

        if (!limiter.TryAcquire(chat.Id))
        {
            logger.LogWarning("Hub: rate limit excedido para o chat {Chat}", chat.Name);
            ctx.Response.Headers.RetryAfter = "60";
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        try { await repo.TouchAsync(chat.Id, ctx.RequestAborted); }
        catch (Exception ex) { logger.LogWarning(ex, "Hub: falha ao atualizar LastSeenAt do chat {Chat}", chat.Name); }

        ctx.Items[HttpCallerAccessor.ItemKey] = chat;
        await next(ctx);
    }

    /// <summary>Extrai o token de "Bearer xxx"; null se ausente/mal formado/grande demais.</summary>
    public static string? ExtractBearer(string? header)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var token = header[prefix.Length..].Trim();
        return token.Length is > 0 and <= MaxTokenLength ? token : null;
    }

    private static Task Unauthorized(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
