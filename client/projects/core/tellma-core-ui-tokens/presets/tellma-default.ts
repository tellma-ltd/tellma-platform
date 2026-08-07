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
    hover: '{grey.25}',
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
    focusHalo: '{teal.50}',
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
    // Translucent, not the light scheme's opaque teal-50: an opaque tint
    // this pale would read as a plate around the field on a dark surface.
    focusHalo: 'rgba(76, 160, 182, 0.22)',
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
    // Overlay elevation only — in-canvas surfaces stay flat with a hairline.
    // md is the anchored-panel shadow (menus, dropdowns, popovers, the date
    // popup); lg lifts a modal off its scrim. Both are negative-spread so
    // the panel edge stays crisp and the shadow reads as depth, not haze.
    shadow: {
      xs: '0 1px 2px rgba(0, 23, 34, 0.05)',
      sm: '0 4px 12px -4px rgba(0, 23, 34, 0.4)',
      md: '0 8px 24px -8px rgba(0, 23, 34, 0.28), 0 2px 6px -2px rgba(0, 23, 34, 0.12)',
      lg: '0 16px 40px -12px rgba(0, 23, 34, 0.4)',
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
    focusRing: { width: '2px', color: '{teal.500}', offset: '2px', haloWidth: '3px' },
    formField: {
      radius: '{radius.sm}',
      height: '38px',
      heightSm: '30px',
      heightLg: '46px',
      paddingX: '12px',
      paddingXsm: '10px',
      paddingXlg: '14px',
      paddingY: '8px',
      fontSize: '{font.size.sm}',
      fontSizeSm: '13px',
      fontSizeLg: '15px',
      labelGap: '6px',
      labelGapSm: '4px',
      labelGapLg: '7px',
      // Label and hint are chrome around the control, not part of it: they
      // hold their type through every size so a dense form still reads.
      labelFontSize: '13px',
      hintFontSize: '{font.size.xs}',
      errorIconSize: '13px',
      optionHeight: '30px',
      optionHeightSm: '26px',
      optionHeightLg: '34px',
    },
    leadingByLang: {
      ar: '{font.leading.arabic}',
      en: '{font.leading.body}',
    },
  },
  component: {
    // tm-alert: page/section status wrapper — geometry only; per-kind
    // colors ride the status semantic tokens in the component CSS. An alert
    // is a block of prose, not a control, so it keeps card geometry
    // (radius-md, roomy padding) while the controls around it go dense.
    alert: {
      gap: '10px',
      paddingX: '14px',
      paddingY: '12px',
      radius: '{radius.md}',
      iconSize: '18px',
      headingFontSize: '{font.size.sm}',
      bodyFontSize: '13px',
    },
    // tmButton: heights ride the shared field-height scale (buttons align
    // with form fields in toolbars); per-variant colors resolve per scheme
    // through the semantic action/field/status roles.
    //
    // Kebab hazard: two adjacent capitals do NOT split, so `paddingXSm`
    // emits `--button-padding-xsm`. The keys below are spelled the way they
    // emit; check any new one against the generated stylesheet.
    button: {
      radius: '{radius.sm}',
      gap: '8px',
      gapSm: '6px',
      gapLg: '8px',
      paddingXsm: '12px',
      paddingX: '16px',
      paddingXlg: '22px',
      fontSize: '{font.size.sm}',
      fontSizeSm: '13px',
      fontSizeLg: '15px',
      // Projected icons need an explicit size: an icon set's natural 24px
      // is taller than a small button's whole content box.
      iconSize: '14px',
      // Disabled is a solid flat plate, not a faded live button: a
      // translucent control shows whatever it sits on and stops reading as
      // one surface. Filled and outline variants land on different plates
      // so a disabled ghost does not grow a box it never had.
      //
      // The disabled INK is --text-muted for every variant. The brand sheet
      // draws white on the filled plate, which is 1.6:1 — legible only
      // because disabled text is exempt from contrast rules. Muted ink on
      // the same plate reads, and disabled-ness is already carried by the
      // flat fill plus the removed border.
      disabledBg: '{border.default}',
      disabledBgSubtle: '{surface.sunken}',
      disabledText: '{text.muted}',
      primaryBg: '{action.primary}',
      primaryText: '{action.onPrimary}',
      primaryHoverBg: '{action.primaryHover}',
      primaryActiveBg: '{action.primaryActive}',
      secondaryBg: '{surface.card}',
      secondaryText: '{text.strong}',
      secondaryBorder: '{border.default}',
      secondaryHoverBg: '{surface.hover}',
      secondaryHoverBorder: '{border.strong}',
      secondaryActiveBg: '{surface.sunken}',
      ghostText: '{text.strong}',
      ghostHoverBg: '{surface.hover}',
      ghostActiveBg: '{border.subtle}',
      dangerBg: '{status.error.fg}',
      dangerText: '{action.onPrimary}',
    },
    // tm-checkbox (§3.3): the visible box renders at the brand 18px while
    // the hit target is padded past the 24px minimum. The box does NOT
    // follow the size ladder — a smaller tick would drop under the touch
    // minimum and is the one control whose glyph must stay legible.
    checkbox: {
      boxSize: '18px',
      // The mark sits INSIDE the box: the glyph spans nearly its whole
      // viewBox, so drawn edge to edge it would touch the box's border.
      glyphSize: '14px',
      gap: '9px',
      labelFontSize: '13px',
    },
    // tm-date-picker: popup geometry. Cells exceed the 24px touch minimum;
    // the popup width holds seven cells plus gaps at every view. Cells are
    // wider than they are tall — a calendar reads as columns of weekdays,
    // and the extra width is where two-digit days breathe.
    datePicker: {
      cellHeight: '30px',
      cellGap: '1px',
      slotHeight: '32px',
      popupWidth: '256px',
      popupPadding: '8px',
      headerFontSize: '13px',
      weekdayHeight: '22px',
      weekdayFontSize: '10.5px',
      cellFontSize: '12.5px',
      navGlyphSize: '15px',
    },
    // tm-select (§3.4): panel + option-row geometry. Rows follow the size
    // ladder because a picker used dozens of times a minute is read as a
    // list, not tapped as a button.
    select: {
      panelMaxHeight: '280px',
      panelPadding: '4px',
      optionRadius: '{radius.xs}',
      optionPaddingX: '8px',
      optionGap: '8px',
      checkSize: '15px',
    },
    // tm-entity-picker (spec 0006 §10): dropdown geometry — rows share the
    // select's sizing; the min-width floor keeps the panel readable when a
    // narrow grid cell would make matched width unusable.
    entityPicker: {
      panelMaxHeight: '280px',
      panelMinWidth: '200px',
      panelPadding: '4px',
      optionRadius: '{radius.xs}',
      optionPaddingX: '8px',
      optionGap: '8px',
      iconSize: '15px',
      noticeFontSize: '11.5px',
    },
    // tm-grid / tm-tree-grid: row density mirrors the field-height scale
    // one notch tighter (data rows, not form fields); selection fill is a
    // translucent brand teal so gridlines and text stay readable under it.
    grid: {
      // The brand sheet's ladder is 26/30/34, but 26 reads as cramped once
      // there is real data in the cells rather than specimen text — the
      // glyphs touch the rules. The whole ladder moves up one step so the
      // dense default lands on 30.
      rowHeight: '34px',
      rowHeightSm: '30px',
      rowHeightLg: '38px',
      headerBg: '{surface.sunken}',
      headerText: '{text.secondary}',
      line: '{border.subtle}',
      // The header's lower edge is the one rule drawn at full strength, in
      // both the ruled and the plain look: it separates chrome from data.
      headerLine: '{border.default}',
      selectionBg: 'rgba(76, 160, 182, 0.14)',
      selectionBorder: '{action.accent}',
      errorBg: '{status.error.bg}',
      errorBorder: '{status.error.border}',
      readonlyBg: '{field.bgFilled}',
      zebraBg: '{surface.subtle}',
      cutBorder: '{action.primary}',
      findMatchBg: '{status.warning.bg}',
      findActiveOutline: '{status.warning.fg}',
      // One indent step per level, sized to sit under the twisty.
      indent: '16px',
      rowHeaderWidth: '34px',
      checkColWidth: '30px',
      minColWidth: '48px',
      handleSize: '24px',
      cellPaddingX: '8px',
      cellFontSize: '12px',
      // The plain (readonly) look drops the vertical rules, so its cells
      // need their own inline breathing room to stay separable.
      //
      // Named plain-FIRST, not `cellPaddingXPlain`: two adjacent capitals do
      // not kebab-split, so that spelling emits `--grid-cell-padding-xplain`
      // and every rule reading `-x-plain` silently resolves to nothing.
      plainCellPaddingX: '11px',
      plainCellFontSize: '12.5px',
      headerFontSize: '11.5px',
      rowHeaderFontSize: '10.5px',
      statusHeight: '30px',
      statusFontSize: '11.5px',
      statusIconSize: '14px',
      twistySize: '15px',
    },
    // tm-dropzone: drop-region geometry; colors ride the field/surface
    // semantics in the component CSS.
    files: {
      dropzoneMinHeight: '108px',
      dropzonePadding: '14px',
      dropzoneRadius: '{radius.md}',
      dropzoneGap: '5px',
      dropzoneIconSize: '22px',
      dropzoneHintFontSize: '12.5px',
      dropzoneMetaFontSize: '11px',
    },
    // tm-image: fixed-box chrome. The chrome fill is a static ink veil
    // that reads over any image on both schemes.
    image: {
      radius: '{radius.sm}',
      glyphSize: '20px',
      chromeBg: 'rgba(0, 23, 34, 0.72)',
      chromeText: '{white}',
      chromeButtonSize: '20px',
      cropBarPaddingX: '8px',
      cropBarPaddingY: '5px',
      cropBarShadow: '0 4px 14px -4px rgba(0, 23, 34, 0.55)',
      cropSliderTrack: '4px',
      cropSliderKnob: '12px',
      cropDoneHeight: '22px',
      cropDoneGlyphSize: '12px',
      chromeIconSize: '14px',
      chromeRadius: '{radius.xs}',
    },
    // tm-menu: panel + item-row geometry; colors ride the field/surface
    // semantic tokens in the component CSS. A menu is a pointer surface
    // read in one pass, so it holds one dense height rather than following
    // the size ladder.
    menu: {
      minWidth: '180px',
      itemHeight: '30px',
      iconSize: '15px',
      itemGap: '9px',
      itemPaddingX: '8px',
      itemRadius: '{radius.xs}',
      itemFontSize: '12.5px',
      panelPadding: '4px',
      shortcutFontSize: '10px',
    },
    // tm-modal: panel buckets + shell geometry. The scrim is a static ink
    // veil that reads correctly over both schemes.
    modal: {
      widthSm: '420px',
      widthMd: '640px',
      margin: '16px',
      lgMargin: '48px',
      radius: '{radius.md}',
      paddingX: '16px',
      headerPaddingY: '11px',
      footerPaddingX: '12px',
      footerPaddingY: '10px',
      footerBg: '{surface.subtle}',
      titleFontSize: '{font.size.sm}',
      closeSize: '28px',
      iconSize: '15px',
      scrim: 'rgba(4, 18, 24, 0.55)',
    },
    // tm-spinner: a decorative ring — a quiet full track with one quarter
    // arc turning on it. The arc rides `color` so a host re-points it by
    // setting text color alone; only the track needs a token, because it
    // has to stay a groove against whatever ink the arc inherited.
    spinner: {
      size: '14px',
      track: '{border.default}',
      duration: '700ms',
    },
    // tm-file-preview: viewer-region geometry inside the lg modal.
    preview: {
      minHeight: '320px',
      cardIconSize: '20px',
      audioMaxWidth: '480px',
      footerPaddingX: '10px',
      footerPaddingY: '8px',
      footerFontSize: '11px',
      footerBg: '{surface.subtle}',
    },
    // tm-popover: panel geometry; colors ride the surface/border semantics
    // in the component CSS.
    popover: {
      padding: '12px',
      radius: '{radius.md}',
      maxInlineSize: '320px',
    },
    // tm-tab-group: strip geometry; the active indicator draws inside the
    // tab box so activation never reflows. A vertical strip is a nav list
    // rather than a header, so it runs at its own tighter row height.
    tabs: {
      height: '34px',
      verticalHeight: '30px',
      indicatorThickness: '2px',
      gap: '2px',
      labelPaddingX: '11px',
      fontSize: '13px',
      verticalFontSize: '12.5px',
      verticalPaddingX: '10px',
      verticalRadius: '{radius.xs}',
    },
    // tmTooltip: inverse-surface text bubble. The directive reads `delay`
    // at show time; `offset` is the visual gap from the host.
    tooltip: {
      delay: '500ms',
      offset: '6px',
      paddingX: '9px',
      paddingY: '5px',
      radius: '{radius.sm}',
      maxInlineSize: '200px',
      fontSize: '11.5px',
    },
    // The validation-message bubble: one look shared by tm-form-field and
    // the grid's active-cell error, so a field error and a cell error read
    // as the same thing. It is a card that has gone red at the edge, not a
    // filled error banner — the field it points at already carries the red.
    errorPopover: {
      paddingX: '9px',
      paddingY: '5px',
      gap: '5px',
      radius: '{radius.sm}',
      fontSize: '{font.size.xs}',
      iconSize: '13px',
      // The visual gap between the field's border box and the bubble; the
      // arrow overlaps back into it.
      offset: '7px',
      arrowSize: '6px',
      // How far the arrow tip sits from the bubble's leading edge, so it
      // lands under the field's inline padding rather than its corner.
      arrowInset: '13px',
      maxInlineSize: '320px',
      shadow: '0 6px 16px -8px rgba(0, 23, 34, 0.28)',
    },
  },
};
