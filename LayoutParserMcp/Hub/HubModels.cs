namespace LayoutParserMcp.Hub;

/// <summary>Chat/repositório registrado no Hub (linha de mcp.Chat, sem o hash).</summary>
public sealed record ChatInfo(int Id, string Name, string? Scope, DateTime CreatedAt, DateTime? RevokedAt, DateTime? LastSeenAt);

/// <summary>Mensagem persistida. Destino é chat (<see cref="ToChatId"/>) OU canal (<see cref="Channel"/>).</summary>
public sealed record HubMessage(
    Guid Id,
    Guid ThreadId,
    int FromChatId,
    string FromChatName,
    int? ToChatId,
    string? ToChatName,
    string? Channel,
    string Kind,
    string Subject,
    string Body,
    IReadOnlyList<string> Refs,
    DateTime CreatedAt);

/// <summary>Filtro de leitura da caixa de entrada.</summary>
public sealed record InboxQuery(int ChatId, bool UnreadOnly, DateTime? Since, Guid? ThreadId, int Limit);

/// <summary>Erro de validação/regra do Hub — a mensagem é segura para devolver ao chamador.</summary>
public sealed class HubException(string message) : Exception(message);
