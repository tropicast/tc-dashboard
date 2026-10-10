import { afterEach, expect, test } from 'vitest';
import { selectTenant } from './tenant';
import type { Me } from './types';

afterEach(() => localStorage.clear());

const memberships: Me['memberships'] = [
  { tenantId: 'tenant-a', tenantName: 'Alpha', tenantSlug: 'alpha', role: 'Owner' },
  { tenantId: 'tenant-b', tenantName: 'Beta', tenantSlug: 'beta', role: 'Admin' },
];

test('preserves a valid locally selected tenant', () => {
  localStorage.setItem('tenant-id', 'tenant-b');

  expect(selectTenant(memberships)).toBe('tenant-b');
  expect(localStorage.getItem('tenant-id')).toBe('tenant-b');
});

test('uses the first membership when no saved tenant remains valid', () => {
  localStorage.setItem('tenant-id', 'removed-tenant');

  expect(selectTenant(memberships)).toBe('tenant-a');
  expect(localStorage.getItem('tenant-id')).toBe('tenant-a');
});
