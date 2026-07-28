// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSchemeColors, TmTokens } from '../contract/tokens';

/**
 * The default preset, reproducing `tellma-brand/design-system`: same
 * hexes, same `--field-*` / focus-ring / spacing / type tokens; the dark
 * scheme restates the brand's inverted neutrals as semantic-role values
 * (primitives never change meaning). One deliberate departure: the focus
 * ring color is teal-500 (clears the 3:1 focus-indicator ratio vs the white
 * field; the brand's older spacing.css still carried teal-400).
 */

const light: TmSchemeColors = {
  colorScheme: 'light',
  text: {
    strong: '{ink.900}',
    body: '{grey.700}',
    secondary: '{grey.500}',
    muted: '{grey.400}',
    onDark: '{white}',
    link: '{teal.600}',
  },
  surface: {
    page: '{white}',
    subtle: '{grey.25}',
    sunken: '{grey.50}',
    card: '{white}',
    inverse: '{ink.900}',
    hover: '{grey.50}',
    selected: '{teal.50}',
  },
  border: {
    subtle: '{grey.100}',
    default: '{grey.200}',
    strong: '{grey.300}',
    divider: '{grey.100}',
  },
  // Light teal selection highlight; ink text keeps 14:1.
  selection: {
    bg: '{teal.100}',
    text: '{ink.900}',
  },
  action: {
    // Teal that CARRIES TEXT is teal-600 (5.67:1 with white); the canonical
    // logo teal-400 is decorative only (2.97:1 on the white page by design).
    primary: '{teal.600}',
    primaryHover: '{teal.700}',
    primaryActive: '{teal.800}',
    onPrimary: '{white}',
    accent: '{teal.400}',
  },
  status: {
    success: { fg: '#2E7D5B', bg: '#E5F2EC', border: '#B6DDC9' },
    warning: { fg: '#B7791F', bg: '#FBF1DF', border: '#ECD5A6' },
    error: { fg: '#C0392B', bg: '#FAE8E6', border: '#EEC2BC' },
    info: { fg: '{teal.500}', bg: '{teal.50}', border: '{teal.100}' },
  },
  field: {
    bg: '{white}',
    bgDisabled: '{grey.50}',
    bgFilled: '{grey.25}',
    border: '{grey.200}',
    borderHover: '{grey.300}',
    borderFocus: '{teal.500}',
    borderInvalid: '{status.error.fg}',
    text: '{ink.900}',
    textDisabled: '{grey.400}',
    placeholder: '{grey.400}',
    icon: '{grey.400}',
  },
};

// The dark neutral scale — the brand's inversion of the light greys, stated
// as literals: primitives are scheme-invariant (--white stays white), so
// dark expresses its whole appearance through the semantic roles below.
const darkNeutral = {
  surface: '#16252D', // card/field surfaces (the slot white fills in light)
  25: '#1C2C34',
  400: '#74888E',
  500: '#94A5AB',
  700: '#CDD8DC',
  900: '#F2F7F8',
} as const;

const dark: TmSchemeColors = {
  colorScheme: 'dark',
  text: {
    strong: darkNeutral[900],
    body: darkNeutral[700],
    secondary: darkNeutral[500],
    muted: darkNeutral[400],
    onDark: darkNeutral[900],
    link: '{teal.300}',
  },
  surface: {
    page: '#0D181E',
    // A visible step OFF the page (midway to the card surface): the role's
    // contract is "a subtle panel tint", and everything riding it — grid
    // zebra stripes, readonly-cell tints, header fills — vanishes if it
    // collapses onto the page color the way an earlier draft had it.
    subtle: '#121E25',
    sunken: '#0A1418',
    card: darkNeutral.surface,
    inverse: darkNeutral[25],
    hover: '#20313A',
    selected: 'rgba(76, 160, 182, 0.18)',
  },
  // White at low alpha reads cleaner than solid greys on dark.
  border: {
    subtle: 'rgba(255, 255, 255, 0.07)',
    default: 'rgba(255, 255, 255, 0.12)',
    strong: 'rgba(255, 255, 255, 0.20)',
    divider: 'rgba(255, 255, 255, 0.07)',
  },
  // Deep teal selection; the light text keeps 10.5:1.
  selection: {
    bg: '{teal.800}',
    text: darkNeutral[900],
  },
  action: {
    // Lighter teal on dark carries INK text (ink on teal-400 = 6.12:1).
    primary: '{teal.400}',
    primaryHover: '{teal.300}',
    primaryActive: '{teal.200}',
    onPrimary: '{ink.900}',
    accent: '{teal.400}',
  },
  status: {
    success: { fg: '#5FC79A', bg: 'rgba(46, 125, 91, 0.18)', border: 'rgba(46, 125, 91, 0.42)' },
    warning: { fg: '#E0A93E', bg: 'rgba(183, 121, 31, 0.20)', border: 'rgba(183, 121, 31, 0.44)' },
    error: { fg: '#E06A5C', bg: 'rgba(192, 57, 43, 0.20)', border: 'rgba(192, 57, 43, 0.44)' },
    info: { fg: '{teal.300}', bg: 'rgba(76, 160, 182, 0.16)', border: 'rgba(76, 160, 182, 0.34)' },
  },
  field: {
    bg: darkNeutral.surface,
    bgDisabled: '#1A2930',
    bgFilled: darkNeutral[25],
    border: 'rgba(255, 255, 255, 0.14)',
    borderHover: 'rgba(255, 255, 255, 0.24)',
    borderFocus: '{teal.300}',
    borderInvalid: '{status.error.fg}',
    text: darkNeutral[900],
    textDisabled: darkNeutral[400],
    placeholder: darkNeutral[400],
    icon: darkNeutral[400],
  },
};

/** The Tellma brand default preset — the document the shipped stylesheet is emitted from. */
export const tmTokensDefault: TmTokens = {
  primitive: {
    color: {
      ink: { 700: '#163542', 800: '#0A2530', 900: '#001722' },
      teal: {
        50: '#EAF4F7',
        100: '#CBE6EC',
        200: '#A3D3DD',
        300: '#74BACA',
        400: '#4CA0B6',
        500: '#3E899D',
        600: '#316E80',
        700: '#265767',
        800: '#1B3F4B',
        900: '#0E2832',
      },
      grey: {
        25: '#F7FAFB',
        50: '#EEF3F4',
        100: '#E1E8EA',
        200: '#CBD6D9',
        300: '#A8B7BC',
        400: '#7C8E94',
        500: '#56686F',
        600: '#3C4E55',
        700: '#283A41',
        800: '#14262E',
        900: '{ink.900}',
      },
      white: '#FEFEFE',
    },
    radius: { xs: '4px', sm: '6px', md: '10px', lg: '16px', xl: '24px', full: '999px' },
    space: {
      0: '0',
      1: '4px',
      2: '8px',
      3: '12px',
      4: '16px',
      5: '20px',
      6: '24px',
      8: '32px',
      10: '40px',
      12: '48px',
      16: '64px',
      20: '80px',
      24: '96px',
    },
    font: {
      sans: "'Noto Sans', 'Noto Sans Fallback', system-ui, -apple-system, 'Segoe UI', sans-serif",
      arabic: "'Noto Sans Arabic', 'Noto Sans Arabic Fallback', 'Noto Sans', sans-serif",
      mono: "'Noto Sans Mono', 'Noto Sans Mono Fallback', ui-monospace, 'SF Mono', Menlo, monospace",
      // Brand faces per script first (core-ui vendors the Latin faces; the
      // Arabic faces arrive with @tellma/locale-ar), then the metric-adjusted
      // local fallbacks (unicode-range keeps each on its own script),
      // generics last.
      ui: [
        "'Noto Sans'",
        "'Noto Sans Arabic'",
        "'Noto Sans Fallback'",
        "'Noto Sans Arabic Fallback'",
        'system-ui',
        '-apple-system',
        "'Segoe UI'",
        'sans-serif',
      ],
      size: { xs: '12px', sm: '14px', base: '16px', lg: '18px' },
      weight: { regular: '400', medium: '500', semibold: '600', bold: '700' },
      leading: { tight: '1.2', snug: '1.35', body: '1.6', arabic: '1.9' },
    },
    border: { width: '1px' },
    shadow: {
      xs: '0 1px 2px rgba(0, 23, 34, 0.05)',
      sm: '0 1px 3px rgba(0, 23, 34, 0.07), 0 1px 2px rgba(0, 23, 34, 0.04)',
      md: '0 4px 12px rgba(0, 23, 34, 0.08), 0 1px 3px rgba(0, 23, 34, 0.05)',
      lg: '0 12px 32px rgba(0, 23, 34, 0.12), 0 2px 6px rgba(0, 23, 34, 0.06)',
    },
    motion: {
      durationFast: '120ms',
      durationNormal: '180ms',
      durationSlow: '280ms',
      easeStandard: 'cubic-bezier(0.2, 0, 0, 1)',
      easeOut: 'cubic-bezier(0, 0, 0, 1)',
      easeInOut: 'cubic-bezier(0.45, 0, 0.2, 1)',
    },
  },
  semantic: {
    colorScheme: { light, dark },
    focusRing: { width: '2px', color: '{teal.500}', offset: '2px' },
    formField: {
      radius: '{radius.sm}',
      height: '38px',
      heightSm: '30px',
      heightLg: '46px',
      paddingX: '12px',
      paddingY: '8px',
      fontSize: '{font.size.sm}',
    },
    leadingByLang: {
      ar: '{font.leading.arabic}',
      en: '{font.leading.body}',
    },
  },
  component: {
    // tm-checkbox (§3.3): the visible box renders at the brand 18px while
    // the hit target is padded past the 24px minimum.
    checkbox: { boxSize: '18px' },
    // tm-select (§3.4): panel + option-row geometry (touch-comfortable rows).
    select: { panelMaxHeight: '280px', optionHeight: '36px' },
    // tm-grid / tm-tree-grid: row density mirrors the field-height scale
    // one notch tighter (data rows, not form fields); selection fill is a
    // translucent brand teal so gridlines and text stay readable under it.
    grid: {
      rowHeight: '32px',
      rowHeightSm: '26px',
      rowHeightLg: '40px',
      headerBg: '{surface.subtle}',
      headerText: '{text.secondary}',
      line: '{border.subtle}',
      selectionBg: 'rgba(76, 160, 182, 0.14)',
      selectionBorder: '{action.accent}',
      errorBg: '{status.error.bg}',
      errorBorder: '{status.error.border}',
      readonlyBg: '{surface.subtle}',
      zebraBg: '{surface.subtle}',
      cutBorder: '{action.accent}',
      findMatchBg: '{status.warning.bg}',
      findActiveOutline: '{status.warning.fg}',
      indent: '20px',
      rowHeaderWidth: '48px',
      checkColWidth: '36px',
      minColWidth: '48px',
      handleSize: '24px',
    },
    // tm-menu: panel + item-row geometry; colors ride the field/surface
    // semantic tokens in the component CSS.
    menu: { minWidth: '180px', itemHeight: '32px', iconSize: '16px' },
  },
};
