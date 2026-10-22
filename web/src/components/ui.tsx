import type { ComponentChildren, CSSProperties, JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import { Icon, type IconName } from './Icon';

type ButtonVariant = 'primary' | 'ghost' | 'quiet' | 'danger' | 'dangerSolid' | 'launch' | 'stop';
type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonProps {
  children?: ComponentChildren;
  variant?: ButtonVariant;
  size?: ButtonSize;
  icon?: IconName;
  iconRight?: IconName;
  loading?: boolean;
  block?: boolean;
  class?: string;
  disabled?: boolean;
  title?: string;
  type?: 'button' | 'submit' | 'reset';
  onClick?: (event: MouseEvent) => void;
  'aria-label'?: string;
  'aria-expanded'?: boolean;
  'aria-haspopup'?: boolean;
}

const BUTTON_VARIANTS: Record<ButtonVariant, string> = {
  primary: 'btn--primary',
  ghost: 'btn--ghost',
  quiet: 'btn--quiet',
  danger: 'btn--danger',
  dangerSolid: 'btn--danger-solid',
  launch: 'btn--launch',
  stop: 'btn--stop'
};

const BUTTON_SIZES: Record<ButtonSize, string> = {
  sm: 'btn--sm',
  md: '',
  lg: 'btn--lg'
};

export function Button({
  children,
  variant = 'ghost',
  size = 'md',
  icon,
  iconRight,
  loading,
  block,
  class: className,
  disabled,
  title,
  type = 'button',
  onClick,
  ...rest
}: ButtonProps) {
  const classes = [
    'btn',
    BUTTON_VARIANTS[variant],
    BUTTON_SIZES[size],
    block ? 'btn--block' : '',
    variant === 'launch' && icon ? 'btn--with-play' : '',
    className ?? ''
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <button
      type={type}
      class={classes}
      disabled={disabled || loading}
      title={title}
      onClick={onClick}
      {...rest}
    >
      {loading ? (
        <span class="btn-spinner" aria-hidden="true" />
      ) : variant === 'launch' && icon === 'play' ? (
        <span class="btn__play" aria-hidden="true">
          <Icon name="play" size={13} />
        </span>
      ) : icon ? (
        <Icon name={icon} size={15} />
      ) : null}
      {children}
      {iconRight ? <Icon name={iconRight} size={14} /> : null}
    </button>
  );
}

interface IconButtonProps {
  icon: IconName;
  label: string;
  size?: number;
  class?: string;
  tone?: 'default' | 'danger';
  disabled?: boolean;
  onClick?: (event: MouseEvent) => void;
  'aria-expanded'?: boolean;
}

export function IconButton({
  icon,
  label,
  size = 16,
  class: className,
  tone = 'default',
  disabled,
  onClick,
  ...rest
}: IconButtonProps) {
  return (
    <button
      type="button"
      class={['icon-btn', tone === 'danger' ? 'icon-btn--danger' : '', className ?? ''].filter(Boolean).join(' ')}
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      {...rest}
    >
      <Icon name={icon} size={size} />
    </button>
  );
}

export function Spinner({ size = 16 }: { size?: number }) {
  return (
    <span
      class="spin"
      aria-hidden="true"
      style={{
        display: 'inline-block',
        width: `${size}px`,
        height: `${size}px`,
        border: '1.5px solid currentColor',
        borderRightColor: 'transparent',
        borderRadius: '50%'
      }}
    />
  );
}

interface FieldProps {
  label?: string;
  optional?: string;
  hint?: string;
  error?: string | null;
  htmlFor?: string;
  children: ComponentChildren;
  class?: string;
}

export function Field({ label, optional, hint, error, htmlFor, children, class: className }: FieldProps) {
  return (
    <div class={['field', className ?? ''].filter(Boolean).join(' ')}>
      {label ? (
        <label class="field__label" for={htmlFor}>
          {label}
          {optional ? <span class="field__optional">{optional}</span> : null}
        </label>
      ) : null}
      {children}
      {error ? <div class="field__error">{error}</div> : hint ? <div class="field__hint">{hint}</div> : null}
    </div>
  );
}

interface ToggleProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  disabled?: boolean;
}

export function Toggle({ checked, onChange, label, disabled }: ToggleProps) {
  return (
    <button
      type="button"
      class="switch"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    />
  );
}

interface SliderProps {
  value: number;
  min: number;
  max: number;
  step?: number;
  label: string;
  disabled?: boolean;
  onInput?: (value: number) => void;
  onChange?: (value: number) => void;
}

export function Slider({ value, min, max, step = 1, label, disabled, onInput, onChange }: SliderProps) {
  const span = Math.max(1, max - min);
  const fill = Math.max(0, Math.min(100, ((value - min) / span) * 100));
  return (
    <input
      class="range"
      type="range"
      min={min}
      max={max}
      step={step}
      value={value}
      disabled={disabled}
      aria-label={label}
      style={{ '--fill': `${fill}%` } as CSSProperties}
      onInput={(event) => onInput?.(Number((event.target as HTMLInputElement).value))}
      onChange={(event) => onChange?.(Number((event.target as HTMLInputElement).value))}
    />
  );
}

interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  disabled?: boolean;
  title?: string;
}

interface SegmentedProps<T extends string> {
  value: T;
  options: Array<SegmentedOption<T>>;
  onChange: (value: T) => void;
  label: string;
  class?: string;
}

export function Segmented<T extends string>({ value, options, onChange, label, class: className }: SegmentedProps<T>) {
  return (
    <div class={['segmented', className ?? ''].filter(Boolean).join(' ')} role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value === option.value}
          disabled={option.disabled}
          title={option.title}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

interface ProgressBarProps {
  value: number;
  indeterminate?: boolean;
  thin?: boolean;
}

export function ProgressBar({ value, indeterminate, thin }: ProgressBarProps) {
  const pct = Math.max(0, Math.min(100, value * 100));
  return (
    <div
      class={['progress', indeterminate ? 'progress--indeterminate' : '', thin ? 'progress--thin' : '']
        .filter(Boolean)
        .join(' ')}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={indeterminate ? undefined : Math.round(pct)}
    >
      <div class="progress__fill" style={{ width: `${pct}%` }} />
    </div>
  );
}

interface SheetProps {
  open: boolean;
  onClose: () => void;
  title: string;
  eyebrow?: string;
  subtitle?: string;
  width?: 'normal' | 'wide' | 'narrow';
  children: ComponentChildren;
  footer?: ComponentChildren;
  footerNote?: ComponentChildren;
  titleId: string;
  dismissible?: boolean;
}

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

export function Sheet({
  open,
  onClose,
  title,
  eyebrow,
  subtitle,
  width = 'normal',
  children,
  footer,
  footerNote,
  titleId,
  dismissible = true
}: SheetProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    const panel = panelRef.current;
    const timer = window.setTimeout(() => {
      const target = panel?.querySelector<HTMLElement>(FOCUSABLE);
      (target ?? panel)?.focus();
    }, 30);

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && dismissible) {
        event.preventDefault();
        closeRef.current();
        return;
      }
      if (event.key !== 'Tab' || !panel) return;
      const nodes = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
        (node) => node.offsetParent !== null
      );
      if (nodes.length === 0) {
        event.preventDefault();
        panel.focus();
        return;
      }
      const first = nodes[0];
      const last = nodes[nodes.length - 1];
      if (event.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus?.();
    };
  }, [open, dismissible]);

  if (!open) return null;

  return (
    <div
      class="sheet-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && dismissible) onClose();
      }}
    >
      <div
        ref={panelRef}
        class={['sheet', width === 'wide' ? 'sheet--wide' : width === 'narrow' ? 'sheet--narrow' : '']
          .filter(Boolean)
          .join(' ')}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <div class="sheet__head">
          <div class="sheet__head-main">
            {eyebrow ? <div class="eyebrow">{eyebrow}</div> : null}
            <h2 class="sheet__title" id={titleId}>
              {title}
            </h2>
            {subtitle ? <p class="sheet__sub">{subtitle}</p> : null}
          </div>
          {dismissible ? (
            <IconButton icon="close" label="关闭" class="sheet__close" onClick={onClose} />
          ) : null}
        </div>
        <div class="sheet__body">{children}</div>
        {footer || footerNote ? (
          <div class="sheet__footer">
            <div class="sheet__footer-note">{footerNote}</div>
            {footer ? <div class="sheet__footer-actions">{footer}</div> : null}
          </div>
        ) : null}
      </div>
    </div>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: ComponentChildren;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: 'default' | 'danger';
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = '确认',
  cancelLabel = '取消',
  tone = 'default',
  busy,
  onConfirm,
  onCancel
}: ConfirmDialogProps) {
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const timer = window.setTimeout(() => confirmRef.current?.focus(), 30);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCancel();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open, onCancel]);

  if (!open) return null;

  return (
    <div
      class="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy) onCancel();
      }}
    >
      <div class="modal" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-text">
        <h2 class="modal__title" id="confirm-title">
          {title}
        </h2>
        <div class="modal__text" id="confirm-text">
          {message}
        </div>
        <div class="modal__actions">
          <Button variant="quiet" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
          <button
            ref={confirmRef}
            type="button"
            class={['btn', tone === 'danger' ? 'btn--danger-solid' : 'btn--primary'].join(' ')}
            onClick={onConfirm}
            disabled={busy}
          >
            {busy ? <span class="btn-spinner" aria-hidden="true" /> : null}
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

interface EmptyProps {
  icon?: IconName;
  title: string;
  text?: string;
  children?: ComponentChildren;
  boxed?: boolean;
}

export function Empty({ icon = 'box', title, text, children, boxed }: EmptyProps) {
  return (
    <div class={['empty', boxed ? 'empty--boxed' : ''].filter(Boolean).join(' ')}>
      <div class="empty__mark">
        <Icon name={icon} size={26} strokeWidth={1.2} />
      </div>
      <div class="empty__title">{title}</div>
      {text ? <div class="empty__text">{text}</div> : null}
      {children ? <div class="empty__actions">{children}</div> : null}
    </div>
  );
}
