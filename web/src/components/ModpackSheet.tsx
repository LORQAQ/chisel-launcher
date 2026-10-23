import { useEffect, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { formatBytes, formatPercent, loaderName } from '../lib/format';
import { navigate } from '../lib/router';
import {
  closeModpackSheet,
  errorMessage,
  installJob,
  modpackSheetOpen,
  showToast,
  startInstallation
} from '../lib/store';
import type { LocalPackCandidate, LocalPackScanResult, ModpackPreview } from '../lib/types';
import { Icon } from './Icon';
import { LoaderTile } from './LoaderTile';
import { Button, ProgressBar, Sheet } from './ui';

type Mode = 'file' | 'scan';

export function ModpackSheet() {
  const open = modpackSheetOpen.value;
  const job = installJob.value;
  const [mode, setMode] = useState<Mode>('file');
  const [preview, setPreview] = useState<ModpackPreview | null>(null);
  const [picking, setPicking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [scan, setScan] = useState<LocalPackScanResult | null>(null);
  const [pending, setPending] = useState<LocalPackCandidate | null>(null);

  useEffect(() => {
    if (!open) return;
    setPreview(null);
    setError(null);
    setPicking(false);
    setMode('file');
    setScan(null);
    setPending(null);
  }, [open]);

  useEffect(() => {
    if (open && mode === 'scan' && !scan && !scanning) void runScan();
  }, [open, mode]);

  const runScan = async () => {
    setScanning(true);
    setError(null);
    try {
      setScan(await request<LocalPackScanResult>('modpack.scan', {}, 120000));
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setScanning(false);
    }
  };

  const pick = async () => {
    setPicking(true);
    setError(null);
    try {
      const result = await request<{ path: string | null; preview: ModpackPreview | null }>(
        'modpack.pick', {}, 120000);
      if (result?.preview) setPreview(result.preview);
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setPicking(false);
    }
  };

  const start = async () => {
    if (!preview) return;
    setError(null);
    const jobId = pending
      ? await startInstallation({
          gameVersion: pending.gameVersion,
          loader: pending.loader,
          loaderVersion: pending.loaderVersion,
          displayName: pending.name,
          localInstancePath: pending.path
        })
      : await startInstallation({
          gameVersion: preview.gameVersion,
          loader: preview.loader,
          loaderVersion: preview.loaderVersion,
          displayName: preview.name,
          modpackPath: preview.path
        });
    if (!jobId) setError('安装没有启动，请检查日志。');
  };

  const selectCandidate = async (candidate: LocalPackCandidate) => {
    setError(null);
    if (candidate.kind === 'archive') {
      setPicking(true);
      try {
        const inspected = await request<ModpackPreview>('modpack.inspect', { path: candidate.path }, 120000);
        setPending(null);
        setPreview(inspected);
        setMode('file');
      } catch (reason) {
        setError(errorMessage(reason));
      } finally {
        setPicking(false);
      }
      return;
    }

    if (!candidate.gameVersion) {
      setError('这个实例没能识别出游戏版本，先用原启动器启动一次再来扫描。');
      return;
    }

    setPending(candidate);
    setPreview({
      path: candidate.path,
      kind: candidate.kind,
      name: candidate.name,
      version: `${candidate.launcher}${candidate.modCount ? ` · ${candidate.modCount} 个模组` : ''}`,
      gameVersion: candidate.gameVersion,
      loader: candidate.loader,
      loaderVersion: candidate.loaderVersion,
      fileCount: 0,
      totalBytes: candidate.sizeBytes,
      summary: '会复制 mods、config、资源包、光影与存档；来源目录不会被改动。',
      warnings: []
    });
  };

  const close = () => {
    if (job?.state === 'running') {
      showToast('整合包会在后台继续安装。', 'info');
    }
    closeModpackSheet();
  };

  const running = job?.state === 'running' && Boolean(preview);
  const done = job?.state === 'done' && Boolean(preview);
  const failed = job?.state === 'failed' && Boolean(preview);
  const pct = job?.progress && job.progress.totalBytes > 0
    ? job.progress.completedBytes / job.progress.totalBytes
    : 0;

  return (
    <Sheet
      open={open}
      onClose={close}
      title="导入整合包"
      subtitle="选文件，或扫描本机其他启动器留下的实例"
      titleId="modpack-sheet-title"
      width="normal"
      footerNote={preview ? `目标：${preview.gameVersion || '？'} · ${loaderName(preview.loader)}` : '压缩包与来源目录都不会被修改。'}
      footer={
        done ? (
          <>
            <Button variant="quiet" onClick={close}>关闭</Button>
            <Button
              variant="primary"
              icon="grid"
              onClick={() => {
                close();
                navigate('versions');
              }}
            >
              查看实例
            </Button>
          </>
        ) : failed ? (
          <>
            <Button variant="quiet" onClick={close}>关闭</Button>
            <Button variant="primary" icon="refresh" onClick={() => void start()}>重试</Button>
          </>
        ) : running ? (
          <Button variant="quiet" onClick={close}>后台继续</Button>
        ) : (
          <>
            <Button variant="quiet" onClick={close}>取消</Button>
            <Button variant="primary" icon="download" disabled={!preview || picking} onClick={() => void start()}>
              开始导入
            </Button>
          </>
        )
      }
    >
      {!preview ? (
        <>
          <div class="pack-tabs" role="tablist" aria-label="导入方式">
            <button
              type="button"
              role="tab"
              aria-selected={mode === 'file'}
              onClick={() => setMode('file')}
            >
              选择文件
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={mode === 'scan'}
              onClick={() => setMode('scan')}
            >
              扫描本机
            </button>
          </div>

          {mode === 'file' ? (
            picking ? (
              <div class="modpack-loading">
                <span class="btn-spinner" /> 正在读取整合包清单…
              </div>
            ) : (
              <button type="button" class="modpack-drop" onClick={() => void pick()}>
                <span class="modpack-drop__icon"><Icon name="box" size={28} /></span>
                <b>选择一个整合包</b>
                <span>.mrpack 或 CurseForge 导出的 .zip</span>
              </button>
            )
          ) : (
            <div class="pack-scan">
              <div class="pack-scan__bar">
                <div>
                  <b>本机实例</b>
                  <span>
                    {scanning
                      ? '正在扫描 Prism / HMCL / PCL / Modrinth App…'
                      : scan
                        ? `找到 ${scan.candidates.length} 个可导入项`
                        : '还没扫描'}
                  </span>
                </div>
                <button type="button" class="text-link" disabled={scanning} onClick={() => void runScan()}>
                  {scanning ? '扫描中…' : '重新扫描'}
                </button>
              </div>

              {scanning && !scan ? (
                <div class="modpack-loading"><span class="btn-spinner" /> 正在扫描本机实例…</div>
              ) : scan && scan.candidates.length === 0 ? (
                <div class="pack-empty">
                  <Icon name="info" size={16} />
                  <span>没找到其他启动器的实例。桌面上有 .mrpack 或整合包 zip 的话，用「选择文件」更快。</span>
                </div>
              ) : (
                <div class="pack-list">
                  {scan?.candidates.map((candidate) => (
                    <button
                      type="button"
                      class="pack-row"
                      key={candidate.id}
                      onClick={() => void selectCandidate(candidate)}
                    >
                      <LoaderTile loader={candidate.kind === 'archive' ? 'vanilla' : candidate.loader} size={34} />
                      <span class="pack-row__body">
                        <b>{candidate.name}</b>
                        <span>
                          {candidate.launcher}
                          {candidate.gameVersion ? ` · ${candidate.gameVersion}` : ''}
                          {candidate.loader !== 'vanilla' ? ` · ${loaderName(candidate.loader)}` : ''}
                          {candidate.loaderVersion ? ` ${candidate.loaderVersion}` : ''}
                        </span>
                      </span>
                      <span class="pack-row__meta">
                        {candidate.kind === 'archive'
                          ? formatBytes(candidate.sizeBytes)
                          : `${candidate.modCount} 个模组`}
                        {candidate.hasSaves ? <i>含存档</i> : null}
                      </span>
                      <Icon name="chevronRight" size={16} />
                    </button>
                  ))}
                </div>
              )}

              {scan && scan.roots.length > 0 ? (
                <p class="pack-scan__roots">扫描位置：{scan.roots.join(' · ')}</p>
              ) : null}
            </div>
          )}

          {error ? <div class="modpack-result modpack-result--bad">{error}</div> : null}
        </>
      ) : (
        <div class="modpack-preview">
          <div class="modpack-preview__head">
            <LoaderTile loader={preview.loader} size={52} />
            <div>
              <b>{preview.name}</b>
              <span>{preview.version || '未标注版本'}</span>
            </div>
            {!running && !done ? (
              <button
                type="button"
                class="text-link"
                onClick={() => {
                  setPreview(null);
                  setPending(null);
                  setMode('scan');
                }}
              >
                换一个
              </button>
            ) : null}
          </div>

          <div class="modpack-facts">
            <div><span>Minecraft</span><b>{preview.gameVersion || '未识别'}</b></div>
            <div><span>加载器</span><b>{loaderName(preview.loader)}{preview.loaderVersion ? ` ${preview.loaderVersion}` : ''}</b></div>
            <div><span>文件</span><b>{preview.fileCount > 0 ? `${preview.fileCount} 个` : '复制时统计'}</b></div>
            <div><span>体积</span><b>{preview.totalBytes > 0 ? formatBytes(preview.totalBytes) : '安装时计算'}</b></div>
          </div>

          {preview.summary ? <p class="modpack-summary">{preview.summary}</p> : null}
          {preview.warnings.length > 0 ? (
            <div class="modpack-warnings">
              {preview.warnings.map((warning) => (
                <div key={warning}><Icon name="info" size={13} /> {warning}</div>
              ))}
            </div>
          ) : null}

          {running ? (
            <div class="modpack-progress">
              <div class="modpack-progress__row">
                <span>{job?.progress?.stageLabel ?? '正在准备'}</span>
                <span class="mono">{formatPercent(pct)}</span>
              </div>
              <ProgressBar value={pct} indeterminate={!job?.progress || job.progress.totalBytes <= 0} />
              <div class="modpack-progress__meta">
                <span class="truncate">{job?.progress?.currentItem ?? '读取文件清单'}</span>
                <span class="mono">
                  {job?.progress ? `${formatBytes(job.progress.completedBytes)} / ${formatBytes(job.progress.totalBytes)}` : ''}
                </span>
              </div>
            </div>
          ) : null}

          {done ? (
            <div class="modpack-result modpack-result--ok">
              <Icon name="check" size={18} /> 整合包已安装完成。
            </div>
          ) : null}

          {failed ? (
            <div class="modpack-result modpack-result--bad">
              <Icon name="alert" size={18} /> {job?.message || '导入没有完成。'}
            </div>
          ) : null}

          {error ? <div class="modpack-result modpack-result--bad">{error}</div> : null}
        </div>
      )}
    </Sheet>
  );
}
