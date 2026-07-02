import { TM_LOCALE_AR_VERSION } from './version';

describe('locale-ar workspace smoke', () => {
  it('exposes the version marker', () => {
    expect(TM_LOCALE_AR_VERSION).toBe('0.1.0');
  });
});
