// Cloudflare Pages Function — dynamic rendering para /noticia
//
// Objetivo: como o site é uma SPA Blazor WASM, crawlers que não executam
// JavaScript (Facebook, Twitter/X, WhatsApp, Telegram, Google em alguns casos)
// recebem HTML vazio e não conseguem indexar/gerar preview das notícias.
//
// Esta function intercepta pedidos a /noticia?url=... vindos de bots
// conhecidos, busca os metadados na API e devolve um HTML já pronto,
// com todas as meta tags. Usuários reais (navegador) passam direto
// para o fluxo normal do Blazor.

const BOT_REGEX =
  /facebookexternalhit|Facebot|Twitterbot|LinkedInBot|WhatsApp|TelegramBot|Slackbot|Discordbot|Googlebot|bingbot|Applebot|Pinterest|redditbot|SkypeUriPreview|vkShare|W3C_Validator|Baiduspider|YandexBot/i;

const API_BASE = "https://mengolnews-api.onrender.com";
const SITE_URL = "https://www.mengolnews.com.br";
const IMAGEM_PADRAO = `${SITE_URL}/images/Flamengo.png`;

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

export async function onRequest(context) {
  const { request, next } = context;
  const userAgent = request.headers.get("user-agent") || "";

  // Usuário normal (navegador) -> segue o fluxo padrão do Blazor (SPA)
  if (!BOT_REGEX.test(userAgent)) {
    return next();
  }

  const requestUrl = new URL(request.url);
  const noticiaUrl = requestUrl.searchParams.get("url");

  // Sem parâmetro de notícia -> deixa o Blazor tratar (tela de "não encontrada")
  if (!noticiaUrl) {
    return next();
  }

  try {
    const apiRes = await fetch(
      `${API_BASE}/api/noticias/meta?url=${encodeURIComponent(noticiaUrl)}`,
      { cf: { cacheTtl: 300, cacheEverything: true } }
    );

    if (!apiRes.ok) {
      return next();
    }

    const noticia = await apiRes.json();

    const titulo = escapeHtml(noticia.titulo || "Notícia");
    const descricao = escapeHtml(truncar(noticia.descricao));
    const imagem = escapeHtml(noticia.imagem || IMAGEM_PADRAO);
    const paginaUrl = escapeHtml(requestUrl.toString());
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
    // Qualquer falha -> não trava o bot, deixa cair no fluxo normal
    return next();
  }
}
