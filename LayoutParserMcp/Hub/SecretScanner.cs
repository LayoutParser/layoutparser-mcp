using System.Text.RegularExpressions;

namespace LayoutParserMcp.Hub;

/// <summary>
/// Rejeita, no servidor, conteúdo com padrões óbvios de segredo (connection string com senha,
/// chaves/tokens conhecidos). Não é DLP completo — é a barreira mínima do ADR. O retorno traz só
/// a CATEGORIA encontrada, nunca o trecho (para não devolver/logar o segredo).
/// </summary>
public static class SecretScanner
{
    private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly (string Categoria, Regex Padrao)[] Padroes =
    [
        ("connection string com senha", new(@"\b(password|pwd)\s*=\s*[^;\s'""]{1,}", Opt | RegexOptions.IgnoreCase)),
        ("chave privada", new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", Opt)),
        ("token Bearer", new(@"\bbearer\s+[A-Za-z0-9\-._~+/]{20,}=*", Opt | RegexOptions.IgnoreCase)),
        ("JWT", new(@"\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]*", Opt)),
        ("chave AWS", new(@"\bAKIA[0-9A-Z]{16}\b", Opt)),
        ("token GitHub", new(@"\b(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})", Opt)),
        ("chave de API Google", new(@"\bAIza[0-9A-Za-z_\-]{35}\b", Opt)),
        ("chave de API (sk-)", new(@"\bsk-[A-Za-z0-9_\-]{20,}", Opt)),
        ("credencial atribuída (key/secret/token)", new(@"\b(api[_-]?key|secret|token|passwd|client[_-]?secret)\s*[:=]\s*['""]?[A-Za-z0-9/+_\-]{16,}", Opt | RegexOptions.IgnoreCase)),
    ];

    /// <summary>Devolve a categoria do primeiro padrão encontrado ou <c>null</c> se limpo.</summary>
    public static string? FindSecretCategory(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var (categoria, padrao) in Padroes)
            if (padrao.IsMatch(text)) return categoria;
        return null;
    }
}
