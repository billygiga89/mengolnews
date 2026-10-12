// Cloudflare Pages Function — dynamic rendering para /noticia
//
// Objetivo: como o site é uma SPA Blazor WASM, crawlers que não executam
// JavaScript (Facebook, Twitter/X, WhatsApp, Telegram, Google em alguns casos)
// recebem HTML vazio e não conseguem indexar/gerar preview das notícias.
//
// Esta function intercepta pedidos a /noticia?id=... (ou ?url=..., links antigos)
// vindos de bots conhecidos, busca os metadados na API e devolve um HTML já pronto,
// com todas as meta tags. Usuários reais (navegador) passam direto
// para o fluxo normal do Blazor.
//
// Notícia inexistente + bot -> 404 de verdade (evita "soft 404" no Google).
// API fora do ar / acordando (cold start) + bot -> 503 (erro temporário),
// para o bot tentar de novo depois em vez de indexar uma página vazia.

const BOT_REGEX =
  /facebookexternalhit|Facebot|Twitterbot|LinkedInBot|WhatsApp|TelegramBot|Slackbot|Discordbot|Googlebot|Google-InspectionTool|GoogleOther|bingbot|Applebot|Pinterest|redditbot|SkypeUriPreview|vkShare|W3C_Validator|Baiduspider|YandexBot/i;

const ID_REGEX = /^[0-9a-f]{64}$/i;

const API_BASE = "https://mengolnews-api.onrender.com";
const SITE_URL = "https://www.mengolnews.com.br";
const IMAGEM_PADRAO = `${SITE_URL}/images/Flamengo.png`;
const TIMEOUT_API_MS = 8000;

function escapeHtml(str) {
  return String(str ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function truncar(texto, max = 160) {
  if (!texto) return "";
  const limpo = texto.trim();
  return limpo.length > max ? limpo.slice(0, max - 3).trimEnd() + "..." : limpo;
}

function naoEncontrada() {
  return new Response(
    `<!DOCTYPE html><html lang="pt-BR"><head><meta charset="utf-8" />
<title>Notícia não encontrada | MengolNews</title>
<meta name="robots" content="noindex" /></head>
<body><h1>Notícia não encontrada</h1>
<p><a href="${SITE_URL}/noticias">Ver as últimas notícias do Flamengo</a></p></body></html>`,
    {
      status: 404,
      headers: { "content-type": "text/html; charset=utf-8" },
    }
  );
}

function indisponivel() {
  return new Response(
    `<!DOCTYPE html><html lang="pt-BR"><head><meta charset="utf-8" />
<title>Indisponível no momento | MengolNews</title>
<meta name="robots" content="noindex" /></head>
<body><h1>Indisponível no momento</h1>
<p>Tente novamente em instantes.</p></body></html>`,
    {
      status: 503,
      headers: {
        "content-type": "text/html; charset=utf-8",
        "retry-after": "120",
        "cache-control": "no-store",
      },
    }
  );
}

export async function onRequest(context) {
  const { request, next } = context;
  const userAgent = request.headers.get("user-agent") || "";

  // Usuário normal (navegador) -> segue o fluxo padrão do Blazor (SPA)
  if (!BOT_REGEX.test(userAgent)) {
    return next();
  }

  const requestUrl = new URL(request.url);
  const noticiaId = requestUrl.searchParams.get("id");
  const noticiaUrl = requestUrl.searchParams.get("url"); // links antigos

  // Sem parâmetro de notícia, ou id inválido -> 404 real para o bot
  if (!noticiaId && !noticiaUrl) {
    return naoEncontrada();
  }
  if (noticiaId && !ID_REGEX.test(noticiaId)) {
    return naoEncontrada();
  }

  const consulta = noticiaId
    ? `id=${encodeURIComponent(noticiaId)}`
    : `url=${encodeURIComponent(noticiaUrl)}`;

  try {
    const apiRes = await fetch(
      `${API_BASE}/api/noticias/meta?${consulta}`,
      {
        signal: AbortSignal.timeout(TIMEOUT_API_MS),
        cf: {
          cacheEverything: true,
          // Só guarda em cache respostas boas; erros (cold start) nunca ficam em cache
          cacheTtlByStatus: { "200-299": 300, "404": 60, "500-599": -1 },
        },
      }
    );

    // Notícia realmente não existe -> 404 real
    if (apiRes.status === 404) {
      return naoEncontrada();
    }

    // Outro erro (cold start do Render, 5xx etc.) -> 503 temporário
    if (!apiRes.ok) {
      return indisponivel();
    }

    const noticia = await apiRes.json();

    // Endereço oficial da notícia: sempre o novo, com id (sem domínio de fonte)
    const enderecoOficial = noticia.id
      ? `${SITE_URL}/noticia?id=${noticia.id}`
      : requestUrl.toString();

    const titulo = escapeHtml(noticia.titulo || "Notícia");
    const descricao = escapeHtml(truncar(noticia.descricao));
    const imagem = escapeHtml(noticia.imagem || IMAGEM_PADRAO);
    const paginaUrl = escapeHtml(enderecoOficial);
    const dataPublicacao = noticia.data ? new Date(noticia.data).toISOString() : "";

    const jsonLd = JSON.stringify({
      "@context": "https://schema.org",
      "@type": "NewsArticle",
      headline: noticia.titulo || "",
      description: noticia.descricao || "",
      image: [noticia.imagem || IMAGEM_PADRAO],
      datePublished: dataPublicacao || undefined,
      publisher: {
        "@type": "Organization",
        name: "MengolNews",
      },
    });

    const html = `<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="utf-8" />
<title>${titulo} | MengolNews</title>
<meta name="description" content="${descricao}" />
<link rel="canonical" href="${paginaUrl}" />

<meta property="og:type" content="article" />
<meta property="og:site_name" content="MengolNews" />
<meta property="og:locale" content="pt_BR" />
<meta property="og:title" content="${titulo}" />
<meta property="og:description" content="${descricao}" />
<meta property="og:image" content="${imagem}" />
<meta property="og:url" content="${paginaUrl}" />
${dataPublicacao ? `<meta property="article:published_time" content="${dataPublicacao}" />` : ""}

<meta name="twitter:card" content="summary_large_image" />
<meta name="twitter:title" content="${titulo}" />
<meta name="twitter:description" content="${descricao}" />
<meta name="twitter:image" content="${imagem}" />

<script type="application/ld+json">${jsonLd}</script>
</head>
<body>
<article>
<h1>${titulo}</h1>
<p>${descricao}</p>
<img src="${imagem}" alt="${titulo}" />
<p><a href="${paginaUrl}">Ler no MengolNews</a></p>
</article>
</body>
</html>`;

    return new Response(html, {
      status: 200,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  } catch (err) {
    // Timeout ou falha de rede ao falar com a API -> 503 temporário
    return indisponivel();
  }
}