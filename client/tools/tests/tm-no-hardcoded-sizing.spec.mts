import stylelint from 'stylelint';
import { describe, expect, it } from 'vitest';

const config = {
  plugins: ['./tools/stylelint/tm-no-hardcoded-sizing.mjs'],
  rules: {
    'tm/no-hardcoded-sizing': [true, { allow: ['1px'] }],
  },
};

async function warningsFor(code: string): Promise<string[]> {
  const result = await stylelint.lint({ code, config, cwd: process.cwd() });
  return result.results[0].warnings.map((w) => w.text);
}

describe('tm/no-hardcoded-sizing', () => {
  it('flags hardcoded sizing values', async () => {
    expect(await warningsFor('.tm-x { height: 38px; }')).toHaveLength(1);
    expect(await warningsFor('.tm-x { padding-inline: 12px; }')).toHaveLength(1);
    expect(await warningsFor('.tm-x { font-size: 0.875rem; }')).toHaveLength(1);
    expect(await warningsFor('.tm-x { margin: 4px 8px; }')).toHaveLength(2);
    expect(await warningsFor('.tm-x { inline-size: calc(100% - 16px); }')).toHaveLength(1);
    // A fallback inside var() must be a token too, not a literal.
    expect(await warningsFor('.tm-x { gap: var(--space-2, 8px); }')).toHaveLength(1);
  });

  it('allows tokens, zero, hairlines, and non-sizing properties', async () => {
    expect(await warningsFor('.tm-x { height: var(--field-height); }')).toHaveLength(0);
    expect(await warningsFor('.tm-x { margin: 0; }')).toHaveLength(0);
    expect(await warningsFor('.tm-x { max-block-size: 1px; }')).toHaveLength(0); // allowlisted
    expect(await warningsFor('.tm-x { border: 1px solid var(--field-border); }')).toHaveLength(0);
    expect(await warningsFor('.tm-x { line-height: 1.6; }')).toHaveLength(0);
    expect(await warningsFor('.tm-x { box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05); }')).toHaveLength(
      0,
    );
  });
});
