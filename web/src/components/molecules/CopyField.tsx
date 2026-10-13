import { CopyButton } from '../atoms/CopyButton';

/** A labelled value to copy (Untitled UI code snippet): one line, or a wrapped block for code. */
export function CopyField({
  label,
  value,
  multiline = false,
}: {
  label: string;
  value: string;
  multiline?: boolean;
}) {
  if (multiline) {
    return (
      <div className="flex flex-col gap-1.5">
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium text-secondary">{label}</span>
          <CopyButton value={value} />
        </div>
        <pre className="overflow-x-auto rounded-lg bg-secondary p-3 ring-1 ring-secondary ring-inset">
          <code className="font-mono text-xs break-all whitespace-pre-wrap text-primary">
            {value}
          </code>
        </pre>
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-secondary">{label}</span>
      <div className="flex items-center gap-2 rounded-lg bg-secondary py-1 pr-1 pl-3 ring-1 ring-secondary ring-inset">
        <code className="min-w-0 flex-1 font-mono text-sm break-all text-primary">{value}</code>
        <CopyButton value={value} />
      </div>
    </div>
  );
}
