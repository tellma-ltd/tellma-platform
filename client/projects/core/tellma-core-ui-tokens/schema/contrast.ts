/**
 * WCAG 2.1 contrast math: relative luminance + contrast ratio, with
 * alpha compositing (the dark scheme uses rgba() borders, which must be
 * composited over their background before measuring). Dependency-free.
 */

/** A parsed color: 0–255 channels plus 0–1 alpha. */
export interface TmRgba {
  /** Red channel, 0–255. */
  readonly r: number;
  /** Green channel, 0–255. */
  readonly g: number;
  /** Blue channel, 0–255. */
  readonly b: number;
  /** Alpha, 0 (transparent) to 1 (opaque). */
  readonly a: number;
}

/** Parses #rgb/#rrggbb/#rrggbbaa and rgb()/rgba() (comma or space syntax). */
export function tmParseColor(value: string): TmRgba | null {
  const v = value.trim().toLowerCase();

  const hex = /^#([0-9a-f]{3,8})$/.exec(v);
  if (hex) {
    const h = hex[1];
    if (h.length === 3 || h.length === 4) {
      const [r, g, b, a] = h.split('').map((c) => parseInt(c + c, 16));
      return { r, g, b, a: h.length === 4 ? a / 255 : 1 };
    }
    if (h.length === 6 || h.length === 8) {
      const r = parseInt(h.slice(0, 2), 16);
      const g = parseInt(h.slice(2, 4), 16);
      const b = parseInt(h.slice(4, 6), 16);
      const a = h.length === 8 ? parseInt(h.slice(6, 8), 16) / 255 : 1;
      return { r, g, b, a };
    }
    return null;
  }

  const fn = /^rgba?\(\s*([\d.]+)\s*[, ]\s*([\d.]+)\s*[, ]\s*([\d.]+)\s*(?:[,/]\s*([\d.%]+)\s*)?\)$/.exec(
    v,
  );
  if (fn) {
    const a = fn[4] === undefined ? 1 : fn[4].endsWith('%') ? parseFloat(fn[4]) / 100 : parseFloat(fn[4]);
    return { r: parseFloat(fn[1]), g: parseFloat(fn[2]), b: parseFloat(fn[3]), a };
  }
  return null;
}

/** Composites `top` over an opaque `bottom` (standard source-over). */
export function tmComposite(top: TmRgba, bottom: TmRgba): TmRgba {
  const a = top.a + bottom.a * (1 - top.a);
  if (a === 0) {
    return { r: 0, g: 0, b: 0, a: 0 };
  }
  const channel = (t: number, b: number) => (t * top.a + b * bottom.a * (1 - top.a)) / a;
  return { r: channel(top.r, bottom.r), g: channel(top.g, bottom.g), b: channel(top.b, bottom.b), a };
}

/** WCAG 2.1 relative luminance of an (assumed opaque) color. */
export function tmRelativeLuminance(color: TmRgba): number {
  const lin = (channel: number) => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * lin(color.r) + 0.7152 * lin(color.g) + 0.0722 * lin(color.b);
}

/**
 * WCAG contrast ratio between fg and bg. Semi-transparent bg is first
 * composited over `canvas` (the scheme's page surface); semi-transparent fg
 * is composited over the resulting bg.
 */
export function tmContrastRatio(fg: TmRgba, bg: TmRgba, canvas?: TmRgba): number {
  let solidBg = bg;
  if (bg.a < 1) {
    solidBg = tmComposite(bg, canvas ?? { r: 255, g: 255, b: 255, a: 1 });
  }
  const solidFg = fg.a < 1 ? tmComposite(fg, solidBg) : fg;
  const l1 = tmRelativeLuminance(solidFg);
  const l2 = tmRelativeLuminance(solidBg);
  const [hi, lo] = l1 >= l2 ? [l1, l2] : [l2, l1];
  return (hi + 0.05) / (lo + 0.05);
}

/** The fixed WCAG 2.1 AA thresholds — never configurable. */
export const TM_CONTRAST_THRESHOLDS = {
  text: 4.5,
  largeText: 3,
  uiComponent: 3,
} as const;
