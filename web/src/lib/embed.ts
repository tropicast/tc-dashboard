/** Snippets for other sites: the iframe, or the script that inserts it. */
export function embedSnippets(embedUrl: string, stationName: string) {
  const url = new URL(embedUrl);
  const path = url.pathname.replace(/^\/embed\//, '');
  const title = stationName.replace(/"/g, '&quot;');
  return {
    iframe: `<iframe src="${url.href}" title="${title} player" width="100%" height="120" style="border:0;max-width:480px" loading="lazy" allow="autoplay"></iframe>`,
    script: `<script src="${url.origin}/embed.js" data-station="${path}" data-title="${title} player" async></script>`,
  };
}
