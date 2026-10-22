import { useEffect, useRef, useState } from 'preact/hooks';
import { loadSkin, skinRevision } from '../lib/skins';
import type { AccountView } from '../lib/types';

export function useSkin(account: AccountView | null): string | null {
  const [dataUrl, setDataUrl] = useState<string | null>(null);
  const revision = skinRevision.value;
  useEffect(() => {
    let alive = true;
    if (!account?.hasSkin) {
      setDataUrl(null);
      return;
    }
    void loadSkin(account).then((value) => {
      if (alive) setDataUrl(value);
    });
    return () => {
      alive = false;
    };
  }, [account?.id, account?.hasSkin, revision]);
  return dataUrl;
}

function hashString(value: string): number {
  let hash = 2166136261;
  for (let i = 0; i < value.length; i++) {
    hash ^= value.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}

export function SkinFace({ account, class: className }: { account: AccountView | null; class?: string }) {
  const dataUrl = useSkin(account);
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const seed = hashString(account?.uuid || account?.name || 'chisel');
    if (!dataUrl) {
      drawFallbackFace(ctx, seed);
      return;
    }
    let alive = true;
    const image = new Image();
    image.onload = () => {
      if (!alive) return;
      ctx.imageSmoothingEnabled = false;
      ctx.clearRect(0, 0, 8, 8);
      drawPart(ctx, image, 8, 8, 8, 8, 0, 0, 8, 8);
      drawPart(ctx, image, 40, 8, 8, 8, 0, 0, 8, 8);
    };
    image.src = dataUrl;
    return () => {
      alive = false;
      image.onload = null;
    };
  }, [dataUrl, account?.uuid, account?.name]);

  return <canvas ref={ref} width={8} height={8} class={className} aria-hidden="true" />;
}

export function SkinViewer({ dataUrl, label }: { dataUrl: string | null; label: string }) {
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    if (!dataUrl) {
      drawFallbackFigure(ctx);
      return;
    }
    let alive = true;
    const image = new Image();
    image.onload = () => {
      if (!alive) return;
      ctx.imageSmoothingEnabled = false;
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      // head, body, arms and legs, then the hat / jacket layers
      drawPart(ctx, image, 8, 8, 8, 8, 4, 0, 8, 8);
      drawPart(ctx, image, 20, 20, 8, 12, 4, 8, 8, 12);
      drawPart(ctx, image, 44, 20, 4, 12, 0, 8, 4, 12);
      drawPart(ctx, image, 36, 52, 4, 12, 12, 8, 4, 12);
      drawPart(ctx, image, 4, 20, 4, 12, 4, 20, 4, 12);
      drawPart(ctx, image, 20, 52, 4, 12, 8, 20, 4, 12);
      drawPart(ctx, image, 40, 8, 8, 8, 4, 0, 8, 8);
      drawPart(ctx, image, 20, 36, 8, 12, 4, 8, 8, 12);
      drawPart(ctx, image, 44, 36, 4, 12, 12, 8, 4, 12);
      drawPart(ctx, image, 52, 52, 4, 12, 0, 8, 4, 12);
      drawPart(ctx, image, 4, 36, 4, 12, 4, 20, 4, 12);
      drawPart(ctx, image, 4, 52, 4, 12, 8, 20, 4, 12);
    };
    image.src = dataUrl;
    return () => {
      alive = false;
      image.onload = null;
    };
  }, [dataUrl]);

  return <canvas ref={ref} width={16} height={32} class="skin-viewer__canvas" role="img" aria-label={label} />;
}

function drawPart(
  ctx: CanvasRenderingContext2D,
  image: HTMLImageElement,
  sx: number,
  sy: number,
  sw: number,
  sh: number,
  dx: number,
  dy: number,
  dw: number,
  dh: number
) {
  if (sx < 0 || sy < 0 || sx + sw > image.naturalWidth || sy + sh > image.naturalHeight) return;
  ctx.drawImage(image, sx, sy, sw, sh, dx, dy, dw, dh);
}

function drawFallbackFace(ctx: CanvasRenderingContext2D, seed: number) {
  const hair = seed % 3 === 0 ? '#3b362b' : seed % 3 === 1 ? '#4a3c2c' : '#2d3126';
  ctx.fillStyle = '#211f18';
  ctx.fillRect(0, 0, 8, 8);
  ctx.fillStyle = hair;
  ctx.fillRect(1, 0, 6, 3);
  ctx.fillRect(0, 1, 8, 2);
  ctx.fillStyle = '#b39772';
  ctx.fillRect(1, 3, 6, 5);
  ctx.fillStyle = '#9a7f5d';
  ctx.fillRect(0, 4, 1, 3);
  ctx.fillRect(7, 4, 1, 3);
  ctx.fillStyle = '#2a2c22';
  ctx.fillRect(2, 4, 1, 1);
  ctx.fillRect(5, 4, 1, 1);
  ctx.fillStyle = '#7d654b';
  ctx.fillRect(3, 6, 2, 1);
  if (seed % 4 === 0) {
    ctx.fillStyle = '#84966d';
    ctx.fillRect(1, 3, 6, 1);
  }
}

function drawFallbackFigure(ctx: CanvasRenderingContext2D) {
  ctx.clearRect(0, 0, 16, 32);
  ctx.fillStyle = '#b39772';
  ctx.fillRect(4, 0, 8, 8);
  ctx.fillStyle = '#3b362b';
  ctx.fillRect(4, 0, 8, 3);
  ctx.fillRect(3, 1, 2, 2);
  ctx.fillRect(11, 1, 2, 2);
  ctx.fillStyle = '#2a2c22';
  ctx.fillRect(6, 4, 1, 1);
  ctx.fillRect(9, 4, 1, 1);
  ctx.fillStyle = '#758661';
  ctx.fillRect(4, 8, 8, 12);
  ctx.fillStyle = '#657654';
  ctx.fillRect(4, 8, 2, 12);
  ctx.fillStyle = '#4b5140';
  ctx.fillRect(0, 8, 4, 12);
  ctx.fillRect(12, 8, 4, 12);
  ctx.fillStyle = '#b39772';
  ctx.fillRect(0, 18, 4, 2);
  ctx.fillRect(12, 18, 4, 2);
  ctx.fillStyle = '#474337';
  ctx.fillRect(4, 20, 4, 10);
  ctx.fillRect(8, 20, 4, 10);
  ctx.fillStyle = '#292820';
  ctx.fillRect(4, 29, 4, 2);
  ctx.fillRect(8, 29, 4, 2);
}
