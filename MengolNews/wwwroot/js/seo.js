export function setMetaTags(title, description, image, url) {

    document.title = title;

    setMeta("description", description);

    setOg("og:title", title);
    setOg("og:description", description);
    setOg("og:image", image);
    setOg("og:url", url);
    setOg("og:type", "article");

    setOg("twitter:card", "summary_large_image");
}

function setMeta(name, content) {
    let tag = document.querySelector(`meta[name='${name}']`);
    if (!tag) {
        tag = document.createElement("meta");
        tag.setAttribute("name", name);
        document.head.appendChild(tag);
    }
    tag.setAttribute("content", content);
}

function setOg(property, content) {
    let tag = document.querySelector(`meta[property='${property}']`);
    if (!tag) {
        tag = document.createElement("meta");
        tag.setAttribute("property", property);
        document.head.appendChild(tag);
    }
    tag.setAttribute("content", content);
}

