import { describe, expect, it } from 'vitest';
import { embedSnippets } from './embed';

describe('embedSnippets', () => {
  it('builds the iframe and the script for the station', () => {
    const { iframe, script } = embedSnippets(
      'https://app.test/embed/mada-radios/radio-mada',
      'Radio "Mada"',
    );
    expect(iframe).toContain('src="https://app.test/embed/mada-radios/radio-mada"');
    expect(iframe).toContain('title="Radio &quot;Mada&quot; player"');
    expect(script).toBe(
      '<script src="https://app.test/embed.js" data-station="mada-radios/radio-mada" data-title="Radio &quot;Mada&quot; player" async></script>',
    );
  });

  it('escapes every attribute metacharacter in the station name', () => {
    const name = `Tom & Jerry's <FM> &copy; "1"`;
    const { iframe, script } = embedSnippets('https://app.test/embed/t/s', name);
    const escaped = 'Tom &amp; Jerry&#39;s &lt;FM&gt; &amp;copy; &quot;1&quot; player';
    expect(iframe).toContain(`title="${escaped}"`);
    expect(script).toContain(`data-title="${escaped}"`);
    // Parsed back, the attribute is the original name.
    const parsed = new DOMParser().parseFromString(iframe, 'text/html').querySelector('iframe');
    expect(parsed?.getAttribute('title')).toBe(`${name} player`);
  });
});
