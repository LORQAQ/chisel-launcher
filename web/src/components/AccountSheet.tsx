import { useEffect, useRef, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { formatDate } from '../lib/format';
import { navigate } from '../lib/router';
import {
  accountSheetOpen,
  accounts,
  activeAccount,
  applyAccounts,
  authState,
  beginMicrosoftAuth,
  cancelMicrosoftAuth,
  copyText,
  errorMessage,
  resetAuthState,
  showToast
} from '../lib/store';
import { invalidateSkin } from '../lib/skins';
import type { AccountView, AccountsPayload } from '../lib/types';
import { Icon } from './Icon';
import { SkinFace, SkinViewer, useSkin } from './Skin';
import { Button, ConfirmDialog, Empty, Field, IconButton, Sheet } from './ui';

const NAME_RULE = /^[A-Za-z0-9_]{3,16}$/;

export function AccountSheet() {
  const open = accountSheetOpen.value;
  const list = accounts.value.items;
  const current = activeAccount.value;
  const skin = useSkin(current);
  const [name, setName] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [removeTarget, setRemoveTarget] = useState<AccountView | null>(null);
  const [removing, setRemoving] = useState(false);
  const [skinError, setSkinError] = useState<string | null>(null);
  const [uploadingSkin, setUploadingSkin] = useState(false);
  const [authServer, setAuthServer] = useState('https://littleskin.cn/api/yggdrasil');
  const [authUser, setAuthUser] = useState('');
  const [authPassword, setAuthPassword] = useState('');
  const [authError, setAuthError] = useState<string | null>(null);
  const [authBusy, setAuthBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const auth = authState.value;

  useEffect(() => {
    if (!open) return;
    resetAuthState();
  }, [open]);

  const close = () => {
    if (auth.phase === 'starting' || auth.phase === 'waiting') void cancelMicrosoftAuth();
    resetAuthState();
    accountSheetOpen.value = false;
  };

  const addOffline = async () => {
    const value = name.trim();
    if (!NAME_RULE.test(value)) {
      setNameError('需要 3–16 位，只能使用字母、数字和下划线。');
      return;
    }
    setNameError(null);
    setAdding(true);
    try {
      const account = await request<AccountView>('accounts.addOffline', { name: value });
      const payload = await request<AccountsPayload>('accounts.select', { id: account.id });
      applyAccounts(payload);
      setName('');
      showToast(`账号 ${account.name} 已添加。`, 'success');
    } catch (error) {
      setNameError(errorMessage(error));
    } finally {
      setAdding(false);
    }
  };

  const selectAccount = async (account: AccountView) => {
    if (account.isActive) return;
    setBusyId(account.id);
    try {
      const payload = await request<AccountsPayload>('accounts.select', { id: account.id });
      applyAccounts(payload);
    } catch (error) {
      showToast('无法切换账号', 'error', errorMessage(error));
    } finally {
      setBusyId(null);
    }
  };

  const removeAccount = async () => {
    if (!removeTarget) return;
    setRemoving(true);
    try {
      const payload = await request<AccountsPayload>('accounts.remove', { id: removeTarget.id });
      applyAccounts(payload);
      showToast('账号已删除。', 'info');
      setRemoveTarget(null);
    } catch (error) {
      showToast('无法删除账号', 'error', errorMessage(error));
    } finally {
      setRemoving(false);
    }
  };

  const chooseSkin = () => fileRef.current?.click();

  const onSkinFile = async (file: File | undefined) => {
    if (!file || !current) return;
    setSkinError(null);
    if (file.type !== 'image/png') {
      setSkinError('请选择 PNG 格式的皮肤。');
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      setSkinError('皮肤文件不能超过 2 MB。');
      return;
    }
    setUploadingSkin(true);
    try {
      const dataUrl = await readFile(file);
      const size = await imageSize(dataUrl);
      const valid = (size.width === 64 && (size.height === 64 || size.height === 32)) || (size.width === 128 && size.height === 128);
      if (!valid) throw new Error('皮肤需要是 64×64、64×32 或 128×128 的 PNG。');
      await request('accounts.setSkin', { accountId: current.id, dataUrl });
      invalidateSkin(current.id);
      showToast('新皮肤已经穿好。', 'success');
    } catch (error) {
      setSkinError(errorMessage(error));
    } finally {
      setUploadingSkin(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  };

  const addAuthlib = async () => {
    const server = authServer.trim();
    const username = authUser.trim();
    if (!server || !username || !authPassword) {
      setAuthError('请填写服务器地址、邮箱和密码。');
      return;
    }
    setAuthError(null);
    setAuthBusy(true);
    try {
      const account = await request<AccountView>('accounts.addAuthlib', {
        server,
        username,
        password: authPassword
      }, 60000);
      const payload = await request<AccountsPayload>('accounts.list');
      applyAccounts(payload);
      setAuthPassword('');
      showToast(`已登录为 ${account.name}。`, 'success');
    } catch (error) {
      setAuthError(errorMessage(error));
    } finally {
      setAuthBusy(false);
    }
  };

  const microsoftReady = accounts.value.microsoftReady;

  return (
    <>
      <Sheet
        open={open}
        onClose={close}
        title="账号"
                subtitle="添加离线、Microsoft 或外置登录账号。"
        titleId="account-sheet-title"
        footerNote="账号凭证加密保存在本机。"
        footer={
          <Button variant="ghost" onClick={close}>
            关闭
          </Button>
        }
      >
        {current ? (
          <div class="account-hero">
            <div class="account-hero__info">
              <div class="eyebrow eyebrow--plain" style={{ color: 'var(--paper-faint)' }}>
                当前账号
              </div>
              <div class="account-hero__name">{current.name}</div>
              <div class="account-hero__meta">
                {current.type === 'microsoft'
                  ? 'Microsoft 账号'
                  : current.type === 'authlib'
                    ? `外置登录 · ${current.authServerName ?? 'authlib-injector'}`
                    : '离线账号'}
                <span>·</span>
                <span class="mono">{current.uuid.slice(0, 8)}</span>
              </div>
              {current.expiresAt ? <div class="account-hero__expiry">凭证有效至 {formatDate(current.expiresAt)}</div> : null}
              <button type="button" class="account-hero__skin-action" onClick={chooseSkin} disabled={uploadingSkin}>
                <Icon name="upload" size={13} />
                {uploadingSkin ? '正在换上…' : '设置本地皮肤'}
              </button>
              <div class="account-hero__expiry">仅用于启动器内的预览，不会改动游戏账号的皮肤。</div>
              {skinError ? <div class="account-hero__error">{skinError}</div> : null}
              <input
                ref={fileRef}
                class="sr-only"
                type="file"
                accept="image/png"
                onChange={(event) => void onSkinFile((event.target as HTMLInputElement).files?.[0])}
              />
            </div>
            <div class="account-hero__stage">
              <div class="skin-stage">
                <SkinViewer dataUrl={skin} label={`${current.name} 的皮肤预览`} />
              </div>
              <span class="account-hero__stage-note">64 × 64 PNG</span>
            </div>
          </div>
        ) : (
          <Empty
            icon="user"
            title="还没有账号"
            text="添加一个离线名字即可开始，也可以登录 Microsoft 账号。"
          />
        )}

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">
              账号列表 <span class="mono">{String(list.length).padStart(2, '0')}</span>
            </span>
          </div>
          {list.length === 0 ? (
            <div class="account-empty-line">还没有账号。</div>
          ) : (
            <div class="account-list">
              {list.map((account) => (
                <div class={['account-row', account.isActive ? 'account-row--active' : ''].filter(Boolean).join(' ')}>
                  <button
                    type="button"
                    class="account-row__main"
                    onClick={() => void selectAccount(account)}
                    aria-label={`切换到 ${account.name}`}
                    disabled={busyId === account.id}
                  >
                    <span class="account-row__face">
                      <SkinFace account={account} class="account-row__face-canvas" />
                    </span>
                    <span class="account-row__text">
                      <span class="account-row__name">{account.name}</span>
                      <span class="account-row__type">
                        {account.type === 'microsoft'
                          ? 'Microsoft'
                          : account.type === 'authlib'
                            ? `外置 · ${account.authServerName ?? 'authlib'}`
                            : '离线'}
                        {account.hasSkin ? ' · 有皮肤' : ''}
                      </span>
                    </span>
                    {account.isActive ? (
                      <span class="account-row__active">
                        <Icon name="check" size={11} /> 当前
                      </span>
                    ) : busyId === account.id ? (
                      <span class="btn-spinner" aria-hidden="true" />
                    ) : null}
                  </button>
                  <IconButton
                    icon="trash"
                    label={`删除 ${account.name}`}
                    tone="danger"
                    size={14}
                    onClick={() => setRemoveTarget(account)}
                  />
                </div>
              ))}
            </div>
          )}
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">
              添加离线账号
            </span>
          </div>
          <Field
            label="游戏内名字"
            error={nameError}
            hint="3–16 位，只能是字母、数字和下划线。这个名字也会出现在多人服务器里。"
          >
            <div class="inline-form">
              <input
                class="input"
                value={name}
                maxLength={16}
                autocomplete="off"
                spellcheck={false}
                placeholder="例如 shanzi_27"
                aria-label="离线账号名字"
                onInput={(event) => {
                  setName((event.target as HTMLInputElement).value);
                  setNameError(null);
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') void addOffline();
                }}
              />
              <Button variant="primary" onClick={() => void addOffline()} loading={adding}>
                添加
              </Button>
            </div>
          </Field>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">外置登录</span>
            <span class="status-mark">authlib-injector</span>
          </div>
          <div class="authlib-panel">
            <div class="authlib-presets">
              <button
                type="button"
                class={['chip', authServer.includes('littleskin') ? 'chip--solid' : ''].filter(Boolean).join(' ')}
                onClick={() => setAuthServer('https://littleskin.cn/api/yggdrasil')}
              >
                LittleSkin
              </button>
              <button
                type="button"
                class="chip"
                onClick={() => setAuthServer('https://')}
              >
                自定义服务器
              </button>
            </div>
            <Field label="认证服务器" hint="通常以 /api/yggdrasil 结尾，只支持 HTTPS。">
              <input
                class="input input--mono"
                value={authServer}
                spellcheck={false}
                autocomplete="off"
                placeholder="https://example.com/api/yggdrasil"
                onInput={(event) => setAuthServer((event.target as HTMLInputElement).value)}
              />
            </Field>
            <div class="authlib-grid">
              <Field label="邮箱 / 用户名">
                <input
                  class="input"
                  value={authUser}
                  autocomplete="username"
                  spellcheck={false}
                  onInput={(event) => setAuthUser((event.target as HTMLInputElement).value)}
                />
              </Field>
              <Field label="密码" error={authError}>
                <input
                  class="input"
                  type="password"
                  value={authPassword}
                  autocomplete="current-password"
                  onInput={(event) => setAuthPassword((event.target as HTMLInputElement).value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void addAuthlib();
                  }}
                />
              </Field>
            </div>
            <div class="authlib-actions">
              <span>密码只用于本次登录，不会保存。</span>
              <Button variant="primary" size="sm" loading={authBusy} onClick={() => void addAuthlib()}>
                登录
              </Button>
            </div>
          </div>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">
              Microsoft 账号
            </span>
            {microsoftReady ? <span class="status-mark status-mark--good">已配置</span> : null}
          </div>

          {auth.phase === 'waiting' && auth.code ? (
            <div class="device-panel">
              <div class="device-panel__head">
                <span>在浏览器中确认登录</span>
                <span class="live-dot live-dot--pulse" />
              </div>
              <div class="device-panel__code">{auth.code.userCode}</div>
              <div class="device-panel__actions">
                <Button
                  size="sm"
                  variant="primary"
                  icon="copy"
                  onClick={() => void copyText(auth.code!.userCode)}
                >
                  复制验证码
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  icon="external"
                  onClick={() => void request('shell.openUrl', { url: auth.code!.verificationUriComplete ?? auth.code!.verificationUri })}
                >
                  重新打开
                </Button>
              </div>
              <div class="device-panel__wait">
                <span class="spinner-dots" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                </span>
                {auth.code.opened ? '已在浏览器打开，并复制了验证码' : '请打开浏览器输入验证码'}
                {auth.message ? ` · ${auth.message}` : ''}
              </div>
              <button type="button" class="device-panel__cancel" onClick={() => void cancelMicrosoftAuth()}>
                取消这次登录
              </button>
            </div>
          ) : auth.phase === 'starting' ? (
            <div class="device-panel device-panel--quiet">
              <span class="btn-spinner" /> 正在向 Microsoft 请求登录代码…
            </div>
          ) : auth.phase === 'success' && auth.account ? (
            <div class="sheet-note">
              已经登录为 <strong>{auth.account.name}</strong>。账号已添加并设为当前账号。
            </div>
          ) : (
            <>
              <div class="device-login">
                <div class="device-login__copy">
                  <div class="device-login__title">使用 Microsoft 账号登录</div>
                  <div class="device-login__hint">
                    点击登录后会自动复制验证码并打开浏览器，授权一次即可。
                  </div>
                </div>
                <Button
                  variant="ghost"
                  icon="globe"
                  disabled={!microsoftReady}
                  onClick={() => void beginMicrosoftAuth()}
                  title={!microsoftReady ? '需要先在设置里填写 Azure 客户端 ID' : undefined}
                >
                  登录 Microsoft
                </Button>
              </div>
              {!microsoftReady ? (
                <div class="sheet-note sheet-note--warn" style={{ marginTop: 10 }}>
                  还没有配置 Azure 客户端 ID。离线账号不影响游玩；
                  <button
                    type="button"
                    class="text-link"
                    onClick={() => {
                      close();
                      navigate('settings');
                    }}
                  >
                    去设置
                  </button>
                  。
                </div>
              ) : null}
              {auth.phase === 'error' ? (
                <div class="sheet-note sheet-note--bad" style={{ marginTop: 10 }}>
                  {auth.error || '这次没能登录，可以再试一次。'}
                </div>
              ) : null}
            </>
          )}
        </section>
      </Sheet>

      <ConfirmDialog
        open={Boolean(removeTarget)}
        title="删除这个账号？"
        message={
          <>
            账号 <b>{removeTarget?.name}</b> 与本地皮肤会被删除，实例和存档不受影响。
          </>
        }
        confirmLabel="删去"
        tone="danger"
        busy={removing}
        onConfirm={() => void removeAccount()}
        onCancel={() => setRemoveTarget(null)}
      />
    </>
  );
}

function readFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error('无法读取这张图片。'));
    reader.readAsDataURL(file);
  });
}

function imageSize(dataUrl: string): Promise<{ width: number; height: number }> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight });
    image.onerror = () => reject(new Error('这张图片无法读取。'));
    image.src = dataUrl;
  });
}
