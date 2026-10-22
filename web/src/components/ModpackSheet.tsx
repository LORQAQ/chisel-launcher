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
import type { ModpackPreview } from '../lib/types';
import { Icon } from './Icon';
import { LoaderTile } from './LoaderTile';
import { Button, ProgressBar, Sheet } from './ui';

export function ModpackSheet() {
  const open = modpackSheetOpen.value;
  const job = installJob.value;
  const [preview, setPreview] = useState<ModpackPreview | null>(null);
  const [picking, setPicking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setPreview(null);
    setError(null);
    setPicking(false);
  }, [open]);

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
    const jobId = await startInstallation({
      gameVersion: preview.gameVersion,
      loader: preview.loader,
      loaderVersion: preview.loaderVersion,
      displayName: preview.name,
      modpackPath: preview.path
    });
    if (!jobId) setError('安装没有启动，请检查日志。');
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
      subtitle="支持 Modrinth .mrpack 与 CurseForge 导出的 .zip"
      titleId="modpack-sheet-title"
      width="normal"
      footerNote={preview ? `目标目录：${preview.gameVersion} · ${loaderName(preview.loader)}` : '压缩包不会被修改。'}
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
      {!preview && !picking ? (
        <button type="button" class="modpack-drop" onClick={() => void pick()}>
          <span class="modpack-drop__icon"><Icon name="box" size={28} /></span>
          <b>选择一个整合包</b>
          <span>.mrpack 或 CurseForge 导出的 .zip</span>
        </button>
      ) : picking ? (
        <div class="modpack-loading">
          <span class="btn-spinner" /> 正在读取整合包清单…
        </div>
      ) : (
        <div class="modpack-preview">
          <div class="modpack-preview__head">
            <LoaderTile loader={preview!.loader} size={52} />
            <div>
              <b>{preview!.name}</b>
              <span>{preview!.version || '未标注版本'}</span>
            </div>
            <button type="button" class="text-link" onClick={() => void pick()}>重新选择</button>
          </div>

          <div class="modpack-facts">
            <div><span>Minecraft</span><b>{preview!.gameVersion}</b></div>
            <div><span>加载器</span><b>{loaderName(preview!.loader)}{preview!.loaderVersion ? ` ${preview!.loaderVersion}` : ''}</b></div>
            <div><span>文件</span><b>{preview!.fileCount} 个</b></div>
            <div><span>体积</span><b>{preview!.totalBytes > 0 ? formatBytes(preview!.totalBytes) : '安装时计算'}</b></div>
          </div>

          {preview!.summary ? <p class="modpack-summary">{preview!.summary}</p> : null}
          {preview!.warnings.length > 0 ? (
            <div class="modpack-warnings">
              {preview!.warnings.map((warning) => (
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
