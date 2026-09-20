// wwwroot/js/customPlayer.js
// Controla o player do YouTube por baixo dos panos via IFrame API, pra
// gente poder desenhar nossa própria barra de progresso/controles em vez
// de depender da UI nativa do YouTube (que não dá pra estilizar por ser
// conteúdo de outro domínio).
window.mnPlayer = {
    _player: null,
    _dotNetRef: null,
    _interval: null,
    _seekBarEl: null,
    _seekHandler: null,
    _apiPromise: null,

    _loadApi: function () {
        if (window.YT && window.YT.Player) {
            return Promise.resolve();
        }
        if (this._apiPromise) return this._apiPromise;

        this._apiPromise = new Promise((resolve) => {
            const jaExiste = document.querySelector('script[src="https://www.youtube.com/iframe_api"]');
            const anterior = window.onYouTubeIframeAPIReady;

            window.onYouTubeIframeAPIReady = function () {
                if (typeof anterior === 'function') anterior();
                resolve();
            };

            if (!jaExiste) {
                const tag = document.createElement('script');
                tag.src = 'https://www.youtube.com/iframe_api';
                document.head.appendChild(tag);
            }
        });

        return this._apiPromise;
    },

    init: async function (iframeId, seekBarEl, dotNetRef) {
        this.destroy();
        await this._loadApi();

        this._dotNetRef = dotNetRef;
        this._seekBarEl = seekBarEl;

        this._player = new YT.Player(iframeId, {
            events: {
                onReady: () => this._startProgressLoop(),
                onStateChange: (e) => {
                    if (this._dotNetRef) {
                        this._dotNetRef.invokeMethodAsync('OnPlayerStateChanged', e.data);
                    }
                }
            }
        });

        this._seekHandler = (ev) => {
            if (!this._player || typeof this._player.getDuration !== 'function') return;
            const rect = this._seekBarEl.getBoundingClientRect();
            const pct = Math.min(1, Math.max(0, (ev.clientX - rect.left) / rect.width));
            const duracao = this._player.getDuration() || 0;
            this._player.seekTo(pct * duracao, true);
        };
        this._seekBarEl.addEventListener('click', this._seekHandler);
    },

    _startProgressLoop: function () {
        clearInterval(this._interval);
        this._interval = setInterval(() => {
            if (!this._player || typeof this._player.getCurrentTime !== 'function') return;
            const atual = this._player.getCurrentTime() || 0;
            const duracao = this._player.getDuration() || 0;
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnProgress', atual, duracao);
            }
        }, 250);
    },

    play: function () {
        if (this._player && typeof this._player.playVideo === 'function') this._player.playVideo();
    },

    pause: function () {
        if (this._player && typeof this._player.pauseVideo === 'function') this._player.pauseVideo();
    },

    toggleMute: function () {
        if (!this._player || typeof this._player.isMuted !== 'function') return true;
        if (this._player.isMuted()) {
            this._player.unMute();
            return false;
        }
        this._player.mute();
        return true;
    },

    destroy: function () {
        clearInterval(this._interval);
        this._interval = null;

        if (this._seekBarEl && this._seekHandler) {
            this._seekBarEl.removeEventListener('click', this._seekHandler);
        }
        this._seekBarEl = null;
        this._seekHandler = null;

        if (this._player && typeof this._player.destroy === 'function') {
            try { this._player.destroy(); } catch (e) { /* ignora */ }
        }
        this._player = null;
        this._dotNetRef = null;
    }
};