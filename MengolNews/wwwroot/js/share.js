window.mnShare = async function (titulo, url) {
    if (navigator.share) {
        try {
            await navigator.share({ title: titulo, url: url });
            return "shared";
        } catch (e) {
            if (e && e.name === "AbortError") return "cancel";
        }
    }
    try {
        await navigator.clipboard.writeText(url);
        return "copied";
    } catch (e) {
        return "fail";
    }
};


