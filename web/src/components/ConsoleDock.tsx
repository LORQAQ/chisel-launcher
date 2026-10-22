import { useEffect, useRef, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { formatBytes, formatPercent, formatSpeed, parseLogLine } from '../lib/format';
import {
  closeConsole,
  consoleOpen,
  consoleTab,
  copyText,
  instances,
  lastDiagnosis,
  launchStatus,
  logs,
  running,
  setConsoleTab
} from '../lib/store';
import type { CrashDiagnosis } from '../lib/types';
import { Empty, IconButton, ProgressBar } from './ui';
import { Icon } from './Icon';

const SEVERITY: Record<string, { label: string; tone: string; icon: 'alert' | 'info' }> = {
  warning: { label: '需要注意', tone: 'copper', icon: 'info' },
  error: { label: '启动中断', tone: 'clay', icon: 'alert' },
  fatal: { label: '严重问题', tone: 'clay', icon: 'alert' }
};

export function ConsoleDock() {
  const bodyRef = useRef<HTMLDivElement>(null);
  const [following, setFollowing] = useState(true);
  const [height, setHeight] = useState(326);
  const [dragging, setDragging] = useState(false);

  const open = consoleOpen.value;
  const tab = consoleTab.value;
  const status = launchStatus.value;
  const diagnosis = lastDiagnosis.value;
  const lines = logs.value.slice(-700);
  const activeId = running.value.instanceId ?? status?.instanceId ?? instances.value[0]?.instance.id ?? null;
  const activeName = activeId
    ? instances.value.find((card) => card.instance.id === activeId)?.instance.displayName ?? '运行记录'
    : '运行记录';

  useEffect(() => {
    if (!open || tab !== 'logs' || !following) return;
    const body = bodyRef.current;
    if (body) body.scrollTop = body.scrollHeight;
  }, [lines.length, open, tab, following]);

  useEffect(() => {
    if (!dragging) return;
    const onMove = (event: PointerEvent) => {
      const next = Math.max(220, Math.min(window.innerHeight - 180, window.innerHeight - event.clientY - 8));
      setHeight(next);
    };
    const onUp = () => setDragging(false);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp, { once: true });
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, [dragging]);

  if (!open) return null;

  const copyAll = () => {
    void copyText(logs.value.map((line) => line.text).join('\n'));
  };

  return (
    <section class="console" style={{ height: `${height}px` }} aria-label="运行日志">
      <div
        class="console__grip"
        role="separator"
        aria-label="调整日志高度"
        aria-orientation="horizontal"
        onPointerDown={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
      />
      <header class="console__head">
        <div class="console__ident">
          <Icon name="terminal" size={14} />
          <span>{activeName}</span>
        </div>
        <div class="console__tabs" role="tablist" aria-label="运行记录">
          <button
            type="button"
            class="console__tab"
            role="tab"
            aria-selected={tab === 'logs'}
            onClick={() => setConsoleTab('logs')}
          >
            日志
            <span class="tabs__count">{logs.value.length > 999 ? '999+' : logs.value.length}</span>
          </button>
          <button
            type="button"
            class="console__tab"
            role="tab"
            aria-selected={tab === 'diagnosis'}
            onClick={() => setConsoleTab('diagnosis')}
          >
            诊断
            {diagnosis ? <span class="live-dot" style={{ background: 'var(--clay)' }} /> : null}
          </button>
        </div>
        <div class="console__head-spacer" />
        <div class="console__actions">
          {tab === 'logs' ? <IconButton icon="copy" label="复制全部日志" size={14} onClick={copyAll} /> : null}
          {tab === 'diagnosis' && diagnosis ? (
            <IconButton
              icon="copy"
              label="复制诊断信息"
              size={14}
              onClick={() => void copyText(diagnosisText(diagnosis))}
            />
          ) : null}
          <IconButton
            icon="folder"
            label="打开日志文件夹"
            size={14}
            onClick={() => void request('logs.openFolder', { instanceId: activeId })}
          />
          <IconButton icon="close" label="收起日志" size={14} onClick={closeConsole} />
        </div>
      </header>

      <div class="console__statusbar">
        <span class={`status-mark ${status?.state === 'failed' ? 'status-mark--bad' : running.value.running ? 'status-mark--live' : ''}`}>
          {status ? stateLabel(status.state) : '待命'}
        </span>
        {status?.state && status.state !== 'running' && status.state !== 'exited' && status.state !== 'failed' ? (
          <>
            <ProgressBar value={status.progress} thin />
            <span>{formatPercent(status.progress)}</span>
          </>
        ) : null}
        <span>{status?.message ?? '启动实例后，日志会显示在这里。'}</span>
        {status && status.bytesTotal > 0 ? (
          <span>
            {formatBytes(status.bytesReceived)} / {formatBytes(status.bytesTotal)}
          </span>
        ) : null}
        {status?.exitCode !== null && status?.exitCode !== undefined && status.state === 'exited' ? (
          <span>退出码 {status.exitCode}</span>
        ) : null}
        <span style={{ marginLeft: 'auto' }}>{activeId ? activeId : 'CHISEL · LOG'}</span>
      </div>

      {tab === 'logs' ? (
        <div
          class="console__body"
          ref={bodyRef}
          onScroll={(event) => {
            const element = event.currentTarget as HTMLDivElement;
            const atBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 28;
            setFollowing(atBottom);
          }}
        >
          {lines.length === 0 ? (
            <div class="console__empty">
              <span>还没有日志。</span>
            </div>
          ) : (
            lines.map((line) => {
              const parsed = parseLogLine(line.text);
              return (
                <div key={`${line.instanceId}-${line.sequence}`} class={`log-line log-line--${parsed.level}`}>
                  <span class="log-line__time">{timeOf(line.at)}</span>
                  <span class="log-line__bar" />
                  <span class="log-line__text">{parsed.text}</span>
                </div>
              );
            })
          )}
        </div>
      ) : (
        <div class="console__body" ref={bodyRef}>
          {diagnosis ? <Diagnosis diagnosis={diagnosis} /> : <NoDiagnosis />}
        </div>
      )}

      {tab === 'logs' && !following && lines.length > 0 ? (
        <button type="button" class="console__jump" onClick={() => setFollowing(true)}>
          <Icon name="chevronDown" size={12} /> 回到最新
        </button>
      ) : null}
    </section>
  );
}

function NoDiagnosis() {
  return (
    <div style={{ padding: '20px 18px' }}>
      <Empty
        icon="check"
        title="还没有需要诊断的事"
        text="如果游戏异常退出，凿境会在这里整理可能的原因与可以尝试的步骤。"
      />
    </div>
  );
}

function Diagnosis({ diagnosis }: { diagnosis: CrashDiagnosis }) {
  const severity = SEVERITY[diagnosis.severity] ?? SEVERITY.error;
  return (
    <div class="diagnosis">
      <div class={`diagnosis__severity chip chip--${severity.tone}`}>
        <Icon name={severity.icon} size={12} />
        {severity.label}
        {diagnosis.exitCode !== null ? <span>· 退出码 {diagnosis.exitCode}</span> : null}
      </div>
      <h3 class="diagnosis__title">{diagnosis.title}</h3>
      <p class="diagnosis__summary">{diagnosis.summary}</p>
      <div class="diagnosis__grid">
        <div>
          <div class="diagnosis__label">可以试试</div>
          <ol class="diagnosis__list">
            {diagnosis.suggestions.map((suggestion, index) => (
              <li key={index}>{suggestion}</li>
            ))}
          </ol>
          {diagnosis.suspectedMods.length > 0 ? (
            <>
              <div class="diagnosis__label" style={{ marginTop: 18 }}>
                可能相关的模组
              </div>
              <div class="diagnosis__chips">
                {diagnosis.suspectedMods.map((mod) => (
                  <span key={mod} class="chip">
                    {mod}
                  </span>
                ))}
              </div>
            </>
          ) : null}
        </div>
        <div>
          <div class="diagnosis__label">日志里的线索</div>
          <pre class="diagnosis__evidence">{diagnosis.evidence || '这次没有抓到更具体的日志行。'}</pre>
        </div>
      </div>
      <div class="diagnosis__paths">
        {diagnosis.latestLogPath ? <PathRow label="latest.log" path={diagnosis.latestLogPath} /> : null}
        {diagnosis.crashReportPath ? <PathRow label="crash report" path={diagnosis.crashReportPath} /> : null}
      </div>
    </div>
  );
}

function PathRow({ label, path }: { label: string; path: string }) {
  return (
    <div class="diagnosis__path">
      <span style={{ color: 'var(--paper-dim)' }}>{label}</span>
      <span>{path}</span>
      <IconButton
        icon="folder"
        label="在文件夹中定位"
        size={12}
        class="icon-btn--sm"
        onClick={() => void request('shell.openPath', { path })}
      />
    </div>
  );
}

function stateLabel(state: string): string {
  if (state === 'checking') return '核对';
  if (state === 'java' || state === 'downloading-java') return 'Java';
  if (state === 'starting') return '启动';
  if (state === 'running') return '运行中';
  if (state === 'exited') return '已结束';
  if (state === 'failed') return '中断';
  return state;
}

function timeOf(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '--:--:--';
  return date.toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function diagnosisText(diagnosis: CrashDiagnosis): string {
  const lines = [
    `${diagnosis.title}（${diagnosis.severity}）`,
    diagnosis.summary,
    '',
    '可以试试：',
    ...diagnosis.suggestions.map((item, index) => `${index + 1}. ${item}`),
    diagnosis.evidence ? `\n线索：\n${diagnosis.evidence}` : ''
  ];
  return lines.join('\n');
}
