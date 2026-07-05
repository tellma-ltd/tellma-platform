import type { TmTokens } from './contract/tokens';
import { tmEmitCss, tmEmittedSchemeVars, tmRefToVarName, tmTokenValueToCss } from './emit/emit-css';
import { tmTokensDefault } from './presets/tellma-default';
import { tmContrastRatio, tmParseColor, tmRelativeLuminance } from './schema/contrast';
import { tmResolveVar, tmValidateTokens } from './schema/validate';

describe('tmRefToVarName', () => {
  it('maps primitive and semantic paths to the brand variable names', () => {
    expect(tmRefToVarName('teal.500')).toBe('--teal-500');
    expect(tmRefToVarName('grey.25')).toBe('--grey-25');
    expect(tmRefToVarName('white')).toBe('--white');
    expect(tmRefToVarName('radius.sm')).toBe('--radius-sm');
    expect(tmRefToVarName('space.4')).toBe('--space-4');
    expect(tmRefToVarName('font.size.sm')).toBe('--text-sm');
    expect(tmRefToVarName('font.sans')).toBe('--font-sans');
    expect(tmRefToVarName('motion.durationFast')).toBe('--duration-fast');
    expect(tmRefToVarName('motion.easeInOut')).toBe('--ease-in-out');
    expect(tmRefToVarName('text.strong')).toBe('--text-strong');
    expect(tmRefToVarName('action.primary')).toBe('--color-primary');
    expect(tmRefToVarName('action.onPrimary')).toBe('--color-on-primary');
    expect(tmRefToVarName('action.accent')).toBe('--accent');
    expect(tmRefToVarName('status.error.fg')).toBe('--error');
    expect(tmRefToVarName('status.error.bg')).toBe('--error-bg');
    expect(tmRefToVarName('field.bgDisabled')).toBe('--field-bg-disabled');
    expect(tmRefToVarName('formField.heightSm')).toBe('--field-height-sm');
    expect(tmRefToVarName('focusRing.color')).toBe('--focus-ring-color');
  });

  it('turns refs into var() and passes literals through', () => {
    expect(tmTokenValueToCss('{teal.600}')).toBe('var(--teal-600)');
    expect(tmTokenValueToCss('#FEFEFE')).toBe('#FEFEFE');
    expect(tmTokenValueToCss('rgba(255, 255, 255, 0.12)')).toBe('rgba(255, 255, 255, 0.12)');
  });
});

describe('contrast math (WCAG 2.1)', () => {
  it('reproduces the ratios the spec quotes (§4)', () => {
    const white = tmParseColor('#FEFEFE')!;
    const teal600 = tmParseColor('#316E80')!;
    const teal400 = tmParseColor('#4CA0B6')!;
    // action-teal = teal-600 for text-on-fill clears 4.5:1
    expect(tmContrastRatio(white, teal600)).toBeGreaterThan(4.5);
    expect(tmContrastRatio(white, teal600)).toBeCloseTo(5.67, 1);
    // canonical teal-400 carries white text at only 2.97:1
    expect(tmContrastRatio(white, teal400)).toBeLessThan(3);
    expect(tmContrastRatio(white, teal400)).toBeCloseTo(2.97, 1);
    // focus ring teal-500 clears 3:1 against the white field
    const teal500 = tmParseColor('#3E899D')!;
    expect(tmContrastRatio(teal500, white)).toBeGreaterThan(3);
  });

  it('parses hex forms and rgb()/rgba()', () => {
    expect(tmParseColor('#fff')).toEqual({ r: 255, g: 255, b: 255, a: 1 });
    expect(tmParseColor('#00172280')?.a).toBeCloseTo(0.5, 1);
    expect(tmParseColor('rgb(76, 160, 182)')).toEqual({ r: 76, g: 160, b: 182, a: 1 });
    expect(tmParseColor('rgba(255, 255, 255, 0.12)')?.a).toBeCloseTo(0.12);
    expect(tmParseColor('nonsense')).toBeNull();
    expect(tmRelativeLuminance(tmParseColor('#000000')!)).toBe(0);
    expect(tmRelativeLuminance(tmParseColor('#ffffff')!)).toBe(1);
  });

  it('composites semi-transparent colors over their background', () => {
    const semiWhite = tmParseColor('rgba(255, 255, 255, 0.14)')!;
    const darkField = tmParseColor('#16252D')!;
    // Composited border is barely lighter than the field — low ratio.
    const ratio = tmContrastRatio(semiWhite, darkField);
    expect(ratio).toBeGreaterThan(1);
    expect(ratio).toBeLessThan(2);
  });
});

describe('tmEmitCss', () => {
  const css = tmEmitCss(tmTokensDefault);

  it('starts every sheet with the canonical layer order statement', () => {
    const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
    const firstStatement = withoutComments.split('\n').find((l) => l.trim());
    expect(firstStatement?.trim()).toBe('@layer tm.base, tm.theme;');
  });

  it('emits both schemes inside @layer tm.base', () => {
    expect(css).toContain('@layer tm.base {');
    expect(css).toContain(':root {');
    expect(css).toContain('[data-theme=dark] {');
    expect(css).toContain('color-scheme: light;');
    expect(css).toContain('color-scheme: dark;');
  });

  it('reproduces the brand anchors', () => {
    expect(css).toContain('--teal-400: #4CA0B6;'); // canonical logo teal
    expect(css).toContain('--ink-900: #001722;'); // canonical wordmark ink
    expect(css).toContain('--field-height: 38px;');
    expect(css).toContain('--field-height-sm: 30px;');
    expect(css).toContain('--field-height-lg: 46px;');
    expect(css).toContain('--field-border-focus: var(--teal-500);');
    expect(css).toContain('--focus-ring-color: var(--teal-500);'); // spec §4, not the stale teal-400
    expect(css).toContain('--color-primary: var(--teal-600);');
    expect(css).toContain('--grey-900: var(--ink-900);');
  });

  it('overlays dark primitives so grey-built components flip automatically', () => {
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    expect(dark.get('--white')).toBe('#16252D');
    expect(dark.get('--grey-900')).toBe('#F2F7F8');
    expect(tmResolveVar(dark, '--field-text')).toBe('#F2F7F8');
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    expect(tmResolveVar(light, '--field-text')).toBe('#001722');
  });
});

describe('tmValidateTokens gates', () => {
  it('passes the default preset', () => {
    expect(tmValidateTokens(tmTokensDefault)).toEqual([]);
  });

  it('fails on a missing token reference', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      component: { badComponent: { color: '{does.not.exist}' } },
    };
    const issues = tmValidateTokens(broken);
    expect(issues.some((i) => i.gate === 'missing-ref')).toBe(true);
  });

  it('fails on a below-AA pair that is not excepted', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      contrastPairs: [
        ...tmTokensDefault.contrastPairs,
        // white text on the canonical teal-400: 2.97:1 — the spec's own example.
        { fg: '--white', bg: '--accent', kind: 'text' },
      ],
    };
    const issues = tmValidateTokens(broken);
    expect(issues.some((i) => i.gate === 'contrast' && i.message.includes('--accent'))).toBe(true);
  });

  it('fails on an exception with an empty reason', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      contrastExceptions: [
        ...tmTokensDefault.contrastExceptions,
        { fg: '--white', bg: '--accent', reason: '   ' },
      ],
    };
    const issues = tmValidateTokens(broken);
    expect(issues.some((i) => i.gate === 'exception')).toBe(true);
  });

  it('fails when an ink-carrying token has no declared pair (completeness)', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      contrastPairs: tmTokensDefault.contrastPairs.filter((p) => p.fg !== '--field-text'),
    };
    const issues = tmValidateTokens(broken);
    expect(
      issues.some((i) => i.gate === 'completeness' && i.message.includes('--field-text')),
    ).toBe(true);
  });
});
