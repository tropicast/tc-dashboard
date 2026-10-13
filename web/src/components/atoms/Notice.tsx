import { problemMessage } from '../../lib/request';
import { Alert } from './Alert';

export function Notice({ error }: { error: unknown }) {
  return error ? <Alert color="error" title={problemMessage(error)} /> : null;
}
