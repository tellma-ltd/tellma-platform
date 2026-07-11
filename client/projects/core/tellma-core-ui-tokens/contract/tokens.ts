// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The typed design-token contract — three tiers
 * (primitive → semantic → component) emitted to CSS custom properties.
 *
 * The TS contract is canonical; the brand CSS
 * (tellma-brand/design-system/tokens) was the starting import. Values are
 * either literal CSS values or typed `Ref`s to other tokens (resolved to
 * `var(--…)` references by the emitter — see `refToVarName`). Ramp shapes
 * follow the brand reality (ink has 700/800/900 only; grey carries a 25
 * stop); the contract is explicitly a design-in-progress subset.
 */

/** A typed reference to another token, e.g. '{teal.600}' or '{status.error.fg}'. */
export type TmRef = `{${string}}`;

/**
 * A CSS value literal, or a `TmRef` to another token. The intersection keeps
 * the union from collapsing to `string`, so editors surface the ref form.
 */
export type TmTokenValue = TmRef | (string & NonNullable<unknown>);

/** Generic color ramp shape (brand ramps use subsets of these stops). */
export type TmColorRamp = Partial<
  Record<25 | 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900, string>
>;

/** The brand teal ramp (stops 50–900). */
export type TmTealRamp = Record<
  50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900,
  string
>;
/** The cool neutral grey ramp (stops 25–900); values may reference other tokens. */
export type TmGreyRamp = Record<
  25 | 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900,
  TmTokenValue
>;
/** The dark ink ramp — the brand carries only the 700/800/900 stops. */
export type TmInkRamp = Record<700 | 800 | 900, string>;

/** Per-status color triple (fg carries text/icon ink; bg/border tint the surface). */
export interface TmStatusColors {
  /** The status ink — carries text and icons. */
  readonly fg: TmTokenValue;
  /** The tinted surface behind status content. */
  readonly bg: TmTokenValue;
  /** The border tint around status content. */
  readonly border: TmTokenValue;
}

/**
 * Everything scheme-dependent, one instance per scheme (light/dark). Dark
 * mode is a second base scheme, not a separate mechanism. Primitives are
 * scheme-invariant — `--white` is always white, `--grey-900` always the
 * darkest grey — so each scheme expresses its whole appearance through
 * these semantic roles.
 */
export interface TmSchemeColors {
  /** Value of the CSS `color-scheme` property inside the scheme's scope. */
  readonly colorScheme: 'light' | 'dark';
  /** Text ink roles, strongest to most muted, plus the on-dark and link inks. */
  readonly text: {
    readonly strong: TmTokenValue;
    readonly body: TmTokenValue;
    readonly secondary: TmTokenValue;
    readonly muted: TmTokenValue;
    readonly onDark: TmTokenValue;
    readonly link: TmTokenValue;
  };
  /** Background surface roles, from the page canvas to hover/selected states. */
  readonly surface: {
    readonly page: TmTokenValue;
    readonly subtle: TmTokenValue;
    readonly sunken: TmTokenValue;
    readonly card: TmTokenValue;
    readonly inverse: TmTokenValue;
    readonly hover: TmTokenValue;
    readonly selected: TmTokenValue;
  };
  /** Border inks by emphasis, plus the divider. */
  readonly border: {
    readonly subtle: TmTokenValue;
    readonly default: TmTokenValue;
    readonly strong: TmTokenValue;
    readonly divider: TmTokenValue;
  };
  /**
   * Text-selection highlight (`::selection`). Flat colors only — the CSS
   * highlight pseudo-elements ignore `background-image`, so a gradient
   * cannot apply here.
   */
  readonly selection: {
    readonly bg: TmTokenValue;
    readonly text: TmTokenValue;
  };
  /** Interactive action colors: the primary surface trio, its text ink, and the accent. */
  readonly action: {
    /** Teal surface that CARRIES TEXT (buttons, solid badges). */
    readonly primary: TmTokenValue;
    readonly primaryHover: TmTokenValue;
    readonly primaryActive: TmTokenValue;
    readonly onPrimary: TmTokenValue;
    /** Brand-identity teal — decorative fills/borders only, never text. */
    readonly accent: TmTokenValue;
  };
  /** Status color triples for success/warning/error/info. */
  readonly status: {
    readonly success: TmStatusColors;
    readonly warning: TmStatusColors;
    readonly error: TmStatusColors;
    readonly info: TmStatusColors;
  };
  /** The scheme-dependent half of the shared form-field group. */
  readonly field: {
    readonly bg: TmTokenValue;
    readonly bgDisabled: TmTokenValue;
    readonly bgFilled: TmTokenValue;
    readonly border: TmTokenValue;
    readonly borderHover: TmTokenValue;
    readonly borderFocus: TmTokenValue;
    readonly borderInvalid: TmTokenValue;
    readonly text: TmTokenValue;
    readonly textDisabled: TmTokenValue;
    readonly placeholder: TmTokenValue;
    readonly icon: TmTokenValue;
  };
}

/** WCAG 2.1 AA contrast categories (fixed thresholds: 4.5 / 3 / 3). */
export type TmContrastKind = 'text' | 'largeText' | 'uiComponent';

/**
 * A declared foreground/background pairing, named by EMITTED variable names
 * (e.g. '--field-text' on '--field-bg'). The contrast gate resolves both in
 * each scheme and fails the build below the fixed AA ratio for `kind`.
 */
export interface TmContrastPair {
  /** Emitted variable name of the foreground ink (e.g. '--field-text'). */
  readonly fg: string;
  /** Emitted variable name of the background it renders on (e.g. '--field-bg'). */
  readonly bg: string;
  /** The WCAG category whose AA threshold the pair must clear. */
  readonly kind: TmContrastKind;
}

/**
 * A reviewed, justified below-AA pair. `reason` is MANDATORY and must be
 * non-empty — the gate fails on an empty reason, so every exception is an
 * explicit decision that shows up in review.
 */
export interface TmContrastException {
  /** Emitted variable name of the excepted pair's foreground. */
  readonly fg: string;
  /** Emitted variable name of the excepted pair's background. */
  readonly bg: string;
  /**
   * Narrows the exception to one scheme. Omitted, it suppresses the pair in
   * both — prefer scoping it, so a pair that only fails in light stays
   * gated in dark.
   */
  readonly scheme?: 'light' | 'dark';
  /** Narrows the exception to pairs declared with one kind; omitted, any. */
  readonly kind?: TmContrastKind;
  /** The justification recorded for review; must be non-empty. */
  readonly reason: string;
  /** Optional date (ISO 8601) by which the exception should be revisited. */
  readonly expires?: string;
  /** Optional owner accountable for the exception. */
  readonly owner?: string;
}

/** The full token document (one preset = one TmTokens instance). */
export interface TmTokens {
  /** Primitive tier: raw brand values (ramps, radius, spacing, type, shadows, motion). */
  readonly primitive: {
    readonly color: {
      readonly ink: TmInkRamp;
      readonly teal: TmTealRamp;
      readonly grey: TmGreyRamp;
      readonly white: string;
    };
    readonly radius: {
      readonly xs: string;
      readonly sm: string;
      readonly md: string;
      readonly lg: string;
      readonly xl: string;
      readonly full: string;
    };
    readonly space: Record<0 | 1 | 2 | 3 | 4 | 5 | 6 | 8 | 10 | 12 | 16 | 20 | 24, string>;
    readonly font: {
      readonly sans: string;
      readonly arabic: string;
      readonly mono: string;
      /**
       * The multi-script UI stack emitted as `--font-ui`: brand family names
       * in fallback order, generic families last. Each glyph resolves to its
       * script's face via the faces' `@font-face` `unicode-range`s, so mixed
       * Arabic/Latin/etc. lines render every brand face at once. Names whose
       * faces no installed locale pack registers are skipped harmlessly, so
       * the stack lists every brand script face up front.
       */
      readonly ui: readonly string[];
      readonly size: {
        readonly xs: string;
        readonly sm: string;
        readonly base: string;
        readonly lg: string;
      };
      readonly weight: {
        readonly regular: string;
        readonly medium: string;
        readonly semibold: string;
        readonly bold: string;
      };
      readonly leading: {
        readonly tight: string;
        readonly snug: string;
        readonly body: string;
        readonly arabic: string;
      };
    };
    readonly border: { readonly width: string };
    readonly shadow: {
      readonly xs: string;
      readonly sm: string;
      readonly md: string;
      readonly lg: string;
    };
    readonly motion: {
      readonly durationFast: string;
      readonly durationNormal: string;
      readonly durationSlow: string;
      readonly easeStandard: string;
      readonly easeOut: string;
      readonly easeInOut: string;
    };
  };
  /** Semantic tier: role tokens referencing primitives, per color scheme where needed. */
  readonly semantic: {
    readonly colorScheme: {
      readonly light: TmSchemeColors;
      readonly dark: TmSchemeColors;
    };
    readonly focusRing: {
      readonly width: string;
      readonly color: TmTokenValue;
      readonly offset: string;
    };
    /** Scheme-independent half of the form-field group (sizing/typography). */
    readonly formField: {
      readonly radius: TmTokenValue;
      readonly height: string;
      readonly heightSm: string;
      readonly heightLg: string;
      readonly paddingX: string;
      readonly paddingY: string;
      readonly fontSize: TmTokenValue;
    };
    /**
     * Language-keyed line-height, emitted as `:lang()` rules that re-point
     * `--leading-ui` AND apply `line-height: var(--leading-ui)` (line-height
     * inherits by computed value, so re-pointing alone would never reach
     * below the root). Leading is a line-box property — unlike the font
     * stack it cannot follow scripts per glyph — so it keys on content
     * language, via the root `lang` attribute or any marked island. Every
     * listed language both sets and resets, so `lang="en"` inside an Arabic
     * page keeps its own leading; the rules live in `@layer tm.base`, so
     * any unlayered component/app `line-height` still wins.
     */
    readonly leadingByLang: Readonly<Record<string, TmTokenValue>>;
  };
  /** Component tier: `--<component>-<key>` variables referencing semantic tokens. */
  readonly component: Record<string, Record<string, TmTokenValue>>;
  /** Declared fg/bg pairings the contrast gate checks in BOTH schemes. */
  readonly contrastPairs: readonly TmContrastPair[];
  /** Reviewed below-AA pairs (mandatory reason) the gate skips. */
  readonly contrastExceptions: readonly TmContrastException[];
}
