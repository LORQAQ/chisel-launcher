import { useEffect, useRef, useState } from 'preact/hooks';

function hash(value: string): number {
  let out = 2166136261;
  for (let i = 0; i < value.length; i++) {
    out ^= value.charCodeAt(i);
    out = Math.imul(out, 16777619);
  }
  return out >>> 0;
}

function rng(seed: number): () => number {
  let state = seed || 1;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return (state >>> 0) / 4294967296;
  };
}

const PALETTES = [
  { hue: 88, sat: 21, light: 39, leaf: 104, leafSat: 27, stone: 6 },
  { hue: 64, sat: 16, light: 41, leaf: 82, leafSat: 24, stone: 5 },
  { hue: 27, sat: 19, light: 42, leaf: 52, leafSat: 25, stone: 4 },
  { hue: 132, sat: 15, light: 36, leaf: 145, leafSat: 22, stone: 4 },
  { hue: 79, sat: 9, light: 43, leaf: 96, leafSat: 15, stone: 3 }
];

const hsl = (h: number, s: number, l: number) => `hsl(${h} ${s}% ${l}%)`;

export function Terrain({ seed, size = 72, class: className, label }: { seed: string | number; size?: number; class?: string; label?: string }) {
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const dpr = 2;
    canvas.width = Math.round(size * dpr);
    canvas.height = Math.round(size * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, size, size);
    drawTerrain(ctx, size, hash(String(seed)));
  }, [seed, size]);

  return (
    <canvas
      ref={ref}
      class={className}
      style={{ width: `${size}px`, height: `${size}px` }}
      role="img"
      aria-label={label ?? '一隅地形'}
    />
  );
}

function drawTerrain(ctx: CanvasRenderingContext2D, size: number, seed: number) {
  const random = rng(seed);
  const palette = PALETTES[seed % PALETTES.length];
  const n = 7;
  const tileW = (size * 0.84) / n;
  const tileH = tileW * 0.5;
  const blockH = Math.max(2.6, size * 0.052);
  const cx = size / 2;
  const cy = size * 0.46;
  const baseY = cy + (n - 1) * tileH + tileH / 2;
  const heights: number[][] = [];
  const present: boolean[][] = [];

  for (let x = 0; x < n; x++) {
    heights[x] = [];
    present[x] = [];
    for (let y = 0; y < n; y++) {
      const nx = (x - (n - 1) / 2) / ((n - 1) / 2);
      const ny = (y - (n - 1) / 2) / ((n - 1) / 2);
      const distance = Math.sqrt(nx * nx + ny * ny);
      const island = 1 - distance * 0.92;
      const ridge = Math.sin((x + seed % 7) * 0.9) * 0.35 + Math.cos((y + seed % 5) * 0.72) * 0.3;
      const noise = random() * 1.4;
      const height = Math.round(island * 2.7 + ridge + noise - 0.55);
      present[x][y] = island > 0.16 || random() > 0.55;
      heights[x][y] = Math.max(0, Math.min(4, height));
    }
  }

  const cells: Array<{ x: number; y: number; depth: number; order: number }> = [];
  let order = 0;
  for (let x = 0; x < n; x++) {
    for (let y = 0; y < n; y++) {
      if (!present[x][y]) continue;
      cells.push({ x, y, depth: heights[x][y], order: order++ });
    }
  }
  cells.sort((a, b) => a.x + a.y - (b.x + b.y) || a.y - b.y);

  const shadow = ctx.createRadialGradient(cx, baseY - size * 0.06, 2, cx, baseY - size * 0.06, size * 0.34);
  shadow.addColorStop(0, 'rgba(8, 9, 4, 0.28)');
  shadow.addColorStop(1, 'rgba(8, 9, 4, 0)');
  ctx.fillStyle = shadow;
  ctx.beginPath();
  ctx.ellipse(cx, baseY - size * 0.04, size * 0.34, size * 0.1, 0, 0, Math.PI * 2);
  ctx.fill();

  const treeChance = 0.16 + (seed % 5) * 0.012;

  for (const cell of cells) {
    const height = cell.depth;
    const px = cx + (cell.x - cell.y) * (tileW / 2);
    const py = cy + (cell.x + cell.y) * (tileH / 2) - height * blockH;
    const top = py - tileH / 2;
    const bottom = py + tileH / 2;
    const left = px - tileW / 2;
    const right = px + tileW / 2;

    const isStone = height === 0 && random() > 0.68;
    const lightness = palette.light + (random() * 8 - 4) + height * 1.6;
    const topColor = isStone
      ? hsl(palette.hue, palette.stone, 38 + height * 1.4)
      : hsl(palette.hue + random() * 6 - 3, palette.sat + 4, lightness);
    const leftColor = hsl(palette.hue, palette.sat + 2, Math.max(18, lightness - 12));
    const rightColor = hsl(palette.hue, palette.sat, Math.max(13, lightness - 19));

    ctx.fillStyle = leftColor;
    ctx.beginPath();
    ctx.moveTo(left, py);
    ctx.lineTo(px, bottom);
    ctx.lineTo(px, baseY);
    ctx.lineTo(left, baseY - tileH / 2);
    ctx.closePath();
    ctx.fill();

    ctx.fillStyle = rightColor;
    ctx.beginPath();
    ctx.moveTo(px, bottom);
    ctx.lineTo(right, py);
    ctx.lineTo(right, baseY - tileH / 2);
    ctx.lineTo(px, baseY);
    ctx.closePath();
    ctx.fill();

    ctx.strokeStyle = 'rgba(10, 11, 6, 0.12)';
    ctx.lineWidth = 0.5;
    ctx.beginPath();
    ctx.moveTo(px, bottom);
    ctx.lineTo(px, baseY);
    ctx.stroke();

    ctx.fillStyle = topColor;
    ctx.beginPath();
    ctx.moveTo(px, top);
    ctx.lineTo(right, py);
    ctx.lineTo(px, bottom);
    ctx.lineTo(left, py);
    ctx.closePath();
    ctx.fill();
    ctx.strokeStyle = 'rgba(10, 11, 6, 0.16)';
    ctx.stroke();

    ctx.fillStyle = 'rgba(255, 255, 240, 0.055)';
    ctx.beginPath();
    ctx.moveTo(px, top);
    ctx.lineTo(right, py);
    ctx.lineTo(px, py + tileH / 2);
    ctx.lineTo(left, py);
    ctx.closePath();
    ctx.fill();

    if (height > 0 && random() < 0.22) {
      ctx.fillStyle = 'rgba(193, 138, 91, 0.55)';
      const ox = px + random() * tileW * 0.4 - tileW * 0.2;
      const oy = py + random() * tileH * 0.25 - tileH * 0.1;
      ctx.fillRect(ox, oy, 1, 1);
    } else if (random() < 0.1) {
      ctx.fillStyle = `hsl(${palette.leaf} 32% 62%)`;
      ctx.fillRect(px + tileW * 0.15, py + tileH * 0.08, 1, 1);
    }

    if (height >= 1 && random() < treeChance) {
      drawTree(ctx, px, py - tileH * 0.14, tileW, palette);
    }
  }
}

function drawTree(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  tileW: number,
  palette: (typeof PALETTES)[number]
) {
  const trunkHeight = Math.max(3, tileW * 0.58);
  ctx.fillStyle = 'hsl(28 24% 24%)';
  ctx.fillRect(x - 0.7, y - trunkHeight, 1.4, trunkHeight);
  ctx.fillStyle = 'hsl(28 20% 32%)';
  ctx.fillRect(x - 0.7, y - trunkHeight, 0.6, trunkHeight);

  const leafTop = hsl(palette.leaf, palette.leafSat, 33);
  const leafLeft = hsl(palette.leaf, palette.leafSat + 2, 23);
  const leafRight = hsl(palette.leaf, palette.leafSat - 2, 17);
  const unit = Math.max(1.6, tileW * 0.34);

  const cubes = [
    { ox: 0, oy: 0, u: unit * 1.2 },
    { ox: -unit * 1.05, oy: -unit * 0.42, u: unit * 0.86 },
    { ox: unit * 1.02, oy: -unit * 0.3, u: unit * 0.8 }
  ];

  for (const cube of cubes) {
    const bx = x + cube.ox;
    const by = y - trunkHeight + cube.oy - cube.u * 0.2;
    const u = cube.u;
    ctx.fillStyle = leafLeft;
    ctx.beginPath();
    ctx.moveTo(bx - u, by);
    ctx.lineTo(bx, by + u * 0.5);
    ctx.lineTo(bx, by + u * 0.5 + u * 0.8);
    ctx.lineTo(bx - u, by + u * 0.8);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = leafRight;
    ctx.beginPath();
    ctx.moveTo(bx, by + u * 0.5);
    ctx.lineTo(bx + u, by);
    ctx.lineTo(bx + u, by + u * 0.8);
    ctx.lineTo(bx, by + u * 0.5 + u * 0.8);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = leafTop;
    ctx.beginPath();
    ctx.moveTo(bx, by - u * 0.5);
    ctx.lineTo(bx + u, by);
    ctx.lineTo(bx, by + u * 0.5);
    ctx.lineTo(bx - u, by);
    ctx.closePath();
    ctx.fill();
  }
}

export function IsoBlock({
  accent,
  texture,
  name,
  size = 96,
  seed = accent
}: {
  accent: string;
  texture?: string | null;
  name: string;
  size?: number;
  seed?: string | number;
}) {
  const ref = useRef<HTMLCanvasElement>(null);
  const [image, setImage] = useState<HTMLImageElement | null>(null);

  useEffect(() => {
    if (!texture) {
      setImage(null);
      return;
    }
    let alive = true;
    const next = new Image();
    next.onload = () => {
      if (alive) setImage(next);
    };
    next.onerror = () => {
      if (alive) setImage(null);
    };
    next.src = texture;
    return () => {
      alive = false;
      next.onload = null;
    };
  }, [texture]);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const dpr = 2;
    const height = Math.round(size * 0.8);
    canvas.width = Math.round(size * dpr);
    canvas.height = height * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, size, height);

    const textureCanvas = makeTexture(accent, hash(String(seed)), image);
    drawIsoBlock(ctx, size, height, textureCanvas);
  }, [accent, seed, image, size]);

  return (
    <canvas
      ref={ref}
      style={{ width: `${size}px`, height: `${Math.round(size * 0.8)}px` }}
      role="img"
      aria-label={name}
    />
  );
}

function makeTexture(accent: string, seed: number, image: HTMLImageElement | null): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = 16;
  canvas.height = 16;
  const ctx = canvas.getContext('2d')!;
  ctx.imageSmoothingEnabled = false;
  if (image) {
    ctx.drawImage(image, 0, 0, image.naturalWidth, image.naturalHeight, 0, 0, 16, 16);
    return canvas;
  }

  const rgb = parseColor(accent);
  const random = rng(seed);
  for (let y = 0; y < 16; y++) {
    for (let x = 0; x < 16; x++) {
      const grain = 0.82 + random() * 0.32;
      const blockEdge = x === 0 || y === 0 || x === 15 || y === 15 ? 0.88 : 1;
      const patina = random() > 0.86 ? 1.12 : 1;
      ctx.fillStyle = `rgb(${Math.round(rgb[0] * grain * blockEdge * patina)}, ${Math.round(rgb[1] * grain * blockEdge * patina)}, ${Math.round(rgb[2] * grain * blockEdge * patina)})`;
      ctx.fillRect(x, y, 1, 1);
    }
  }
  for (let i = 0; i < 14; i++) {
    const x = Math.floor(random() * 16);
    const y = Math.floor(random() * 16);
    ctx.fillStyle = `rgba(20, 18, 12, ${0.1 + random() * 0.18})`;
    ctx.fillRect(x, y, 1 + Math.floor(random() * 2), 1);
  }
  for (let i = 0; i < 6; i++) {
    ctx.fillStyle = `rgba(244, 233, 210, ${0.06 + random() * 0.08})`;
    ctx.fillRect(Math.floor(random() * 16), Math.floor(random() * 16), 1, 1);
  }
  return canvas;
}

function drawIsoBlock(ctx: CanvasRenderingContext2D, width: number, height: number, texture: HTMLCanvasElement) {
  const u = width * 0.28;
  const sideH = width * 0.42;
  const cx = width / 2;
  const topY = width * 0.055;
  const nx = cx;
  const ny = topY;
  const ex = cx + u;
  const ey = topY + u / 2;
  const sx = cx;
  const sy = topY + u;
  const wx = cx - u;
  const wy = topY + u / 2;
  const bottom = sy + sideH;

  ctx.save();
  ctx.translate(cx, bottom + 3);
  ctx.scale(1, 0.22);
  const shadow = ctx.createRadialGradient(0, 0, 1, 0, 0, width * 0.46);
  shadow.addColorStop(0, 'rgba(12, 13, 8, 0.3)');
  shadow.addColorStop(1, 'rgba(12, 13, 8, 0)');
  ctx.fillStyle = shadow;
  ctx.beginPath();
  ctx.arc(0, 0, width * 0.46, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();

  const unit = u / 16;
  const half = unit / 2;

  ctx.save();
  ctx.transform(unit, half, -unit, half, nx, ny);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(texture, 0, 0);
  ctx.restore();

  ctx.save();
  ctx.transform(unit, half, 0, sideH / 16, wx, wy);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(texture, 0, 0);
  ctx.fillStyle = 'rgba(10, 9, 5, 0.26)';
  ctx.fillRect(0, 0, 16, 16);
  ctx.restore();

  ctx.save();
  ctx.transform(unit, -half, 0, sideH / 16, sx, sy);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(texture, 0, 0);
  ctx.fillStyle = 'rgba(10, 9, 5, 0.45)';
  ctx.fillRect(0, 0, 16, 16);
  ctx.restore();

  ctx.strokeStyle = 'rgba(8, 8, 4, 0.22)';
  ctx.lineWidth = 0.7;
  ctx.beginPath();
  ctx.moveTo(nx, ny);
  ctx.lineTo(ex, ey);
  ctx.lineTo(sx, sy);
  ctx.lineTo(wx, wy);
  ctx.closePath();
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(wx, wy);
  ctx.lineTo(sx, sy);
  ctx.lineTo(sx, bottom);
  ctx.lineTo(wx, wy + sideH);
  ctx.closePath();
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(sx, sy);
  ctx.lineTo(ex, ey);
  ctx.lineTo(ex, ey + sideH);
  ctx.lineTo(sx, bottom);
  ctx.closePath();
  ctx.stroke();
}

function parseColor(value: string): [number, number, number] {
  const match = /^#?([\da-f]{6})$/i.exec(value.trim());
  if (!match) return [176, 111, 81];
  const number = parseInt(match[1], 16);
  return [(number >> 16) & 255, (number >> 8) & 255, number & 255];
}
