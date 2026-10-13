import type { ReactNode } from 'react';
import {
  useController,
  type Control,
  type FieldPath,
  type FieldValues,
  type RegisterOptions,
} from 'react-hook-form';
import { Checkbox } from '@/components/base/checkbox/checkbox';
import { Input, type InputProps } from '@/components/base/input/input';
import { NativeSelect } from '@/components/base/select/select-native';
import { TextArea } from '@/components/base/textarea/textarea';

/** Ties an Untitled UI field to a react-hook-form field; its error message shows as the hint. */
type Bound<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  rules?: RegisterOptions<T, FieldPath<T>>;
  label: string;
  hint?: ReactNode;
};

export function TextField<T extends FieldValues>({
  control,
  name,
  rules,
  hint,
  ...props
}: Bound<T> &
  Pick<
    InputProps,
    'type' | 'autoComplete' | 'placeholder' | 'icon' | 'passwordToggleLabel' | 'inputClassName'
  >) {
  const {
    field: { ref, value, onChange, onBlur },
    fieldState,
  } = useController({ control, name, rules });
  return (
    <Input
      {...props}
      ref={ref}
      name={name}
      value={value ?? ''}
      onChange={onChange}
      onBlur={onBlur}
      isInvalid={fieldState.invalid}
      hint={fieldState.error?.message || hint}
    />
  );
}

export function TextAreaField<T extends FieldValues>({
  control,
  name,
  rules,
  hint,
  label,
}: Bound<T>) {
  const {
    field: { ref, value, onChange, onBlur },
    fieldState,
  } = useController({ control, name, rules });
  return (
    <TextArea
      label={label}
      textAreaRef={ref}
      name={name}
      value={value ?? ''}
      onChange={onChange}
      onBlur={onBlur}
      isInvalid={fieldState.invalid}
      hint={fieldState.error?.message || hint}
      rows={4}
    />
  );
}

export function CheckboxField<T extends FieldValues>({ control, name, label, hint }: Bound<T>) {
  const { field } = useController({ control, name });
  return (
    <Checkbox
      name={field.name}
      isSelected={Boolean(field.value)}
      onChange={field.onChange}
      onBlur={field.onBlur}
      label={label}
      hint={hint}
    />
  );
}

export function SelectField<T extends FieldValues>({
  control,
  name,
  label,
  options,
}: Bound<T> & { options: { label: string; value: string }[] }) {
  const { field } = useController({ control, name });
  return (
    <NativeSelect
      label={label}
      name={field.name}
      value={field.value}
      onChange={(event) => field.onChange(event.target.value)}
      onBlur={field.onBlur}
      options={options}
    />
  );
}
