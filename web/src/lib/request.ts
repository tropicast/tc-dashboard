import type { components } from '../api/schema';
import i18n from './i18n';
import type { Problem } from './types';

export function problemMessage(error: unknown) {
  const problem = error as Problem | undefined;
  const fields = Object.values(
    (problem as components['schemas']['HttpValidationProblemDetails'])?.errors ?? {},
  )
    .flat()
    .join(' ');
  return fields || problem?.detail || problem?.title || i18n.t('common.requestFailed');
}

export async function data<T>(request: Promise<{ data?: T; error?: Problem }>) {
  const result = await request;
  if (result.error) throw result.error;
  return result.data as T;
}
