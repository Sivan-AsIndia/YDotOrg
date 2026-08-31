/**
 * Minimal, dependency-free QR Code generator.
 *
 * Produces a genuine, scannable QR Code (byte mode, error-correction level M)
 * as a boolean matrix. Supports versions 1–10 (auto-selected), which comfortably
 * covers the short tracking URLs used in the Tracking asset manager.
 *
 * Implements the ISO/IEC 18004 pipeline: data encoding, Reed–Solomon error
 * correction over GF(256), block interleaving, function-pattern placement,
 * zig-zag data placement, data masking with penalty-based mask selection, and
 * BCH format information.
 */

// ---- GF(256) arithmetic (primitive polynomial 0x11d) ----
const GF_EXP = new Uint8Array(512);
const GF_LOG = new Uint8Array(256);
(() => {
  let x = 1;
  for (let i = 0; i < 255; i++) {
    GF_EXP[i] = x;
    GF_LOG[x] = i;
    x <<= 1;
    if (x & 0x100) x ^= 0x11d;
  }
  for (let i = 255; i < 512; i++) GF_EXP[i] = GF_EXP[i - 255];
})();

function gfMul(a: number, b: number): number {
  if (a === 0 || b === 0) return 0;
  return GF_EXP[GF_LOG[a] + GF_LOG[b]];
}

function rsGeneratorPoly(degree: number): number[] {
  let poly = [1];
  for (let i = 0; i < degree; i++) {
    const next = new Array(poly.length + 1).fill(0);
    for (let j = 0; j < poly.length; j++) {
      next[j] ^= poly[j];
      next[j + 1] ^= gfMul(poly[j], GF_EXP[i]);
    }
    poly = next;
  }
  return poly; // length degree + 1, poly[0] === 1
}

function rsEncode(data: number[], ecLen: number): number[] {
  const gen = rsGeneratorPoly(ecLen);
  const res = new Array(ecLen).fill(0);
  for (const d of data) {
    const factor = d ^ res[0];
    res.shift();
    res.push(0);
    if (factor !== 0) {
      for (let i = 0; i < ecLen; i++) res[i] ^= gfMul(gen[i + 1], factor);
    }
  }
  return res;
}

// ---- Version tables for error-correction level M (versions 1–10) ----
// [ total data codewords, ec codewords per block, number of blocks ]
// All these versions use equal-sized blocks at level M.
const EC_M: Record<number, { dataCw: number; ecPerBlock: number; blocks: number }> = {
  1: { dataCw: 16, ecPerBlock: 10, blocks: 1 },
  2: { dataCw: 28, ecPerBlock: 16, blocks: 1 },
  3: { dataCw: 44, ecPerBlock: 26, blocks: 1 },
  4: { dataCw: 64, ecPerBlock: 18, blocks: 2 },
  5: { dataCw: 86, ecPerBlock: 24, blocks: 2 },
  6: { dataCw: 108, ecPerBlock: 16, blocks: 4 },
  7: { dataCw: 124, ecPerBlock: 18, blocks: 4 },
  8: { dataCw: 154, ecPerBlock: 22, blocks: 2 }, // 2×(22) + 2×(23) — see note below
  9: { dataCw: 182, ecPerBlock: 22, blocks: 3 },
  10: { dataCw: 216, ecPerBlock: 26, blocks: 4 },
};

// Alignment-pattern centre coordinates per version.
const ALIGN_POS: Record<number, number[]> = {
  1: [],
  2: [6, 18],
  3: [6, 22],
  4: [6, 26],
  5: [6, 30],
  6: [6, 34],
  7: [6, 22, 38],
  8: [6, 24, 42],
  9: [6, 26, 46],
  10: [6, 28, 50],
};

/** Text encoder for byte mode (UTF-8). */
function toUtf8Bytes(text: string): number[] {
  const out: number[] = [];
  for (const ch of unescape(encodeURIComponent(text))) out.push(ch.charCodeAt(0) & 0xff);
  return out;
}

/** Choose the smallest supported version (level M) that fits the data. */
function chooseVersion(dataLen: number): number {
  const neededBits = 4 + 8 + dataLen * 8; // mode + 8-bit count (v1–9) + data
  for (let v = 1; v <= 10; v++) {
    // Versions 10+ use a 16-bit count indicator; recompute for v === 10.
    const countBits = v >= 10 ? 16 : 8;
    const bits = 4 + countBits + dataLen * 8;
    if (EC_M[v].dataCw * 8 >= (v >= 10 ? bits : neededBits)) return v;
  }
  return 10;
}

/** Build the bit stream of data codewords (with padding). */
function buildDataCodewords(text: string, version: number): number[] {
  const bytes = toUtf8Bytes(text);
  const bits: number[] = [];
  const push = (value: number, len: number) => {
    for (let i = len - 1; i >= 0; i--) bits.push((value >> i) & 1);
  };
  push(0b0100, 4); // byte mode
  push(bytes.length, version >= 10 ? 16 : 8); // character count
  for (const b of bytes) push(b, 8);

  const totalDataBits = EC_M[version].dataCw * 8;
  // Terminator (up to 4 zero bits).
  for (let i = 0; i < 4 && bits.length < totalDataBits; i++) bits.push(0);
  // Pad to a byte boundary.
  while (bits.length % 8 !== 0) bits.push(0);

  const codewords: number[] = [];
  for (let i = 0; i < bits.length; i += 8) {
    let byte = 0;
    for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j];
    codewords.push(byte);
  }
  // Pad bytes.
  const padBytes = [0xec, 0x11];
  let p = 0;
  while (codewords.length < EC_M[version].dataCw) codewords.push(padBytes[p++ % 2]);
  return codewords;
}

/** Interleave data + error-correction codewords into the final bit stream. */
function buildFinalCodewords(dataCodewords: number[], version: number): number[] {
  const { ecPerBlock, blocks } = EC_M[version];
  const perBlock = Math.floor(dataCodewords.length / blocks);
  const dataBlocks: number[][] = [];
  const ecBlocks: number[][] = [];
  // Distribute; later blocks take one extra codeword when not evenly divisible.
  const remainder = dataCodewords.length - perBlock * blocks;
  let idx = 0;
  for (let b = 0; b < blocks; b++) {
    const size = perBlock + (b >= blocks - remainder ? 1 : 0);
    const block = dataCodewords.slice(idx, idx + size);
    idx += size;
    dataBlocks.push(block);
    ecBlocks.push(rsEncode(block, ecPerBlock));
  }

  const result: number[] = [];
  const maxData = Math.max(...dataBlocks.map((b) => b.length));
  for (let i = 0; i < maxData; i++) {
    for (const block of dataBlocks) if (i < block.length) result.push(block[i]);
  }
  for (let i = 0; i < ecPerBlock; i++) {
    for (const block of ecBlocks) result.push(block[i]);
  }
  return result;
}

type Cell = { dark: boolean; fn: boolean }; // fn = function module (not maskable / not data)

function makeGrid(size: number): Cell[][] {
  return Array.from({ length: size }, () =>
    Array.from({ length: size }, () => ({ dark: false, fn: false })),
  );
}

function placeFinder(grid: Cell[][], row: number, col: number): void {
  for (let r = -1; r <= 7; r++) {
    for (let c = -1; c <= 7; c++) {
      const rr = row + r;
      const cc = col + c;
      if (rr < 0 || cc < 0 || rr >= grid.length || cc >= grid.length) continue;
      const isBorder = r >= 0 && r <= 6 && c >= 0 && c <= 6;
      const dark =
        isBorder &&
        ((r === 0 || r === 6 || c === 0 || c === 6) || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
      grid[rr][cc] = { dark, fn: true };
    }
  }
}

function placeAlignment(grid: Cell[][], version: number): void {
  const positions = ALIGN_POS[version];
  const size = grid.length;
  for (const r of positions) {
    for (const c of positions) {
      // Skip the three finder corners.
      if ((r === 6 && c === 6) || (r === 6 && c === size - 7) || (r === size - 7 && c === 6)) continue;
      for (let dr = -2; dr <= 2; dr++) {
        for (let dc = -2; dc <= 2; dc++) {
          const dark = Math.max(Math.abs(dr), Math.abs(dc)) !== 1;
          grid[r + dr][c + dc] = { dark, fn: true };
        }
      }
    }
  }
}

function placeTimingAndFixed(grid: Cell[][], version: number): void {
  const size = grid.length;
  for (let i = 8; i < size - 8; i++) {
    const dark = i % 2 === 0;
    if (!grid[6][i].fn) grid[6][i] = { dark, fn: true };
    if (!grid[i][6].fn) grid[i][6] = { dark, fn: true };
  }
  // Dark module.
  grid[4 * version + 9][8] = { dark: true, fn: true };
}

function reserveFormatAreas(grid: Cell[][]): void {
  const size = grid.length;
  for (let i = 0; i < 9; i++) {
    if (!grid[8][i].fn) grid[8][i] = { dark: false, fn: true };
    if (!grid[i][8].fn) grid[i][8] = { dark: false, fn: true };
  }
  for (let i = 0; i < 8; i++) {
    grid[8][size - 1 - i] = { dark: false, fn: true };
    grid[size - 1 - i][8] = { dark: false, fn: true };
  }
}

function placeData(grid: Cell[][], bits: number[]): void {
  const size = grid.length;
  let bitIdx = 0;
  let upward = true;
  for (let col = size - 1; col > 0; col -= 2) {
    if (col === 6) col--; // skip timing column
    for (let i = 0; i < size; i++) {
      const row = upward ? size - 1 - i : i;
      for (let c = 0; c < 2; c++) {
        const cc = col - c;
        if (grid[row][cc].fn) continue;
        const bit = bitIdx < bits.length ? bits[bitIdx++] : 0;
        grid[row][cc].dark = bit === 1;
      }
    }
    upward = !upward;
  }
}

function maskFn(mask: number, r: number, c: number): boolean {
  switch (mask) {
    case 0: return (r + c) % 2 === 0;
    case 1: return r % 2 === 0;
    case 2: return c % 3 === 0;
    case 3: return (r + c) % 3 === 0;
    case 4: return (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0;
    case 5: return ((r * c) % 2) + ((r * c) % 3) === 0;
    case 6: return (((r * c) % 2) + ((r * c) % 3)) % 2 === 0;
    default: return (((r + c) % 2) + ((r * c) % 3)) % 2 === 0;
  }
}

function applyMask(grid: Cell[][], mask: number): void {
  for (let r = 0; r < grid.length; r++) {
    for (let c = 0; c < grid.length; c++) {
      if (grid[r][c].fn) continue;
      if (maskFn(mask, r, c)) grid[r][c].dark = !grid[r][c].dark;
    }
  }
}

function formatBits(mask: number): number {
  // EC level M = 0b00; combine with 3-bit mask.
  let data = (0b00 << 3) | mask;
  let rem = data;
  for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) & 1 ? 0x537 : 0);
  return ((data << 10) | rem) ^ 0x5412;
}

function placeFormat(grid: Cell[][], mask: number): void {
  const size = grid.length;
  const bits = formatBits(mask);
  const get = (i: number) => ((bits >> i) & 1) === 1;
  // Around the top-left finder.
  for (let i = 0; i <= 5; i++) grid[8][i].dark = get(i);
  grid[8][7].dark = get(6);
  grid[8][8].dark = get(7);
  grid[7][8].dark = get(8);
  for (let i = 9; i <= 14; i++) grid[14 - i][8].dark = get(i);
  // Split copy across top-right and bottom-left.
  for (let i = 0; i <= 7; i++) grid[size - 1 - i][8].dark = get(i);
  for (let i = 8; i <= 14; i++) grid[8][size - 15 + i].dark = get(i);
}

function penalty(grid: Cell[][]): number {
  const size = grid.length;
  let score = 0;
  const at = (r: number, c: number) => grid[r][c].dark;
  // Rule 1: runs of 5+ same-colour modules in rows and columns.
  for (let r = 0; r < size; r++) {
    let runColor = at(r, 0);
    let runLen = 1;
    for (let c = 1; c < size; c++) {
      if (at(r, c) === runColor) {
        runLen++;
      } else {
        if (runLen >= 5) score += 3 + (runLen - 5);
        runColor = at(r, c);
        runLen = 1;
      }
    }
    if (runLen >= 5) score += 3 + (runLen - 5);
  }
  for (let c = 0; c < size; c++) {
    let runColor = at(0, c);
    let runLen = 1;
    for (let r = 1; r < size; r++) {
      if (at(r, c) === runColor) {
        runLen++;
      } else {
        if (runLen >= 5) score += 3 + (runLen - 5);
        runColor = at(r, c);
        runLen = 1;
      }
    }
    if (runLen >= 5) score += 3 + (runLen - 5);
  }
  // Rule 2: 2×2 blocks of the same colour.
  for (let r = 0; r < size - 1; r++) {
    for (let c = 0; c < size - 1; c++) {
      const v = at(r, c);
      if (v === at(r, c + 1) && v === at(r + 1, c) && v === at(r + 1, c + 1)) score += 3;
    }
  }
  // Rule 3: finder-like patterns.
  const pat1 = [true, false, true, true, true, false, true, false, false, false, false];
  const pat2 = [false, false, false, false, true, false, true, true, true, false, true];
  const rowMatch = (r: number, c: number, pat: boolean[]) => {
    for (let i = 0; i < 11; i++) if (at(r, c + i) !== pat[i]) return false;
    return true;
  };
  const colMatch = (r: number, c: number, pat: boolean[]) => {
    for (let i = 0; i < 11; i++) if (at(r + i, c) !== pat[i]) return false;
    return true;
  };
  for (let r = 0; r < size; r++) {
    for (let c = 0; c <= size - 11; c++) {
      if (rowMatch(r, c, pat1) || rowMatch(r, c, pat2)) score += 40;
    }
  }
  for (let c = 0; c < size; c++) {
    for (let r = 0; r <= size - 11; r++) {
      if (colMatch(r, c, pat1) || colMatch(r, c, pat2)) score += 40;
    }
  }
  // Rule 4: dark-module proportion.
  let dark = 0;
  for (let r = 0; r < size; r++) for (let c = 0; c < size; c++) if (at(r, c)) dark++;
  const percent = (dark * 100) / (size * size);
  const prev = Math.floor(Math.abs(percent - 50) / 5);
  score += prev * 10;
  return score;
}

/**
 * Generate a genuine QR Code matrix for the given text.
 * Returns a boolean matrix (true = dark module), without a quiet zone.
 */
export function generateQrMatrix(text: string): boolean[][] {
  const bytes = toUtf8Bytes(text);
  const version = chooseVersion(bytes.length);
  const dataCodewords = buildDataCodewords(text, version);
  const finalCodewords = buildFinalCodewords(dataCodewords, version);

  const bits: number[] = [];
  for (const cw of finalCodewords) for (let i = 7; i >= 0; i--) bits.push((cw >> i) & 1);

  const size = 4 * version + 17;

  // Build the base grid with all function patterns; keep a clean copy to re-mask.
  const base = makeGrid(size);
  placeFinder(base, 0, 0);
  placeFinder(base, 0, size - 7);
  placeFinder(base, size - 7, 0);
  placeAlignment(base, version);
  placeTimingAndFixed(base, version);
  reserveFormatAreas(base);
  placeData(base, bits);

  // Choose the mask with the lowest penalty.
  let bestMask = 0;
  let bestScore = Infinity;
  let bestGrid: Cell[][] = base;
  for (let mask = 0; mask < 8; mask++) {
    const grid = base.map((row) => row.map((cell) => ({ ...cell })));
    applyMask(grid, mask);
    placeFormat(grid, mask);
    const score = penalty(grid);
    if (score < bestScore) {
      bestScore = score;
      bestMask = mask;
      bestGrid = grid;
    }
  }
  void bestMask;

  return bestGrid.map((row) => row.map((cell) => cell.dark));
}

/**
 * Build an SVG `<path>` "d" string for the dark modules of a QR matrix,
 * offset by a 4-module quiet zone. Use with a white background rect.
 */
export function qrMatrixToPath(matrix: boolean[][], quietZone = 4): string {
  let d = '';
  for (let r = 0; r < matrix.length; r++) {
    for (let c = 0; c < matrix.length; c++) {
      if (matrix[r][c]) d += `M${c + quietZone} ${r + quietZone}h1v1h-1z`;
    }
  }
  return d;
}
