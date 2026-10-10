import type { components } from '../api/schema';
import type { Problem } from './types';

export function problemMessage(error: unknown) {
  const problem = error as Problem | undefined;
  const fields = Object.values(
    (problem as components['schemas']['HttpValidationProblemDetails'])?.errors ?? {},
  )
    .flat()
    .join(' ');
  return fields || problem?.detail || problem?.title || 'Request failed. Please try again.';
}

export async function data<T>(request: Promise<{ data?: T; error?: Problem }>) {
  const result = await request;
  if (result.error) throw result.error;
  return result.data as T;
}
