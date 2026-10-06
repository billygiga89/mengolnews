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
            "/images/flamengo13.webp",
            "/images/flamengo14.webp",
            "/images/flamengo15.webp",
            "/images/flamengo16.webp",
            "/images/flamengo17.webp",
            "/images/flamengo18.webp",
        };

        // Escolhe sempre a mesma imagem para a mesma notícia (baseado no título).
        // Use quando só precisa de UMA imagem avulsa (sem olhar as vizinhas).
        public static string ObterPara(string chave)
        {
            if (string.IsNullOrWhiteSpace(chave) || Imagens.Length == 0)
                return Imagens.Length > 0 ? Imagens[0] : "";

            return Imagens[IndicePreferido(chave)];
        }

        /// <summary>
        /// Distribui as imagens para uma lista de notícias, na ordem em que aparecem na tela.
        /// Só gasta imagem quem realmente precisa de fallback (Precisa = true) e só repete
        /// uma imagem depois que todas as outras já foram usadas.
        /// Devolve um array do mesmo tamanho da lista ("" para quem não precisa).
        /// </summary>
        public static string[] Distribuir(IReadOnlyList<(string Chave, bool Precisa)> itens)
        {
            var resultado = new string[itens.Count];
            Array.Fill(resultado, "");

            if (Imagens.Length == 0) return resultado;

            var usadas = new HashSet<int>();

            for (int i = 0; i < itens.Count; i++)
            {
                if (!itens[i].Precisa) continue;

                // ciclo completo: todas já apareceram, começa um novo
                if (usadas.Count == Imagens.Length)
                    usadas.Clear();

                int indice = IndicePreferido(itens[i].Chave);

                // se a preferida já foi usada, anda para a próxima livre
                while (usadas.Contains(indice))
                    indice = (indice + 1) % Imagens.Length;

                usadas.Add(indice);
                resultado[i] = Imagens[indice];
            }

            return resultado;
        }

        private static int IndicePreferido(string? chave)
            => (int)(ComputeFnv1aHash(chave ?? "") % (uint)Imagens.Length);

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