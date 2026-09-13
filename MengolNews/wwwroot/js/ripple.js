// wwwroot/js/ripple.js
// Efeito "ripple" (ondulação) estilo Material Design, aplicado globalmente
// via delegação de evento — não precisa adicionar classe em cada componente.
(function () {
    // Seletores que recebem o efeito.
    var SELECTOR = [
        'button',
        '.news-card',
        '.video-card',
        '.player-card',
        '.artigo-card',
        '.match-card',
        '.match-card-v2',
        '.filtro-btn',
        '.seriea-tab',
        '.sobre-canal-btn'
    ].join(', ');

    function createRipple(target, clientX, clientY) {
        var style = window.getComputedStyle(target);

        // Só força position:relative se o elemento ainda não tiver uma posição
        // própria — evita quebrar botões que já usam absolute/fixed de propósito
        // (botão de fechar modal, botão de fechar vídeo, puxadeira da gaveta etc).
        if (style.position === 'static') {
            target.classList.add('mn-ripple-relative');
        }
        target.classList.add('mn-ripple-overflow');

        var rect = target.getBoundingClientRect();
        var size = Math.max(rect.width, rect.height) * 1.8;

        var span = document.createElement('span');
        span.className = 'mn-ripple-circle';
        span.style.width = size + 'px';
        span.style.height = size + 'px';
        span.style.left = (clientX - rect.left - size / 2) + 'px';
        span.style.top = (clientY - rect.top - size / 2) + 'px';

        target.appendChild(span);

        span.addEventListener('animationend', function () {
            span.remove();
        });

        // Segurança: remove mesmo se o evento de animação não disparar
        setTimeout(function () {
            if (span.parentNode) span.remove();
        }, 700);
    }

    document.addEventListener('pointerdown', function (e) {
        if (e.pointerType === 'mouse' && e.button !== 0) return; // só botão esquerdo do mouse
        var target = e.target.closest(SELECTOR);
        if (!target || target.disabled) return;
        createRipple(target, e.clientX, e.clientY);
    }, { passive: true });
})();

