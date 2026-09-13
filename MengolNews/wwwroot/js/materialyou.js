window.mnMaterialYou = (function () {
    const cache = new Map();

    function getDominantColor(img) {
        try {
            const canvas = document.createElement('canvas');
            const w = canvas.width = 24;
            const h = canvas.height = 24;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(img, 0, 0, w, h);
            const data = ctx.getImageData(0, 0, w, h).data;

            let r = 0, g = 0, b = 0, count = 0;
            for (let i = 0; i < data.length; i += 4) {
                const alpha = data[i + 3];
                if (alpha < 200) continue;
                r += data[i]; g += data[i + 1]; b += data[i + 2];
                count++;
            }
            if (count === 0) return null;
            return {
                r: Math.round(r / count),
                g: Math.round(g / count),
                b: Math.round(b / count)
            };
        } catch (e) {
            // Canvas "contaminado" por imagem cross-origin sem CORS liberado.
            return null;
        }
    }

    function applyColorToCard(card, color) {
        const { r, g, b } = color;
        card.style.setProperty('--mn-card-accent', `rgb(${r}, ${g}, ${b})`);
        card.style.setProperty('--mn-card-accent-soft', `rgba(${r}, ${g}, ${b}, 0.14)`);
        card.classList.add('mn-colorized');
    }

    function processCard(card) {
        const img = card.querySelector('img');
        if (!img) return;

        const key = img.currentSrc || img.src;
        if (!key) return;

        if (cache.has(key)) {
            applyColorToCard(card, cache.get(key));
            return;
        }

        const run = () => {
            const color = getDominantColor(img);
            if (color) {
                cache.set(key, color);
                applyColorToCard(card, color);
            }
        };

        if (img.complete && img.naturalWidth > 0) {
            run();
        } else {
            img.addEventListener('load', run, { once: true });
        }
    }

    function scan(root) {
        (root || document)
            .querySelectorAll('.news-card, .artigo-card, .video-card, .match-card-v2, .player-card')
            .forEach(processCard);
    }

    function init() {
        scan();

        const observer = new MutationObserver((mutations) => {
            for (const m of mutations) {
                m.addedNodes.forEach((node) => {
                    if (node.nodeType !== 1) return;
                    if (node.matches && node.matches('.news-card, .artigo-card, .video-card, .match-card-v2, .player-card')) {
                        processCard(node);
                    } else if (node.querySelectorAll) {
                        scan(node);
                    }
                });
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }

    return { init, scan };
})();

document.addEventListener('DOMContentLoaded', () => {
    window.mnMaterialYou.init();
});