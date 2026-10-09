using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MengolNews.Api.Services;

public static class DeduplicadorNoticias
{
    // Palavras que aparecem em quase todo título e não ajudam a diferenciar notícias
    private static readonly HashSet<string> PalavrasComuns = new(StringComparer.Ordinal)
    {
        "de","da","do","das","dos","e","a","o","as","os","em","no","na","nos","nas","com","por",
        "para","pra","um","uma","uns","umas","que","se","ao","aos","sobre","apos","como","mais",
        "ja","sua","seu","suas","seus","vai","tem","faz","ser","foi","x","vs","apos","ate","entre",
        "flamengo","fla","mengao","rubro","negro","brasileirao","jogo","partida"
    };

    // Parâmetros ajustáveis
    private const double LimiarTitulo = 0.5;          // coincidência só pelo título
    private const int MinPalavrasEmComum = 3;      // mínimo de palavras iguais pra regra do título
    private const double LimiarTituloComMesmaImagem = 0.25; // se a imagem for igual, basta bem menos

    public static List<T> Deduplicar<T>(
        IEnumerable<T> itens,
        Func<T, string?> titulo,
        Func<T, string?> imagem,
        Func<T, int> prioridade) // menor número = fonte mais confiável (ESPN = 0, etc.)
    {
        var lista = itens
            .Select((item, idx) => new Entrada<T>(item, idx, Tokens(titulo(item)), ChaveImagem(imagem(item)), prioridade(item)))
            .ToList();

        // Quem tem prioridade melhor é avaliado primeiro e "ganha" a vaga
        var porPrioridade = lista.OrderBy(e => e.Prioridade).ThenBy(e => e.Indice).ToList();
        var mantidos = new List<Entrada<T>>();

        foreach (var cand in porPrioridade)
        {
            bool repetida = mantidos.Any(m => EhMesmaNoticia(m, cand));
            if (!repetida) mantidos.Add(cand);
        }

        // Devolve na ordem original da lista
        return mantidos.OrderBy(e => e.Indice).Select(e => e.Item).ToList();
    }

    private static bool EhMesmaNoticia<T>(Entrada<T> a, Entrada<T> b)
    {
        if (a.Palavras.Count == 0 || b.Palavras.Count == 0) return false;

        int comum = a.Palavras.Count(p => b.Palavras.Contains(p));
        double coincidencia = (double)comum / Math.Min(a.Palavras.Count, b.Palavras.Count);

        // Regra 1: títulos muito parecidos
        if (comum >= MinPalavrasEmComum && coincidencia >= LimiarTitulo) return true;

        // Regra 2: mesma imagem + um pouco de assunto em comum
        bool mesmaImagem = a.ChaveImg != null && a.ChaveImg == b.ChaveImg;
        if (mesmaImagem && comum >= 2 && coincidencia >= LimiarTituloComMesmaImagem) return true;

        return false;
    }

    // ---------- helpers ----------

    private static HashSet<string> Tokens(string? texto)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(texto)) return set;

        foreach (var palavra in SemAcento(texto).ToLowerInvariant()
                     .Split(new[] { ' ', '-', ':', ',', '.', '!', '?', '"', '\'', '(', ')', '/', '|', ';', '“', '”', '‘', '’' },
                            StringSplitOptions.RemoveEmptyEntries))
        {
            if (PalavrasComuns.Contains(palavra)) continue;
            // Letras soltas descartadas, mas números (placar) são mantidos
            if (palavra.Length < 2 && !char.IsDigit(palavra[0])) continue;

            // "empate", "empata", "empatam" -> "empat"
            set.Add(palavra.Length > 5 ? palavra[..5] : palavra);
        }
        return set;
    }

    // Identifica a foto pelo nome do arquivo, ignorando tamanho (-1024x683) e query string.
    // Imagens locais (fallback, começam com "/") não entram na comparação.
    private static string? ChaveImagem(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("/")) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var nome = Path.GetFileNameWithoutExtension(uri.AbsolutePath).ToLowerInvariant();
        nome = Regex.Replace(nome, @"[-_]\d{2,4}x\d{2,4}", "");
        nome = Regex.Replace(nome, @"[-_](scaled|large|medium|small|thumb|thumbnail)$", "");
        return nome.Length >= 6 ? nome : null; // nomes muito curtos/genéricos dariam falso positivo
    }

    private static string SemAcento(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record Entrada<T>(T Item, int Indice, HashSet<string> Palavras, string? ChaveImg, int Prioridade);
}
