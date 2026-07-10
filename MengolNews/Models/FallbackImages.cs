namespace MengolNews.Models
{
    public static class FallbackImages
    {
        // Adicione novas imagens aqui — só neste arquivo, nada mais precisa mudar
        public static readonly string[] Imagens = new[]
        {
            "/images/flamengo01.jpg",
            "/images/flamengo02.jpg",
            "/images/flamengo03.jpg",
            "/images/flamengo04.jpg",
            "/images/flamengo05.jpg",
            "/images/flamengo06.jpg",
            "/images/flamengo07.jpg",
            "/images/flamengo08.jpg",
            "/images/flamengo09.png",
            "/images/flamengo10.webp",
            "/images/flamengo11.jpg",
            "/images/flamengo12.webp",
        };

        // Escolhe sempre a mesma imagem para a mesma notícia (baseado no título)
        public static string ObterPara(string chave)
        {
            if (string.IsNullOrWhiteSpace(chave) || Imagens.Length == 0)
                return Imagens.Length > 0 ? Imagens[0] : "";

            var hash = ComputeFnv1aHash(chave);
            var index = (int)(hash % (uint)Imagens.Length);
            return Imagens[index];
        }

        // FNV-1a: hash com boa distribuição, evita agrupamento
        // de títulos parecidos na mesma imagem.
        private static uint ComputeFnv1aHash(string input)
        {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;

            uint hash = fnvOffsetBasis;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }
}