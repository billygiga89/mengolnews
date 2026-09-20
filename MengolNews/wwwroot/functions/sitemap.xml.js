// Cloudflare Pages Function — gera /sitemap.xml dinamicamente.
//
// Como as notícias vêm de RSS ao vivo (sem banco de dados), um sitemap.xml
// estático ficaria desatualizado em minutos. Esta function busca as listas
// atuais na API (notícias e artigos) e monta o XML na hora, com cache curto de borda.

const API_BASE = "https://mengolnews-api.onrender.com";
const SITE_URL = "https://www.mengolnews.com.br";

const PAGINAS_ESTATICAS = [
    { path: "/", changefreq: "hourly", priority: "1.0", usaUltimaNoticia: true },
    { path: "/noticias", changefreq: "hourly", priority: "0.9", usaUltimaNoticia: true },
    { path: "/videos", changefreq: "daily", priority: "0.7" },
    { path: "/artigos", changefreq: "weekly", priority: "0.7" },
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

// Devolve a data em ISO ou null se for vazia/inválida
function dataIso(valor) {
    if (!valor) return null;
    const d = new Date(valor);
    return isNaN(d.getTime()) ? null : d.toISOString();
}

// Busca uma lista na API; qualquer falha vira lista vazia (o sitemap sai só com o que deu certo)
async function buscarLista(caminho) {
    try {
        const res = await fetch(`${API_BASE}${caminho}`, {
            cf: { cacheTtl: 300, cacheEverything: true },
        });
        if (!res.ok) return [];
        const dados = await res.json();
        return Array.isArray(dados) ? dados : [];
    } catch (err) {
        return [];
    }
}

function montarUrl({ loc, lastmod, changefreq, priority }) {
    return [
        "  <url>",
        `    <loc>${escapeXml(loc)}</loc>`,
        lastmod ? `    <lastmod>${lastmod}</lastmod>` : null,
        `    <changefreq>${changefreq}</changefreq>`,
        `    <priority>${priority}</priority>`,
        "  </url>",
    ]
        .filter(Boolean)
        .join("\n");
}

export async function onRequest() {
    const [noticias, artigos] = await Promise.all([
        buscarLista("/api/noticias"),
        buscarLista("/api/artigos"),
    ]);

    // Data da notícia mais recente: serve de lastmod pra home e /noticias
    const datasNoticias = noticias
        .map((n) => dataIso(n && n.data))
        .filter(Boolean)
        .sort();
    const ultimaNoticia = datasNoticias.length
        ? datasNoticias[datasNoticias.length - 1]
        : null;

    const urlsEstaticas = PAGINAS_ESTATICAS.map((p) =>
        montarUrl({
            loc: `${SITE_URL}${p.path}`,
            lastmod: p.usaUltimaNoticia ? ultimaNoticia : null,
            changefreq: p.changefreq,
            priority: p.priority,
        })
    );

    const urlsNoticias = noticias
        .filter((n) => n && n.link)
        .map((n) =>
            montarUrl({
                loc: `${SITE_URL}/noticia?url=${encodeURIComponent(n.link)}`,
                lastmod: dataIso(n.data),
                changefreq: "daily",
                priority: "0.8",
            })
        );

    const urlsArtigos = artigos
        .filter((a) => a && a.slug)
        .map((a) =>
            montarUrl({
                loc: `${SITE_URL}/artigo/${encodeURIComponent(a.slug)}`,
                lastmod: dataIso(a.dataPublicacao),
                changefreq: "monthly",
                priority: "0.8",
            })
        );

    const xml = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${[...urlsEstaticas, ...urlsArtigos, ...urlsNoticias].join("\n")}
</urlset>`;

    return new Response(xml, {
        status: 200,
        headers: {
            "content-type": "application/xml; charset=utf-8",
            "cache-control": "public, max-age=300",
        },
    });
}