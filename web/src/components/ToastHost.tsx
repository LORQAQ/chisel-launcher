import { dismissToast, toasts } from '../lib/store';
import { Icon } from './Icon';

export function ToastHost() {
  return (
    <div class="toasts" role="status" aria-live="polite">
      {toasts.value.map((toast) => (
        <div key={toast.id} class={`toast toast--${toast.tone}`}>
          <span class="toast__icon">
            <Icon
              name={toast.tone === 'success' ? 'check' : toast.tone === 'error' ? 'alert' : toast.tone === 'warn' ? 'info' : 'spark'}
              size={15}
            />
          </span>
          <div class="toast__body">
            <div class="toast__text">{toast.text}</div>
            {toast.detail ? <div class="toast__detail">{toast.detail}</div> : null}
          </div>
          <button type="button" class="toast__close" aria-label="关闭提示" onClick={() => dismissToast(toast.id)}>
            <Icon name="close" size={12} />
          </button>
        </div>
      ))}
    </div>
  );
}
