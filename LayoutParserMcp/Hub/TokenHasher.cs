using System.Security.Cryptography;
using System.Text;

namespace LayoutParserMcp.Hub;

/// <summary>
/// Hash de token: HMAC-SHA256 com "pepper" (segredo do servidor, vindo de env) — só o hash vai
/// para o banco. O pepper impede que um dump do SQL permita testar tokens por força bruta offline.
/// Nunca logue o token nem o pepper.
/// </summary>
public sealed class TokenHasher
{
    private readonly byte[] _pepper;

    public TokenHasher(string pepper)
    {
        if (string.IsNullOrWhiteSpace(pepper) || pepper.Length < 16)
            throw new ArgumentException("O pepper deve ter ao menos 16 caracteres.", nameof(pepper));
        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    /// <summary>Calcula o hash (32 bytes) do token.</summary>
    public byte[] Hash(string token) => HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(token));

    /// <summary>Hash em hexadecimal com prefixo 0x (literal varbinary do SQL Server).</summary>
    public string HashToSqlLiteral(string token) => "0x" + Convert.ToHexString(Hash(token));
}
