// Cloudflare Pages Function — gera /sitemap.xml dinamicamente.
//
// Como as notícias vêm de RSS ao vivo (sem banco de dados), um sitemap.xml
// estático ficaria desatualizado em minutos. Esta function busca a lista
// atual na API e monta o XML na hora, com cache curto de borda.

const API_BASE = "https://mengolnews-api.onrender.com";
const SITE_URL = "https://www.mengolnews.com.br";

const PAGINAS_ESTATICAS = [
  { path: "/", changefreq: "hourly", priority: "1.0" },
  { path: "/noticias", changefreq: "hourly", priority: "0.9" },
  { path: "/videos", changefreq: "daily", priority: "0.7" },
  { path: "/elenco", changefreq: "weekly", priority: "0.6" },
  { path: "/serie-a", changefreq: "daily", priority: "0.6" },
  { path: "/sobre", changefreq: "monthly", priority: "0.3" },
];

function escapeXml(str) {
  return String(str ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

export async function onRequest(context) {
  let noticias = [];

  try {
    const apiRes = await fetch(`${API_BASE}/api/noticias`, {
      cf: { cacheTtl: 300, cacheEverything: true },
    });

    if (apiRes.ok) {
      noticias = await apiRes.json();
    }
  } catch (err) {
    // Se a API falhar, devolve sitemap só com as páginas estáticas
  }

  const agora = new Date().toISOString();

  const urlsEstaticas = PAGINAS_ESTATICAS.map(
    (p) => `  <url>
    <loc>${SITE_URL}${p.path}</loc>
    <lastmod>${agora}</lastmod>
    <changefreq>${p.changefreq}</changefreq>
    <priority>${p.priority}</priority>
  </url>`
  );

  const urlsNoticias = (noticias || [])
    .filter((n) => n && n.link)
    .map((n) => {
      const loc = `${SITE_URL}/noticia?url=${encodeURIComponent(n.link)}`;
      const lastmod = n.data ? new Date(n.data).toISOString() : agora;
      return `  <url>
    <loc>${escapeXml(loc)}</loc>
    <lastmod>${lastmod}</lastmod>
    <changefreq>daily</changefreq>
    <priority>0.8</priority>
  </url>`;
    });

  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${[...urlsEstaticas, ...urlsNoticias].join("\n")}
</urlset>`;

  return new Response(xml, {
    status: 200,
    headers: {
      "content-type": "application/xml; charset=utf-8",
      "cache-control": "public, max-age=300",
    },
  });
}
