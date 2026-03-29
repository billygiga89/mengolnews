window.infiniteScroll = {
    observe: function (element, dotnetHelper) {
        if (!(element instanceof Element)) {
            console.warn("Sentinel ainda não é um elemento DOM", element);
            return;
        }
        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    dotnetHelper.invokeMethodAsync("OnScrollChegouNoFim")
                        .catch(() => observer.disconnect()); // ← para se der erro
                }
            });
        }, {
            root: null,
            threshold: 0.1
        });
        observer.observe(element);
    }
};


