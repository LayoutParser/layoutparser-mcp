using System.Collections.Concurrent;

namespace LayoutParserMcp.Hub;

/// <summary>
/// Rate limit simples por chat (janela deslizante em memória, por instância). Suficiente para um
/// Hub de container único; se escalar para várias instâncias, migrar para contador compartilhado.
/// </summary>
public sealed class RateLimiter(int maxRequests, TimeSpan window, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<int, Queue<DateTimeOffset>> _hits = new();

    /// <summary>Registra uma requisição; <c>false</c> se o chat estourou o limite na janela.</summary>
    public bool TryAcquire(int chatId)
    {
        var now = _clock.GetUtcNow();
        var fila = _hits.GetOrAdd(chatId, _ => new Queue<DateTimeOffset>());
        lock (fila)
        {
            while (fila.Count > 0 && now - fila.Peek() >= window) fila.Dequeue();
            if (fila.Count >= maxRequests) return false;
            fila.Enqueue(now);
            return true;
        }
    }
}
