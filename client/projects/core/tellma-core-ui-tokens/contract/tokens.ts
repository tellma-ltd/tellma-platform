/**
 * The typed design-token contract (spec 0002 §4) — three tiers
 * (primitive → semantic → component) emitted to CSS custom properties.
 *
 * The TS contract is canonical; the brand CSS
 * (tellma-brand/design-system/tokens) was the starting import. Values are
 * either literal CSS values or typed `Ref`s to other tokens (resolved to
 * `var(--…)` references by the emitter — see `refToVarName`). Ramp shapes
 * follow the brand reality (ink has 700/800/900 only; grey carries a 25
 * stop); the spec's slice is explicitly a design-in-progress subset.
 */

/** A typed reference to another token, e.g. '{teal.600}' or '{status.error.fg}'. */
export type Ref = `{${string}}`;

/** A CSS value literal, or a `Ref` to another token. */
export type TmTokenValue = string;

/** Generic color ramp shape (brand ramps use subsets of these stops). */
export type ColorRamp = Partial<
  Record<25 | 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900, string>
>;

export type TmTealRamp = Record<
  50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900,
  string
>;
export type TmGreyRamp = Record<
  25 | 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900,
  TmTokenValue
>;
export type TmInkRamp = Record<700 | 800 | 900, string>;

/** Per-status color triple (fg carries text/icon ink; bg/border tint the surface). */
export interface TmStatusColors {
  readonly fg: TmTokenValue;
  readonly bg: TmTokenValue;
  readonly border: TmTokenValue;
}

/**
 * Everything scheme-dependent, one instance per scheme (light/dark). Dark
 * mode is a second base scheme, not a separate mechanism (§4): the dark
 * instance may override primitive ramp values (the brand inverts the cool
 * neutral ramp so grey-built components flip automatically) and redefines
 * the semantic roles.
 */
export interface TmSchemeColors {
  /** Value of the CSS `color-scheme` property inside the scheme's scope. */
  readonly colorScheme: 'light' | 'dark';
  /** Primitive ramp overrides applied within this scheme's scope. */
  readonly primitiveOverrides?: {
    readonly white?: string;
    readonly grey?: Partial<TmGreyRamp>;
    readonly teal?: Partial<TmTealRamp>;
  };
  readonly text: {
    readonly strong: TmTokenValue;
    readonly body: TmTokenValue;
    readonly secondary: TmTokenValue;
    readonly muted: TmTokenValue;
    readonly onDark: TmTokenValue;
    readonly link: TmTokenValue;
  };
  readonly surface: {
    readonly page: TmTokenValue;
    readonly subtle: TmTokenValue;
    readonly sunken: TmTokenValue;
    readonly card: TmTokenValue;
    readonly inverse: TmTokenValue;
    readonly hover: TmTokenValue;
    readonly selected: TmTokenValue;
  };
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
  readonly action: {
    /** Teal surface that CARRIES TEXT (buttons, solid badges). */
    readonly primary: TmTokenValue;
    readonly primaryHover: TmTokenValue;
    readonly primaryActive: TmTokenValue;
    readonly onPrimary: TmTokenValue;
    /** Brand-identity teal — decorative fills/borders only, never text. */
    readonly accent: TmTokenValue;
  };
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

/** WCAG 2.1 AA contrast categories (fixed thresholds: 4.5 / 3 / 3 — §4). */
export type TmContrastKind = 'text' | 'largeText' | 'uiComponent';

/**
 * A declared foreground/background pairing, named by EMITTED variable names
 * (e.g. '--field-text' on '--field-bg'). The contrast gate resolves both in
 * each scheme and fails the build below the fixed AA ratio for `kind`.
 */
export interface TmContrastPair {
  readonly fg: string;
  readonly bg: string;
  readonly kind: TmContrastKind;
}

/**
 * A reviewed, justified below-AA pair. `reason` is MANDATORY and must be
 * non-empty — the gate fails on an empty reason, so every exception is an
 * explicit decision that shows up in review (§4).
 */
export interface TmContrastException {
  readonly fg: string;
  readonly bg: string;
  readonly reason: string;
  readonly expires?: string;
  readonly owner?: string;
}

/** The full token document (one preset = one TmTokens instance). */
export interface TmTokens {
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
     * Language-keyed line-height, emitted as `:lang()` rules re-pointing
     * `--leading-ui`. Leading is a line-box property — unlike the font stack
     * it cannot follow scripts per glyph — so it keys on content language,
     * which distributions set via the root `lang` attribute. Every listed
     * language both sets and resets the alias, so an explicitly marked
     * island (`lang="en"` inside an Arabic page) keeps its own leading.
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
