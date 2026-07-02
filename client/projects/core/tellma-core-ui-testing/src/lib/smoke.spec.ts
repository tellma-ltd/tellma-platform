import { TM_CORE_UI_TESTING_VERSION } from './version';

describe('core-ui-testing workspace smoke', () => {
  it('exposes the version marker', () => {
    expect(TM_CORE_UI_TESTING_VERSION).toBe('0.1.0');
  });
});
