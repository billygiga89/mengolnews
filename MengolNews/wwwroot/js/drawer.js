// wwwroot/js/drawer.js
// Gaveta lateral de navegação arrastável no mobile (swipe da borda esquerda).
window.mnDrawer = {
    _dotNetRef: null,
    _handlers: null,

    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;

        const nav = document.querySelector('.nav');
        const backdrop = document.querySelector('.nav-backdrop');
        if (!nav || !backdrop) return;

        let startX = 0;
        let currentX = 0;
        let dragging = false;
        let wasOpen = false;

        const EDGE_ZONE = 28; // px a partir da borda esquerda pra iniciar o gesto quando fechado

        function isMobile() {
            return window.matchMedia('(max-width: 768px)').matches;
        }

        function clamp(v, min, max) {
            return Math.max(min, Math.min(max, v));
        }

        function onTouchStart(e) {
            if (!isMobile()) return;
            const t = e.touches[0];
            wasOpen = nav.classList.contains('nav-open');
            // Se está fechado, só inicia o arraste perto da borda esquerda.
            // Se já está aberto, pode arrastar de qualquer ponto pra fechar.
            if (!wasOpen && t.clientX > EDGE_ZONE) return;

            dragging = true;
            startX = t.clientX;
            currentX = startX;
            nav.style.transition = 'none';
            backdrop.style.transition = 'none';
        }

        function onTouchMove(e) {
            if (!dragging) return;
            currentX = e.touches[0].clientX;
            const w = nav.offsetWidth;
            const delta = currentX - startX;
            const base = wasOpen ? 0 : -w;
            const translate = clamp(base + delta, -w, 0);

            nav.style.transform = 'translateX(' + translate + 'px)';
            const progress = clamp(1 + translate / w, 0, 1);
            backdrop.style.opacity = String(progress);
            backdrop.style.pointerEvents = progress > 0.05 ? 'auto' : 'none';
        }

        function settle(open) {
            nav.style.transition = '';
            backdrop.style.transition = '';
            nav.style.transform = open ? 'translateX(0)' : 'translateX(-100%)';
            backdrop.style.opacity = open ? '1' : '0';
            backdrop.style.pointerEvents = open ? 'auto' : 'none';

            if (window.mnDrawer._dotNetRef) {
                window.mnDrawer._dotNetRef.invokeMethodAsync(open ? 'AbrirMenuJS' : 'FecharMenuJS');
            }

            // Depois que a transição terminar, limpa o estilo inline
            // pra deixar o CSS (classe nav-open) no controle novamente.
            setTimeout(function () {
                nav.style.transform = '';
                backdrop.style.opacity = '';
                backdrop.style.pointerEvents = '';
            }, 320);
        }

        function onTouchEnd() {
            if (!dragging) return;
            dragging = false;
            const w = nav.offsetWidth;
            const delta = currentX - startX;
            const threshold = w * 0.3;

            if (wasOpen) {
                settle(!(delta < -threshold));
            } else {
                settle(delta > threshold);
            }
        }

        document.addEventListener('touchstart', onTouchStart, { passive: true });
        document.addEventListener('touchmove', onTouchMove, { passive: true });
        document.addEventListener('touchend', onTouchEnd);

        this._handlers = { onTouchStart, onTouchMove, onTouchEnd };
    },

    destroy: function () {
        if (!this._handlers) return;
        document.removeEventListener('touchstart', this._handlers.onTouchStart);
        document.removeEventListener('touchmove', this._handlers.onTouchMove);
        document.removeEventListener('touchend', this._handlers.onTouchEnd);
        this._handlers = null;
        this._dotNetRef = null;
    }
};
