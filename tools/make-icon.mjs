// Generates src/RudolphTech/Assets/rudolph.ico from scratch: a magnifying glass (the "relevamiento")
// over a dark rounded square, drawn with plain math so the repository carries no third party artwork.
// Run with: node tools/make-icon.mjs
import { deflateSync } from "node:zlib";
import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const SIZES = [16, 24, 32, 48, 64, 128, 256];

const BACKGROUND = [24, 32, 46, 255]; // deep slate
const GLASS = [239, 243, 248, 255]; // near white
const ACCENT = [214, 58, 47, 255]; // Rudolph red

/** Smooth 0..1 coverage for a signed distance field, one pixel wide edge. */
function coverage(distance, feather) {
  if (distance <= -feather) return 1;
  if (distance >= feather) return 0;
  return (feather - distance) / (2 * feather);
}

function over(dst, src, alpha) {
  for (let i = 0; i < 3; i++) dst[i] = Math.round(src[i] * alpha + dst[i] * (1 - alpha));
  dst[3] = Math.round(src[3] * alpha + dst[3] * (1 - alpha));
}

/** Signed distance to a rounded square centred on the canvas. */
function roundedSquareDistance(x, y, size) {
  const half = size / 2;
  const radius = size * 0.22;
  const dx = Math.abs(x - half) - (half - radius);
  const dy = Math.abs(y - half) - (half - radius);
  const ax = Math.max(dx, 0);
  const ay = Math.max(dy, 0);
  return Math.min(Math.max(dx, dy), 0) + Math.hypot(ax, ay) - radius;
}

/** Distance to a line segment, used for the magnifying glass handle. */
function segmentDistance(px, py, ax, ay, bx, by) {
  const vx = bx - ax;
  const vy = by - ay;
  const wx = px - ax;
  const wy = py - ay;
  const t = Math.max(0, Math.min(1, (wx * vx + wy * vy) / (vx * vx + vy * vy)));
  return Math.hypot(px - (ax + t * vx), py - (ay + t * vy));
}

/** RGBA pixels for one square icon size. */
function render(size) {
  const pixels = Buffer.alloc(size * size * 4);
  const feather = 0.7;
  const scale = size / 64;
  const lensX = 27 * scale;
  const lensY = 26 * scale;
  const lensR = 13 * scale;
  const ring = 3.2 * scale;
  const handleWidth = 3.4 * scale;

  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const px = x + 0.5;
      const py = y + 0.5;
      const rgba = [0, 0, 0, 0];

      over(rgba, BACKGROUND, coverage(roundedSquareDistance(px, py, size), feather));

      const handle = segmentDistance(px, py, lensX + lensR * 0.72, lensY + lensR * 0.72, 51 * scale, 50 * scale);
      over(rgba, ACCENT, coverage(handle - handleWidth / 2, feather));

      const lens = Math.abs(Math.hypot(px - lensX, py - lensY) - lensR) - ring / 2;
      over(rgba, GLASS, coverage(lens, feather));

      const inner = Math.hypot(px - lensX, py - lensY) - (lensR - ring / 2);
      over(rgba, [70, 130, 190, 90], coverage(inner, feather) * 0.55);

      const at = (y * size + x) * 4;
      pixels[at] = rgba[0];
      pixels[at + 1] = rgba[1];
      pixels[at + 2] = rgba[2];
      pixels[at + 3] = rgba[3];
    }
  }
  return pixels;
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) crc = crc & 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

function png(size, pixels) {
  const stride = size * 4 + 1;
  const raw = Buffer.alloc(stride * size);
  for (let y = 0; y < size; y++) {
    raw[y * stride] = 0;
    pixels.copy(raw, y * stride + 1, y * size * 4, (y + 1) * size * 4);
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 6; // truecolour with alpha
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

/** A classic 32 bit BMP icon image (DIB header, bottom up BGRA, plus the legacy AND mask). */
function bmp(size, pixels) {
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8); // colour plus mask, as the ICO format expects
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  const body = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const from = ((size - 1 - y) * size + x) * 4;
      const to = (y * size + x) * 4;
      body[to] = pixels[from + 2];
      body[to + 1] = pixels[from + 1];
      body[to + 2] = pixels[from];
      body[to + 3] = pixels[from + 3];
    }
  }
  const maskStride = Math.ceil(size / 32) * 4;
  return Buffer.concat([header, body, Buffer.alloc(maskStride * size)]);
}

const images = SIZES.map((size) => {
  const pixels = render(size);
  return { size, data: size >= 128 ? png(size, pixels) : bmp(size, pixels) };
});

const directory = Buffer.alloc(6 + images.length * 16);
directory.writeUInt16LE(0, 0);
directory.writeUInt16LE(1, 2); // type: icon
directory.writeUInt16LE(images.length, 4);
let offset = directory.length;
images.forEach((image, index) => {
  const at = 6 + index * 16;
  directory[at] = image.size >= 256 ? 0 : image.size;
  directory[at + 1] = image.size >= 256 ? 0 : image.size;
  directory.writeUInt16LE(1, at + 4);
  directory.writeUInt16LE(32, at + 6);
  directory.writeUInt32LE(image.data.length, at + 8);
  directory.writeUInt32LE(offset, at + 12);
  offset += image.data.length;
});

const out = resolve(ROOT, "src/RudolphTech/Assets/rudolph.ico");
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, Buffer.concat([directory, ...images.map((image) => image.data)]));
console.log(`Icono generado: ${out} (${SIZES.join(", ")} px)`);
