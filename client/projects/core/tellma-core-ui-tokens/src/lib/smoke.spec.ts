import { TM_CONTRACTS_ENTRY_POINT } from '@tellma/core-ui/contracts';

import { TM_CORE_UI_TOKENS_VERSION } from './version';

describe('core-ui-tokens workspace smoke', () => {
  it('exposes the version marker', () => {
    expect(TM_CORE_UI_TOKENS_VERSION).toBe('0.1.0');
  });

  it('resolves the @tellma/core-ui/contracts secondary entry point via path mapping', () => {
    expect(TM_CONTRACTS_ENTRY_POINT).toBe('@tellma/core-ui/contracts');
  });
});
