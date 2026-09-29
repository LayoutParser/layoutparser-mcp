using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace LayoutParserMcp.Hub;

/// <summary>
/// Repositório SQL (ADO.NET cru) do esquema [mcp]. A connection string vem de
/// MCP_HUB_SQL_CONNECTION (IdentityDatabase) — nunca hardcoded. Sem DDL aqui: o esquema é criado
/// por sql/001-init.sql. Toda query é parametrizada.
/// </summary>
public sealed class SqlHubRepository(string connectionString) : IHubRepository
{
    private const string MessageSelect = @"
SELECT m.Id, m.ThreadId, m.FromChatId, f.Name AS FromName, m.ToChatId, t.Name AS ToName,
       m.Channel, m.Kind, m.Subject, m.Body, m.RefsJson, m.CreatedAt
FROM mcp.Message m
JOIN mcp.Chat f ON f.Id = m.FromChatId
LEFT JOIN mcp.Chat t ON t.Id = m.ToChatId";

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<ChatInfo?> FindActiveChatByTokenHashAsync(byte[] tokenHash, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, Name, Scope, CreatedAt, RevokedAt, LastSeenAt FROM mcp.Chat WHERE TokenHash = @h AND RevokedAt IS NULL", conn);
        cmd.Parameters.Add("@h", SqlDbType.VarBinary, 32).Value = tokenHash;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadChat(r) : null;
    }

    public async Task TouchAsync(int chatId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("UPDATE mcp.Chat SET LastSeenAt = SYSUTCDATETIME() WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", chatId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatInfo>> ListChatsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, Name, Scope, CreatedAt, RevokedAt, LastSeenAt FROM mcp.Chat ORDER BY Name", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ChatInfo>();
        while (await r.ReadAsync(ct)) lista.Add(ReadChat(r));
        return lista;
    }

    public async Task<ChatInfo?> FindChatByNameAsync(string name, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, Name, Scope, CreatedAt, RevokedAt, LastSeenAt FROM mcp.Chat WHERE Name = @n", conn);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = name;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadChat(r) : null;
    }

    public async Task InsertMessageAsync(HubMessage m, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
INSERT INTO mcp.Message (Id, ThreadId, FromChatId, ToChatId, Channel, Kind, Subject, Body, RefsJson, CreatedAt)
VALUES (@id, @thread, @from, @to, @channel, @kind, @subject, @body, @refs, @at)", conn);
        cmd.Parameters.AddWithValue("@id", m.Id);
        cmd.Parameters.AddWithValue("@thread", m.ThreadId);
        cmd.Parameters.AddWithValue("@from", m.FromChatId);
        cmd.Parameters.AddWithValue("@to", (object?)m.ToChatId ?? DBNull.Value);
        cmd.Parameters.Add("@channel", SqlDbType.NVarChar, 40).Value = (object?)m.Channel ?? DBNull.Value;
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = m.Kind;
        cmd.Parameters.Add("@subject", SqlDbType.NVarChar, 200).Value = m.Subject;
        cmd.Parameters.Add("@body", SqlDbType.NVarChar, -1).Value = m.Body;
        cmd.Parameters.Add("@refs", SqlDbType.NVarChar, -1).Value = m.Refs.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(m.Refs);
        cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = m.CreatedAt;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<HubMessage>> QueryInboxAsync(InboxQuery q, CancellationToken ct)
    {
        var sql = MessageSelect + @"
WHERE (m.ToChatId = @chat OR m.Channel IS NOT NULL) AND m.FromChatId <> @chat";
        if (q.UnreadOnly) sql += " AND NOT EXISTS (SELECT 1 FROM mcp.MessageAck a WHERE a.MessageId = m.Id AND a.ChatId = @chat)";
        if (q.Since is not null) sql += " AND m.CreatedAt > @since";
        if (q.ThreadId is not null) sql += " AND m.ThreadId = @thread";
        sql += " ORDER BY m.CreatedAt ASC OFFSET 0 ROWS FETCH NEXT @lim ROWS ONLY";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@chat", q.ChatId);
        cmd.Parameters.AddWithValue("@lim", q.Limit);
        if (q.Since is not null) cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = q.Since.Value;
        if (q.ThreadId is not null) cmd.Parameters.AddWithValue("@thread", q.ThreadId.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<HubMessage>();
        while (await r.ReadAsync(ct))
        {
            var refsJson = r.IsDBNull(10) ? null : r.GetString(10);
            lista.Add(new HubMessage(r.GetGuid(0), r.GetGuid(1), r.GetInt32(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
                refsJson is null ? [] : JsonSerializer.Deserialize<string[]>(refsJson) ?? [], r.GetDateTime(11)));
        }
        return lista;
    }

    public async Task<bool> AckAsync(Guid messageId, int chatId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Só confirma mensagem visível ao chat (dirigida a ele ou de canal, de outro remetente); idempotente.
        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM mcp.Message WHERE Id = @m AND (ToChatId = @c OR Channel IS NOT NULL) AND FromChatId <> @c)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM mcp.MessageAck WHERE MessageId = @m AND ChatId = @c)
        INSERT INTO mcp.MessageAck (MessageId, ChatId) VALUES (@m, @c);
    SELECT 1;
END
ELSE SELECT 0;", conn);
        cmd.Parameters.AddWithValue("@m", messageId);
        cmd.Parameters.AddWithValue("@c", chatId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task AuditAsync(int? chatId, string tool, string outcome, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT INTO mcp.Audit (ChatId, Tool, Outcome) VALUES (@c, @t, @o)", conn);
        cmd.Parameters.AddWithValue("@c", (object?)chatId ?? DBNull.Value);
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 64).Value = tool;
        cmd.Parameters.Add("@o", SqlDbType.NVarChar, 200).Value = outcome.Length > 200 ? outcome[..200] : outcome;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChatInfo ReadChat(SqlDataReader r) => new(r.GetInt32(0), r.GetString(1),
        r.IsDBNull(2) ? null : r.GetString(2), r.GetDateTime(3),
        r.IsDBNull(4) ? null : r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetDateTime(5));
}
