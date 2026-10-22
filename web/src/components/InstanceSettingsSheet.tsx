import { useEffect, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { formatBytes, formatDate, formatPlaytime, loaderName } from '../lib/format';
import {
  closeInstanceSettings,
  errorMessage,
  instanceSheetId,
  instances,
  launchGame,
  launchStatus,
  memoryInfo,
  openConsole,
  running,
  settings,
  showToast
} from '../lib/store';
import { Button, ConfirmDialog, Field, Sheet, Slider, Toggle } from './ui';
import { Icon } from './Icon';
import { InstanceAvatar } from './InstanceAvatar';

export function InstanceSettingsSheet() {
  const id = instanceSheetId.value;
  const card = id ? instances.value.find((entry) => entry.instance.id === id) ?? null : null;
  const instance = card?.instance;
  const [name, setName] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);
  const [overrideMemory, setOverrideMemory] = useState(false);
  const [memory, setMemory] = useState(4096);
  const [saving, setSaving] = useState(false);
  const [picking, setPicking] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const isRunning = running.value.running && running.value.instanceId === id;
  const globalMemory = settings.value.memoryMb ?? memoryInfo.value.defaultMb ?? 4096;
  const maxMemory = Math.max(2048, Math.min(32768, Math.floor(((memoryInfo.value.totalMb || 16384) - 1024) / 256) * 256));

  useEffect(() => {
    if (!instance) return;
    setName(instance.displayName);
    setOverrideMemory(instance.memoryMbOverride != null);
    setMemory(instance.memoryMbOverride ?? Math.min(maxMemory, Math.max(1024, globalMemory)));
    setNameError(null);
  }, [id]);

  if (!card || !instance) return null;

  const close = () => {
    closeInstanceSettings();
    setConfirmDelete(false);
  };

  const save = async () => {
    const value = name.trim();
    if (value.length < 1) {
      setNameError('名字不能留空。');
      return;
    }
    if (value.length > 40) {
      setNameError('名字最多 40 个字符。');
      return;
    }
    setNameError(null);
    setSaving(true);
    try {
      await request('instance.update', {
        id: instance.id,
        displayName: value,
        memoryMb: overrideMemory ? memory : 0
      });
      showToast('实例的设置已经记下。', 'success');
      close();
    } catch (error) {
      showToast('没有保存成功', 'error', errorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const pickJava = async () => {
    setPicking(true);
    try {
      const result = await request<{ path: string | null }>('dialog.pickJava', {}, 120000);
      if (!result?.path) return;
      await request('instance.update', { id: instance.id, javaPath: result.path });
      showToast('这个实例会使用新的 Java。', 'success');
    } catch (error) {
      showToast('这个 Java 不能用', 'error', errorMessage(error));
    } finally {
      setPicking(false);
    }
  };

  const clearJava = async () => {
    try {
      await request('instance.update', { id: instance.id, clearJava: true });
      showToast('已改回跟随全局 Java。', 'info');
    } catch (error) {
      showToast('无法改回全局 Java', 'error', errorMessage(error));
    }
  };

  const remove = async () => {
    setDeleting(true);
    try {
      await request('instance.delete', { id: instance.id });
      showToast('实例已移入回收站。', 'info');
      setConfirmDelete(false);
      close();
    } catch (error) {
      showToast('无法删除实例', 'error', errorMessage(error));
    } finally {
      setDeleting(false);
    }
  };

  const chooseIcon = async () => {
    try {
      const result = await request<{ dataUrl: string | null }>('instance.chooseIcon', { id: instance.id }, 120000);
      if (result) {
        showToast('实例图标已更新。', 'success');
        close();
      }
    } catch (error) {
      showToast('无法设置图标', 'error', errorMessage(error));
    }
  };

  const clearIcon = async () => {
    try {
      await request('instance.clearIcon', { id: instance.id });
      showToast('已恢复默认图标。', 'info');
      close();
    } catch (error) {
      showToast('无法恢复默认图标', 'error', errorMessage(error));
    }
  };

  const launch = () => {
    close();
    if (isRunning) openConsole('logs');
    else void launchGame(instance.id);
  };

  return (
    <>
      <Sheet
        open={Boolean(id)}
        onClose={close}
        title={instance.displayName}
        subtitle={`${instance.gameVersion} · ${loaderName(instance.loader)}${instance.loaderVersion ? ` ${instance.loaderVersion}` : ''}`}
        titleId="instance-sheet-title"
        footerNote="只影响当前实例。"
        footer={
          <>
            <Button variant="quiet" onClick={close}>
              取消
            </Button>
            <Button variant="primary" loading={saving} onClick={() => void save()}>
              保存改动
            </Button>
          </>
        }
      >
        <div class="instance-facts">
          <div class="instance-fact">
            <span>游玩</span>
            <b>{formatPlaytime(instance.playSeconds)}</b>
          </div>
          <div class="instance-fact">
            <span>存档</span>
            <b class="mono">{card.saveCount}</b>
          </div>
          <div class="instance-fact">
            <span>内容</span>
            <b class="mono">{card.modCount}</b>
          </div>
          <div class="instance-fact">
            <span>占用</span>
            <b class="mono">{formatBytes(card.sizeBytes)}</b>
          </div>
          <div class="instance-fact">
            <span>建于</span>
            <b class="mono">{formatDate(instance.createdAt)}</b>
          </div>
          <div class="instance-fact">
            <span>上次</span>
            <b class="mono">
              {instance.lastPlayedAt ? formatDate(instance.lastPlayedAt) : '还没玩过'}
            </b>
          </div>
        </div>

        {isRunning ? (
          <div class="instance-running">
            <span class="live-dot live-dot--pulse" />
            <div class="instance-running__text">
              <div>这个实例正在运行。</div>
              <div class="instance-running__meta">
                {launchStatus.value?.message ?? '窗口已经打开'}
              </div>
            </div>
            <Button size="sm" variant="ghost" onClick={launch}>
              查看日志
            </Button>
          </div>
        ) : null}

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">图标</span>
            <span class="status-mark">{card.hasIcon ? '自定义' : '默认'}</span>
          </div>
          <div class="instance-icon-editor">
            <InstanceAvatar card={card} size={64} />
            <div class="instance-icon-editor__copy">
              <b>实例图标</b>
              <span>选择一张图片，会裁成 256×256 的方形图标。</span>
            </div>
            <div class="instance-icon-editor__actions">
              <Button size="sm" variant="ghost" icon="image" onClick={() => void chooseIcon()}>
                更换
              </Button>
              {card.hasIcon ? (
                <Button size="sm" variant="quiet" onClick={() => void clearIcon()}>
                  恢复默认
                </Button>
              ) : null}
            </div>
          </div>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">名字</span>
          </div>
          <Field
            label="实例名称"
            htmlFor="instance-name"
            error={nameError}
            hint="只修改显示名称，不改变实例目录。"
          >
            <input
              id="instance-name"
              class="input"
              value={name}
              maxLength={40}
              spellcheck={false}
              onInput={(event) => {
                setName((event.target as HTMLInputElement).value);
                setNameError(null);
              }}
            />
          </Field>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">内存</span>
            <div class="sheet-head-toggle">
              <span>单独设定</span>
              <Toggle
                checked={overrideMemory}
                label="单独设定内存"
                onChange={(checked) => setOverrideMemory(checked)}
              />
            </div>
          </div>
          {overrideMemory ? (
            <>
              <div class="range-row">
                <Slider
                  value={memory}
                  min={1024}
                  max={maxMemory}
                  step={256}
                  label="实例内存（MB）"
                  onInput={(value) => setMemory(value)}
                />
                <span class="range-value">{memory} MB</span>
              </div>
              <div class="field__hint">
                本机共有 {formatBytes(memoryInfo.value.totalMb * 1024 * 1024, 0)} 内存，当前可用约{' '}
                {formatBytes(memoryInfo.value.availableMb * 1024 * 1024, 0)}。
              </div>
            </>
          ) : (
            <div class="instance-settings-line">
              <span>跟随全局设置，当前为 {globalMemory} MB。</span>
            </div>
          )}
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">Java</span>
            {instance.javaPathOverride ? <span class="status-mark status-mark--good">单独指定</span> : null}
          </div>
          <div class="instance-java">
            <div class="instance-java__path">
              <Icon name="coffee" size={14} />
              <span class="mono truncate" title={instance.javaPathOverride ?? settings.value.javaPath ?? '自动选择'}>
                {instance.javaPathOverride ?? '跟随全局设置'}
              </span>
            </div>
            <div class="instance-java__actions">
              {instance.javaPathOverride ? (
                <Button size="sm" variant="quiet" onClick={() => void clearJava()}>
                  改回全局
                </Button>
              ) : null}
              <Button size="sm" variant="ghost" loading={picking} onClick={() => void pickJava()}>
                选择 java.exe
              </Button>
            </div>
          </div>
          <div class="field__hint">
            {instance.requiredJava ? `这个版本需要 Java ${instance.requiredJava} 或更高。` : '需要与游戏版本匹配的 Java。'}
          </div>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">位置</span>
          </div>
          <div class="instance-path">
            <span class="mono truncate" title={card.gameDir}>{card.gameDir}</span>
            <Button size="sm" variant="ghost" icon="folder" onClick={() => void request('instance.openFolder', { id: instance.id })}>
              打开目录
            </Button>
          </div>
          <div class="instance-isolation">
            <Icon name="layers" size={13} />
            <div class="instance-isolation__body">
              <span class="instance-isolation__title">版本隔离</span>
              <span class="instance-isolation__hint">存档、模组与配置只属于这个实例。</span>
            </div>
            <span class="chip chip--solid">已开启</span>
          </div>
        </section>

        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">危险的事</span>
          </div>
          <div class="danger-line">
            <div>
              <div class="danger-line__title">删除实例</div>
              <div class="danger-line__hint">整个实例目录会移入回收站：存档、资源与配置一起离开，但还能从回收站找回。</div>
            </div>
            <Button
              size="sm"
              variant="danger"
              icon="trash"
              disabled={isRunning}
              title={isRunning ? '先结束正在运行的游戏' : undefined}
              onClick={() => setConfirmDelete(true)}
            >
              删除
            </Button>
          </div>
        </section>

        <div class="instance-sheet-cta">
          <Button
            block
            variant={isRunning ? 'stop' : 'launch'}
            icon={isRunning ? 'terminal' : 'play'}
            onClick={launch}
          >
            {isRunning ? '查看这次运行的日志' : '启动游戏'}
          </Button>
        </div>
      </Sheet>

      <ConfirmDialog
        open={confirmDelete}
        title={`删除「${instance.displayName}」？`}
        message={
          <>
            整个实例目录会被移入回收站。这是一个
            <b> 可以撤销 </b>
            的操作，但需要你自己去回收站把它找回来。
          </>
        }
        confirmLabel="移入回收站"
        tone="danger"
        busy={deleting}
        onConfirm={() => void remove()}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
