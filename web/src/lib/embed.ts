/** Escapes a value for a double-quoted HTML attribute: the snippet is pasted and parsed as HTML. */
export function attribute(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

/** Snippets for other sites: the iframe, or the script that inserts it. */
export function embedSnippets(embedUrl: string, stationName: string) {
  const url = new URL(embedUrl);
  const path = attribute(url.pathname.replace(/^\/embed\//, ''));
  const title = attribute(stationName);
  return {
    iframe: `<iframe src="${attribute(url.href)}" title="${title} player" width="100%" height="120" style="border:0;max-width:480px" loading="lazy" allow="autoplay"></iframe>`,
    script: `<script src="${attribute(url.origin)}/embed.js" data-station="${path}" data-title="${title} player" async></script>`,
  };
}
