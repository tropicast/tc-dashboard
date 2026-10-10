import type { Me } from './types';

export function selectTenant(memberships: Me['memberships']) {
  const saved = localStorage.getItem('tenant-id');
  const selected =
    memberships.find((membership) => membership.tenantId === saved) ?? memberships[0];
  if (selected) localStorage.setItem('tenant-id', selected.tenantId);
  else localStorage.removeItem('tenant-id');
  return selected?.tenantId;
}
