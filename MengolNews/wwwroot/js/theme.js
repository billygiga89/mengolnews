window.mnTheme = {
    get: function () {
        return localStorage.getItem('mn-theme') || 'dark';
    },
    set: function (theme) {
        localStorage.setItem('mn-theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
    }
};