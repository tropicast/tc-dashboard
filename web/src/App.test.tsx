import { render, screen } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import App from './App';

afterEach(() => vi.unstubAllGlobals());

test('shows the API version', async () => {
  const fetch = vi.fn(async () => Response.json({ version: '1.2.3' }));
  vi.stubGlobal('fetch', fetch);
  render(<App />);
  expect(await screen.findByText('API 1.2.3')).toBeInTheDocument();
  expect(fetch).toHaveBeenCalledTimes(1);
});
