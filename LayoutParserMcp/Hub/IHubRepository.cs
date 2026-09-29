namespace LayoutParserMcp.Hub;

/// <summary>Persistência do Hub. Implementação real: SQL (IdentityDatabase); testes usam fake em memória.</summary>
public interface IHubRepository
{
    /// <summary>Chat ativo (não revogado) dono do hash, ou null.</summary>
    Task<ChatInfo?> FindActiveChatByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    Task TouchAsync(int chatId, CancellationToken ct);

    Task<IReadOnlyList<ChatInfo>> ListChatsAsync(CancellationToken ct);

    Task<ChatInfo?> FindChatByNameAsync(string name, CancellationToken ct);

    Task InsertMessageAsync(HubMessage message, CancellationToken ct);

    /// <summary>Mensagens visíveis ao chat: dirigidas a ele ou em canal (exceto as que ele mesmo enviou).</summary>
    Task<IReadOnlyList<HubMessage>> QueryInboxAsync(InboxQuery query, CancellationToken ct);

    /// <summary>Marca como lida; idempotente. Devolve false se a mensagem não é visível ao chat.</summary>
    Task<bool> AckAsync(Guid messageId, int chatId, CancellationToken ct);

    Task AuditAsync(int? chatId, string tool, string outcome, CancellationToken ct);
}
