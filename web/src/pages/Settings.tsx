import { useEffect, useState } from 'preact/hooks';
import { onEvent, request } from '../lib/bridge';
import {
  formatBytes,
  formatPercent,
  loaderName
} from '../lib/format';
import {
  accountSheetOpen,
  accounts,
  activeAccount,
  applyAccounts,
  appInfo,
  errorMessage,
  instances,
  javaProgress,
  memoryInfo,
  settings,
  showToast,
  systemInfo,
  updateSettings
} from '../lib/store';
import type { AccountsPayload, JavaRuntime, JavaScanPayload } from '../lib/types';
import { Icon } from '../components/Icon';
import { Button, Field, ProgressBar, Segmented, Slider, Toggle } from '../components/ui';

const SECTIONS = [
  ['game', '游戏'],
  ['java', 'Java'],
  ['downloads', '下载'],
  ['account', '账号'],
  ['advanced', '高级'],
  ['about', '关于']
] as const;

export function SettingsPage() {
  const current = settings.value;
  const [memoryValue, setMemoryValue] = useState(current.memoryMb ?? memoryInfo.value.recommendedMb ?? 4096);
  const [concurrency, setConcurrency] = useState(current.downloadConcurrency);
  const [width, setWidth] = useState(String(current.width));
  const [height, setHeight] = useState(String(current.height));
  const [jvmArgs, setJvmArgs] = useState(current.jvmArgs);
  const [clientId, setClientId] = useState(current.microsoftClientId);
  const [curseKey, setCurseKey] = useState(current.curseForgeApiKey);
  const [savingClient, setSavingClient] = useState(false);
  const [savingCurse, setSavingCurse] = useState(false);
  const [javaScan, setJavaScan] = useState<JavaScanPayload | null>(null);
  const [scanning, setScanning] = useState(false);
  const [pickingJava, setPickingJava] = useState(false);
  const [downloadingMajor, setDownloadingMajor] = useState<number | null>(null);
  const [cancellingJava, setCancellingJava] = useState(false);
  const [activeSection, setActiveSection] = useState<string>('game');

  const maxMemory = Math.max(
    2048,
    Math.min(32768, Math.floor(((memoryInfo.value.totalMb || 16384) - 1024) / 256) * 256)
  );

  useEffect(() => {
    setMemoryValue(current.memoryMb ?? memoryInfo.value.recommendedMb ?? 4096);
  }, [current.memoryMb, memoryInfo.value.recommendedMb]);

  useEffect(() => setConcurrency(current.downloadConcurrency), [current.downloadConcurrency]);
  useEffect(() => {
    setWidth(String(current.width));
    setHeight(String(current.height));
  }, [current.width, current.height]);
  useEffect(() => setJvmArgs(current.jvmArgs), [current.jvmArgs]);
  useEffect(() => setClientId(current.microsoftClientId), [current.microsoftClientId]);
  useEffect(() => setCurseKey(current.curseForgeApiKey), [current.curseForgeApiKey]);

  const scanJava = async (force: boolean) => {
    setScanning(true);
    try {
      const result = await request<JavaScanPayload>('java.scan', { force }, force ? 120000 : 60000);
      setJavaScan(result);
    } catch (error) {
      showToast('没有扫描到 Java', 'error', errorMessage(error));
    } finally {
      setScanning(false);
    }
  };

  useEffect(() => {
    void scanJava(false);
    const off = onEvent('java.done', () => window.setTimeout(() => void scanJava(true), 500));
    return off;
  }, []);

  const pickJava = async () => {
    setPickingJava(true);
    try {
      const result = await request<{ path: string | null }>('dialog.pickJava', {}, 120000);
      if (!result?.path) return;
      await request('java.addPath', { path: result.path });
      await scanJava(true);
      showToast('已经记下这个 Java。', 'success');
    } catch (error) {
      showToast('这个路径不能用', 'error', errorMessage(error));
    } finally {
      setPickingJava(false);
    }
  };

  const useRuntime = async (runtime: JavaRuntime) => {
    try {
      await request('java.addPath', { path: runtime.path });
      await scanJava(false);
      showToast(`之后的游戏会优先使用 Java ${runtime.major}。`, 'success');
    } catch (error) {
      showToast('无法切换 Java', 'error', errorMessage(error));
    }
  };

  const useAutoJava = async () => {
    await updateSettings({ javaPath: '' });
    showToast('Java 交回给凿境自动选择。', 'info');
  };

  const downloadJava = async (major: number) => {
    setDownloadingMajor(major);
    try {
      await request('java.download', { major });
      showToast(`正在取回 Java ${major}，可以继续做别的事。`, 'info');
    } catch (error) {
      showToast('无法开始下载 Java', 'error', errorMessage(error));
    } finally {
      setDownloadingMajor(null);
    }
  };

  const cancelJava = async () => {
    setCancellingJava(true);
    try {
      await request('java.cancel');
    } catch (error) {
      showToast('无法停下 Java 下载', 'error', errorMessage(error));
    } finally {
      setCancellingJava(false);
    }
  };

  const saveClientId = async () => {
    const value = clientId.trim();
    setSavingClient(true);
    try {
      await updateSettings({ microsoftClientId: value });
      const payload = await request<AccountsPayload>('accounts.list');
      applyAccounts(payload);
      showToast(value ? 'Azure 客户端 ID 已保存。' : '已清空客户端 ID。', 'success');
    } catch (error) {
      showToast('没有保存', 'error', errorMessage(error));
    } finally {
      setSavingClient(false);
    }
  };

  const saveCurseKey = async () => {
    setSavingCurse(true);
    try {
      await updateSettings({ curseForgeApiKey: curseKey.trim() });
      const payload = await request<AccountsPayload>('accounts.list');
      applyAccounts(payload);
      showToast(curseKey.trim() ? 'CurseForge API Key 已保存。' : '已清空 CurseForge API Key。', 'success');
    } catch (error) {
      showToast('没有保存', 'error', errorMessage(error));
    } finally {
      setSavingCurse(false);
    }
  };

  const commitResolution = () => {
    const nextWidth = Math.min(7680, Math.max(640, Number.parseInt(width, 10) || current.width || 1280));
    const nextHeight = Math.min(4320, Math.max(360, Number.parseInt(height, 10) || current.height || 720));
    setWidth(String(nextWidth));
    setHeight(String(nextHeight));
    void updateSettings({ width: nextWidth, height: nextHeight });
  };

  const preferredPath = current.javaPath && current.javaPath.length > 0 ? current.javaPath : javaScan?.selectedPath ?? null;
  const runtimes = javaScan?.runtimes ?? [];
  const requiredMajors = Array.from(
    new Set<number>([21, ...instances.value.map((card) => card.instance.requiredJava ?? 0).filter(Boolean)])
  );
  const hasSuitable = (major: number) =>
    runtimes.some((runtime) => runtime.major === major || (major >= 17 && runtime.major > major));
  const missingMajors = requiredMajors.filter((major) => !hasSuitable(major)).slice(0, 3);
  const progress = javaProgress.value;

  const goTo = (id: string) => {
    setActiveSection(id);
    document.getElementById(`settings-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <div class="page settings">
      <header class="page-head">
        <div class="page-head__main">
          <h1 class="page-title">设置</h1>
          <p class="page-sub">调整游戏内存、Java、下载源与账号。</p>
        </div>
      </header>

      <div class="settings-layout">
        <nav class="settings-nav" aria-label="设置分区">
          {SECTIONS.map(([id, label]) => (
            <button
              type="button"
              key={id}
              class={['settings-nav__item', activeSection === id ? 'settings-nav__item--active' : '']
                .filter(Boolean)
                .join(' ')}
              onClick={() => goTo(id)}
            >
              <span>{label}</span>
            </button>
          ))}
        </nav>

        <div class="settings-main">
          <section class="settings-section" id="settings-game">
            <SectionHead index="01" eyebrow="GAME" title="游戏" note="内存、窗口与启动行为。" />

            <div class="setting-row">
              <SettingCopy
                title="自动分配内存"
                hint={`按机器情况取一个稳妥的值，当前建议 ${memoryInfo.value.recommendedMb} MB。关掉后可以自己拉。`}
              />
              <Toggle
                checked={current.autoMemory}
                label="自动分配内存"
                onChange={(checked) => {
                  if (!checked) setMemoryValue(current.memoryMb ?? memoryInfo.value.recommendedMb ?? 4096);
                  void updateSettings({ autoMemory: checked });
                }}
              />
            </div>

            <div class={['setting-block', current.autoMemory ? 'setting-block--disabled' : ''].filter(Boolean).join(' ')}>
              <div class="setting-block__head">
                <span>分配内存</span>
                <span class="range-value">{current.autoMemory ? `${memoryInfo.value.recommendedMb} MB · 自动` : `${memoryValue} MB`}</span>
              </div>
              <Slider
                value={current.autoMemory ? memoryInfo.value.recommendedMb : Math.min(maxMemory, memoryValue)}
                min={1024}
                max={maxMemory}
                step={256}
                label="游戏内存（MB）"
                disabled={current.autoMemory}
                onInput={(value) => setMemoryValue(value)}
                onChange={(value) => void updateSettings({ memoryMb: value })}
              />
              <div class="setting-block__foot">
                <span class="mono">本机 {formatBytes(memoryInfo.value.totalMb * 1024 * 1024, 0)}</span>
                <span class="mono">可用 {formatBytes(memoryInfo.value.availableMb * 1024 * 1024, 0)}</span>
              </div>
            </div>

            <div class="setting-row">
              <SettingCopy
                title="自定义窗口尺寸"
                hint="关闭时使用游戏自己的默认窗口。只影响新启动的游戏。"
              />
              <Toggle
                checked={current.customResolution}
                label="自定义窗口尺寸"
                onChange={(checked) => void updateSettings({ customResolution: checked })}
              />
            </div>

            {current.customResolution ? (
              <div class="setting-inputs">
                <Field label="宽度" htmlFor="game-width" hint="640 – 7680">
                  <input
                    id="game-width"
                    class="input input--mono"
                    type="number"
                    min={640}
                    max={7680}
                    value={width}
                    onInput={(event) => setWidth((event.target as HTMLInputElement).value)}
                    onBlur={commitResolution}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') commitResolution();
                    }}
                  />
                </Field>
                <span class="setting-inputs__x mono">×</span>
                <Field label="高度" htmlFor="game-height" hint="360 – 4320">
                  <input
                    id="game-height"
                    class="input input--mono"
                    type="number"
                    min={360}
                    max={4320}
                    value={height}
                    onInput={(event) => setHeight((event.target as HTMLInputElement).value)}
                    onBlur={commitResolution}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') commitResolution();
                    }}
                  />
                </Field>
              </div>
            ) : null}

            <div class="setting-row">
              <SettingCopy
                title="版本隔离"
                hint="每个实例的存档、模组与配置互相隔离；版本文件、运行库和资源库共享，避免重复下载。"
              />
              <span class="chip chip--solid">始终开启</span>
            </div>

            <div class="setting-row">
              <SettingCopy title="启动后显示日志" hint="游戏开始启动时，打开下方的控制台，能及时看见进展与报错。" />
              <Toggle
                checked={current.showConsoleOnLaunch}
                label="启动后显示日志"
                onChange={(checked) => void updateSettings({ showConsoleOnLaunch: checked })}
              />
            </div>

            <div class="setting-row">
              <SettingCopy title="启动后收起凿境" hint="游戏起来之后，把启动器最小化到任务栏，桌面更干净。" />
              <Toggle
                checked={current.closeOnLaunch}
                label="启动后收起凿境"
                onChange={(checked) => void updateSettings({ closeOnLaunch: checked })}
              />
            </div>
          </section>

          <section class="settings-section" id="settings-java">
            <SectionHead index="02" eyebrow="RUNTIME" title="Java" note="选择游戏使用的 Java 运行时。" />

            <div class="setting-row">
              <SettingCopy
                title="自动准备 Java"
                hint="需要的时候从 Adoptium 取回匹配的 JRE，装进凿境自己的目录，不碰系统环境。"
              />
              <Toggle
                checked={current.autoDownloadJava}
                label="自动准备 Java"
                onChange={(checked) => void updateSettings({ autoDownloadJava: checked })}
              />
            </div>

            <div class="java-panel">
              <div class="java-panel__head">
                <span class="eyebrow eyebrow--plain">本机上的 Java</span>
                <div class="java-panel__actions">
                  <Button size="sm" variant="quiet" icon="refresh" loading={scanning} onClick={() => void scanJava(true)}>
                    重新扫描
                  </Button>
                  <Button size="sm" variant="ghost" icon="folder" loading={pickingJava} onClick={() => void pickJava()}>
                    选择 java.exe
                  </Button>
                </div>
              </div>

              {scanning && !javaScan ? (
                <div class="java-loading">
                  <span class="btn-spinner" /> 正在翻看常见的安装位置…
                </div>
              ) : (javaScan?.runtimes.length ?? 0) === 0 ? (
                <div class="java-empty">
                  <Icon name="coffee" size={22} strokeWidth={1.2} />
                  <div>
                    <div>还没有找到 Java。</div>
                    <div class="faint">可以让凿境自动准备，或者手动选择一个 java.exe。</div>
                  </div>
                </div>
              ) : (
                <div class="java-list">
                  {(javaScan?.runtimes ?? []).map((runtime) => {
                    const active = runtime.path === preferredPath;
                    return (
                      <div class={['java-row', active ? 'java-row--active' : ''].filter(Boolean).join(' ')} key={runtime.path}>
                        <span class="java-row__mark mono">{runtime.major}</span>
                        <div class="java-row__main">
                          <div class="java-row__title">
                            Java {runtime.major}
                            <span class="faint">·</span>
                            <span class="java-row__vendor">{runtime.vendor || '未知发行版'}</span>
                            {runtime.managed ? <span class="chip chip--moss">凿境准备</span> : null}
                            {active ? <span class="chip chip--copper">正在使用</span> : null}
                          </div>
                          <div class="java-row__path mono truncate" title={runtime.path}>
                            {runtime.path}
                          </div>
                          <div class="java-row__meta">
                            {runtime.version}
                            {runtime.is64BitHint ? ' · 64 位' : ''}
                          </div>
                        </div>
                        <Button
                          size="sm"
                          variant={active ? 'quiet' : 'ghost'}
                          disabled={active}
                          onClick={() => void useRuntime(runtime)}
                        >
                          {active ? '已选中' : '使用'}
                        </Button>
                      </div>
                    );
                  })}
                </div>
              )}

              {current.javaPath ? (
                <div class="java-auto-line">
                  <span>目前优先使用上面选中的路径。</span>
                  <button type="button" class="text-link" onClick={() => void useAutoJava()}>
                    交回自动选择
                  </button>
                </div>
              ) : null}
            </div>

            {progress ? (
              <div class="java-progress">
                <div class="java-progress__head">
                  <span>
                    正在准备 Java {progress.major}
                    <span class="faint"> · {progress.message ?? progress.stage}</span>
                  </span>
                  <span class="java-progress__actions">
                    <span class="mono">
                      {progress.totalBytes > 0
                        ? formatPercent(progress.receivedBytes / progress.totalBytes)
                        : '···'}
                    </span>
                    <button
                      type="button"
                      class="text-link"
                      disabled={cancellingJava}
                      onClick={() => void cancelJava()}
                    >
                      {cancellingJava ? '正在停下' : '取消'}
                    </button>
                  </span>
                </div>
                <ProgressBar
                  value={progress.totalBytes > 0 ? progress.receivedBytes / progress.totalBytes : 0}
                  indeterminate={progress.totalBytes <= 0}
                />
                <div class="progress-meta">
                  <span>{progress.stage === 'download' ? '正在下载' : progress.stage}</span>
                  <span>
                    {formatBytes(progress.receivedBytes)} / {formatBytes(progress.totalBytes)}
                  </span>
                </div>
              </div>
            ) : missingMajors.length > 0 ? (
              <div class="java-missing">
                <div>
                  <div class="java-missing__title">还缺这些 Java</div>
                  <div class="java-missing__text">
                    有的版本需要更旧或更新的运行时。可以提前取回来，启动时会自动使用。
                  </div>
                </div>
                <div class="java-missing__actions">
                  {missingMajors.map((major) => (
                    <Button
                      key={major}
                      size="sm"
                      variant="ghost"
                      icon="download"
                      loading={downloadingMajor === major}
                      onClick={() => void downloadJava(major)}
                    >
                      Java {major}
                    </Button>
                  ))}
                </div>
              </div>
            ) : null}

            <div class="settings-note">
              自动准备的是 Eclipse Temurin JRE。不会修改系统 PATH，删除数据目录即可移除。
            </div>
          </section>

          <section class="settings-section" id="settings-downloads">
            <SectionHead index="03" eyebrow="NETWORK" title="下载" note="下载源、镜像与并发数。" />

            <div class="setting-row">
              <SettingCopy
                title="下载源"
                hint="智能模式会测速选择官方或镜像；文件仍按官方 SHA1 校验。"
              />
              <Segmented
                label="下载源"
                value={current.downloadSource}
                options={[
                  { value: 'auto', label: '智能' },
                  { value: 'official', label: '官方' },
                  { value: 'mirror', label: '镜像' }
                ]}
                onChange={(value) => void updateSettings({ downloadSource: value })}
              />
            </div>

            <div class="setting-block">
              <div class="setting-block__head">
                <span>同时下载的任务数</span>
                <span class="range-value">{concurrency}</span>
              </div>
              <Slider
                value={concurrency}
                min={4}
                max={32}
                step={2}
                label="下载并发数"
                onInput={(value) => setConcurrency(value)}
                onChange={(value) => void updateSettings({ downloadConcurrency: value })}
              />
              <div class="setting-block__foot">
                <span>更多并行通常更快，也会更占带宽。</span>
                <span class="mono">4 — 32</span>
              </div>
            </div>

            <div class="setting-block">
              <Field
                label="CurseForge API Key"
                htmlFor="curseforge-key"
                hint="用于在内容页搜索 CurseForge。没有 Key 时仍可粘贴项目链接添加，也可以导入 CurseForge 整合包。"
              >
                <div class="inline-form">
                  <input
                    id="curseforge-key"
                    class="input input--mono"
                    value={curseKey}
                    spellcheck={false}
                    autocomplete="off"
                    placeholder="$2a$10$..."
                    onInput={(event) => setCurseKey((event.target as HTMLInputElement).value)}
                  />
                  <Button variant="primary" loading={savingCurse} onClick={() => void saveCurseKey()}>
                    保存
                  </Button>
                </div>
              </Field>
              <div class="setting-block__foot setting-block__foot--links">
                <button
                  type="button"
                  class="text-link"
                  onClick={() => void request('shell.openUrl', { url: 'https://console.curseforge.com/' })}
                >
                  <Icon name="external" size={11} /> 申请 CurseForge API Key
                </button>
                <span class="mono">{accounts.value.curseForgeReady ? '已配置' : '未配置'}</span>
              </div>
            </div>

            <div class="settings-note">
              游戏本体、资源与运行库都按官方清单下载，完成后核对大小与 SHA1。
            </div>
          </section>

          <section class="settings-section" id="settings-account">
            <SectionHead index="04" eyebrow="ACCOUNT" title="账号" note="微软、外置登录与离线账号。" />

            <div class="account-status">
              <div class="account-status__face">
                <span class="mono">{activeAccount.value?.name.slice(0, 1).toUpperCase() ?? '?'}</span>
              </div>
              <div class="account-status__body">
                <div class="account-status__name">{activeAccount.value?.name ?? '还没有账号'}</div>
                <div class="account-status__meta">
                  {activeAccount.value
                    ? activeAccount.value.type === 'microsoft'
                      ? 'Microsoft 账号 · 正版验证'
                      : '离线账号 · 只在本机'
                    : `还有 ${accounts.value.items.length} 个账号`}
                </div>
              </div>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  accountSheetOpen.value = true;
                }}
              >
                打开账号页
              </Button>
            </div>

            <div class="setting-block">
              <Field
                label="Microsoft Azure 客户端 ID"
                htmlFor="microsoft-client"
                hint="在 Azure 门户注册一个应用，启用「允许公共客户端流」，把应用程序（客户端）ID 填在这里。设备代码登录不需要客户端密钥。"
              >
                <div class="inline-form">
                  <input
                    id="microsoft-client"
                    class="input input--mono"
                    value={clientId}
                    spellcheck={false}
                    autocomplete="off"
                    placeholder="00000000-0000-0000-0000-000000000000"
                    onInput={(event) => setClientId((event.target as HTMLInputElement).value)}
                  />
                  <Button variant="primary" loading={savingClient} onClick={() => void saveClientId()}>
                    保存
                  </Button>
                </div>
              </Field>
              <div class="setting-block__foot setting-block__foot--links">
                <button
                  type="button"
                  class="text-link"
                  onClick={() =>
                    void request('shell.openUrl', {
                      url: 'https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade'
                    })
                  }
                >
                  <Icon name="external" size={11} /> 打开 Azure 应用注册
                </button>
                <span class="mono">
                  {accounts.value.microsoftReady ? '已配置，可以使用' : '尚未配置'}
                </span>
              </div>
            </div>

            <div class="settings-note">
              微软登录使用设备代码流程，密码不会经过凿境。凭证使用 Windows DPAPI 加密保存在本机。
            </div>
          </section>

          <section class="settings-section" id="settings-advanced">
            <SectionHead index="05" eyebrow="ADVANCED" title="高级" note="JVM 参数与实例信息。" />

            <Field
              label="额外的 JVM 参数"
              htmlFor="jvm-args"
              hint="会追加到每个实例的启动命令。内存由上方设置控制，不需要在这里写 -Xmx。多个参数用空格分隔。"
            >
              <textarea
                id="jvm-args"
                class="textarea input--mono"
                value={jvmArgs}
                spellcheck={false}
                placeholder="-XX:+UseG1GC -XX:+ParallelRefProcEnabled"
                onInput={(event) => setJvmArgs((event.target as HTMLTextAreaElement).value)}
                onBlur={() => void updateSettings({ jvmArgs: jvmArgs.trim() })}
              />
            </Field>

            <div class="advanced-table">
              <div class="advanced-table__row">
                <span>当前主账号</span>
                <span class="mono">{activeAccount.value?.name ?? '无'}</span>
              </div>
              {instances.value.slice(0, 4).map((card) => (
                <div class="advanced-table__row" key={card.instance.id}>
                  <span>{card.instance.displayName}</span>
                  <span class="mono">
                    {card.instance.gameVersion} · {loaderName(card.instance.loader)}
                    {card.instance.memoryMbOverride ? ` · ${card.instance.memoryMbOverride} MB` : ''}
                  </span>
                </div>
              ))}
            </div>
          </section>

          <section class="settings-section" id="settings-about">
            <SectionHead index="06" eyebrow="ABOUT" title="关于" note="版本信息与数据目录。" />

            <div class="about-mark">
              <div class="about-mark__name serif">凿境</div>
              <div class="about-mark__meta mono">CHISEL · v{appInfo.value?.version ?? '0.1.1'}</div>
              <p>
                从游戏版本、账号、内容到日志，都放在同一个地方。
                没有账号墙，没有广告，也不替你决定怎么玩。
              </p>
            </div>

            <div class="about-paths">
              <div class="about-path">
                <span>数据目录</span>
                <span class="mono truncate" title={appInfo.value?.dataRoot}>{appInfo.value?.dataRoot ?? '—'}</span>
                <Button size="sm" variant="ghost" icon="folder" onClick={() => void request('shell.openData')}>
                  打开
                </Button>
              </div>
              <div class="about-path">
                <span>日志目录</span>
                <span class="mono truncate" title={appInfo.value?.logsDir}>{appInfo.value?.logsDir ?? '—'}</span>
                <Button size="sm" variant="ghost" icon="terminal" onClick={() => void request('logs.openFolder')}>
                  打开
                </Button>
              </div>
              <div class="about-path">
                <span>系统</span>
                <span class="mono truncate">{systemInfo.value?.version ?? 'Windows'} · {systemInfo.value?.arch ?? 'x64'}</span>
              </div>
            </div>

            <div class="about-legal">
              <div class="about-legal__title">边界</div>
              <p>
                凿境不是 Mojang Studios 或 Microsoft 的官方产品，也未获得其背书。
                Minecraft 是 Mojang Studios 的商标。游戏资源从官方渠道获取，启动器不分发游戏本体。
              </p>
              <p>
                通过 Modrinth 与 CurseForge 安装的内容版权归各作者所有，使用前请阅读对应的许可。
                账号凭证使用 Windows DPAPI 加密后保存在本机。
              </p>
              <p>
                实例图标中的方块纹理来自 OpenGameArt「16×16 Block Texture Set」（CC0，ARoachIFoundOnMyPillow）；
                Fabric、Quilt、Forge 等加载器图标来自各自项目，仅用于标识。
              </p>
              <div class="about-legal__links">
                <button
                  type="button"
                  class="text-link"
                  onClick={() => void request('shell.openUrl', { url: 'https://www.minecraft.net/' })}
                >
                  Minecraft 官网 <Icon name="external" size={11} />
                </button>
                <button
                  type="button"
                  class="text-link"
                  onClick={() => void request('shell.openUrl', { url: 'https://modrinth.com/' })}
                >
                  Modrinth <Icon name="external" size={11} />
                </button>
              </div>
            </div>
          </section>
        </div>
      </div>

      <footer class="page-foot">
        <div class="page-foot__group">
          <span>CHISEL {appInfo.value?.version ?? '0.1.1'}</span>
          <span>Made for the quiet hours</span>
        </div>
        <div class="page-foot__group">
          <span>非官方启动器</span>
        </div>
      </footer>
    </div>
  );
}

function SectionHead({
  index,
  eyebrow,
  title,
  note
}: {
  index: string;
  eyebrow: string;
  title: string;
  note: string;
}) {
  return (
    <div class="settings-section__head">
      <div>
        <h2 class="settings-section__title">{title}</h2>
        <p class="settings-section__note">{note}</p>
      </div>
    </div>
  );
}

function SettingCopy({ title, hint }: { title: string; hint: string }) {
  return (
    <div class="setting-copy">
      <div class="setting-copy__title">{title}</div>
      <div class="setting-copy__hint">{hint}</div>
    </div>
  );
}
