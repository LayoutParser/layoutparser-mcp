using LayoutParserMcp.Hub;

namespace LayoutParserMcp.Tests;

/// <summary>Fake em memória do repositório do Hub (espelha as regras de visibilidade do SQL).</summary>
public sealed class InMemoryHubRepository : IHubRepository
{
    private readonly List<(ChatInfo Chat, byte[] Hash)> _chats = [];
    private readonly List<HubMessage> _messages = [];
    private readonly HashSet<(Guid, int)> _acks = [];
    public List<(int? ChatId, string Tool, string Outcome)> Audits { get; } = [];
    public HashSet<int> Touched { get; } = [];

    public ChatInfo AddChat(string name, byte[] hash, bool revoked = false)
    {
        var c = new ChatInfo(_chats.Count + 1, name, "repo:" + name, DateTime.UtcNow, revoked ? DateTime.UtcNow : null, null);
        _chats.Add((c, hash));
        return c;
    }

    public IReadOnlyList<HubMessage> Messages => _messages;

    public Task<ChatInfo?> FindActiveChatByTokenHashAsync(byte[] h, CancellationToken ct) =>
        Task.FromResult(_chats.Where(x => x.Hash.SequenceEqual(h) && x.Chat.RevokedAt is null).Select(x => x.Chat).FirstOrDefault());

    public Task TouchAsync(int chatId, CancellationToken ct) { Touched.Add(chatId); return Task.CompletedTask; }

    public Task<IReadOnlyList<ChatInfo>> ListChatsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ChatInfo>>(_chats.Select(x => x.Chat).ToList());

    public Task<ChatInfo?> FindChatByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(_chats.Select(x => x.Chat).FirstOrDefault(c => c.Name == name));

    public Task InsertMessageAsync(HubMessage m, CancellationToken ct) { _messages.Add(m); return Task.CompletedTask; }

    public Task<IReadOnlyList<HubMessage>> QueryInboxAsync(InboxQuery q, CancellationToken ct)
    {
        var r = _messages.Where(m => (m.ToChatId == q.ChatId || m.Channel is not null) && m.FromChatId != q.ChatId);
        if (q.UnreadOnly) r = r.Where(m => !_acks.Contains((m.Id, q.ChatId)));
        if (q.Since is not null) r = r.Where(m => m.CreatedAt > q.Since);
        if (q.ThreadId is not null) r = r.Where(m => m.ThreadId == q.ThreadId);
        return Task.FromResult<IReadOnlyList<HubMessage>>(r.OrderBy(m => m.CreatedAt).Take(q.Limit).ToList());
    }

    public Task<bool> AckAsync(Guid id, int chatId, CancellationToken ct)
    {
        var visivel = _messages.Any(m => m.Id == id && (m.ToChatId == chatId || m.Channel is not null) && m.FromChatId != chatId);
        if (visivel) _acks.Add((id, chatId));
        return Task.FromResult(visivel);
    }

    public Task AuditAsync(int? chatId, string tool, string outcome, CancellationToken ct)
    {
        Audits.Add((chatId, tool, outcome));
        return Task.CompletedTask;
    }
}
