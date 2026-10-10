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
});
