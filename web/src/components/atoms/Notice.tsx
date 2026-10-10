import { problemMessage } from '../../lib/request';

export function Notice({ error }: { error: unknown }) {
  return error ? (
    <p role="alert" className="notice toast error">
      {problemMessage(error)}
    </p>
  ) : null;
}
