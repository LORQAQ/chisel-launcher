import { useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import {
  applyAccounts,
  errorMessage,
  showToast,
  updateSettings
} from '../lib/store';
import { navigate } from '../lib/router';
import type { AccountView, AccountsPayload } from '../lib/types';
import { BrandMark, Icon } from './Icon';
import { Button } from './ui';

const NAME_RULE = /^[A-Za-z0-9_]{3,16}$/;

const FEATURES = [
  ['grid', '实例隔离', '每个实例都有独立的存档、模组与配置。'],
  ['box', '安装与导入', '原版、Fabric、Forge、NeoForge、整合包。'],
  ['download', '内容与镜像', 'Modrinth、CurseForge，下载自动选择镜像。']
] as const;

export function Onboarding() {
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [leaving, setLeaving] = useState(false);

  const enter = async () => {
    const value = name.trim();
    if (!NAME_RULE.test(value)) {
      setError('需要 3–16 位，只能使用字母、数字和下划线。');
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const account = await request<AccountView>('accounts.addOffline', { name: value });
      const payload = await request<AccountsPayload>('accounts.select', { id: account.id });
      applyAccounts(payload);
      await updateSettings({ onboarded: true });
      showToast(`欢迎，${account.name}。`, 'success');
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  const lookAround = async () => {
    setLeaving(true);
    await updateSettings({ onboarded: true });
    navigate('home');
  };

  return (
    <div class="welcome">
      <div class="welcome__left">
        <div class="welcome__brand">
          <BrandMark size={32} />
          <span>凿境</span>
          <span class="welcome__version mono">CHISEL</span>
        </div>
        <h1 class="welcome__title">开始使用凿境</h1>
        <p class="welcome__lede">
          先创建一个离线账号。装好游戏后，可以随时添加 Microsoft 或外置登录账号。
        </p>

        <div class="welcome__features">
          {FEATURES.map(([icon, title, text]) => (
            <div class="welcome__feature" key={title}>
              <span class="welcome__feature-icon"><Icon name={icon} size={17} /></span>
              <div>
                <b>{title}</b>
                <span>{text}</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div class="welcome__right">
        <div class="welcome__card">
          <div class="welcome__card-head">
            <h2>创建离线账号</h2>
            <span class="chip">离线</span>
          </div>
          <p class="welcome__card-note">
            这个名字会显示在游戏与多人服务器中。不需要密码，资料只保存在本机。
          </p>
          <label class="welcome__label" for="welcome-name">游戏内名字</label>
          <input
            id="welcome-name"
            class="input welcome__input"
            value={name}
            maxLength={16}
            placeholder="例如 shanzi_27"
            autocomplete="off"
            spellcheck={false}
            onInput={(event) => {
              setName((event.target as HTMLInputElement).value);
              setError(null);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void enter();
            }}
          />
          {error ? <div class="welcome__error">{error}</div> : null}
          <Button block variant="primary" size="lg" loading={busy} onClick={() => void enter()}>
            创建并开始
          </Button>
          <button type="button" class="welcome__skip" onClick={() => void lookAround()} disabled={leaving}>
            稍后再说，先看看界面
          </button>
        </div>
        <div class="welcome__foot">
          <Icon name="info" size={13} />
          游戏资源从 Mojang 官方渠道获取，凿境不分发游戏本体。
        </div>
      </div>
    </div>
  );
}
