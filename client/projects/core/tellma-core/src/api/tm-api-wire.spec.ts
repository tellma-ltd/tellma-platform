// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError } from './tm-api-error';
import {
  TM_OUTDATED,
  tmCompareBuilds,
  tmDispositionFileName,
  tmKindOf,
  tmParseProblem,
  tmParseStepUp,
  tmParseVersionTags,
  tmRandomHex,
  tmRetryAfterMs,
} from './tm-api-wire';

// The HTTP layer's pure wire rules: the status-to-kind table, the headers it
// parses, the trace ids it draws and the build order it judges by.

describe('tmKindOf', () => {
  it('maps each row of the table', () => {
    expect(tmKindOf(400, 'query-invalid')).toBe('query-invalid');
    expect(tmKindOf(401, 'step-up-required')).toBe('step-up');
    expect(tmKindOf(404, 'route-not-found')).toBe(TM_OUTDATED);
    expect(tmKindOf(412, 'client-outdated')).toBe(TM_OUTDATED);
    expect(tmKindOf(503, null)).toBe('unavailable');
    expect(tmKindOf(504, 'timeout')).toBe('unavailable');
    expect(tmKindOf(504, null)).toBe('server');
  });

  it('maps a blob rejection to validation at any status', () => {
    expect(tmKindOf(415, 'blob-rejected')).toBe('validation');
  });

  it('maps a known code at an unexpected status to server', () => {
    expect(tmKindOf(400, 'forbidden')).toBe('server');
    expect(tmKindOf(200, 'validation')).toBe('server');
  });
});

describe('tmParseProblem', () => {
  it('reads a JSON object with a string code', () => {
    expect(tmParseProblem('{"code":"forbidden","status":403}')).toEqual({ code: 'forbidden', status: 403 });
  });

  it('refuses anything else', () => {
    for (const text of ['', '<html></html>', '[]', 'null', '{"code":7}', '{"title":"x"}']) {
      expect(tmParseProblem(text)).toBeNull();
    }
  });
});

describe('tmRetryAfterMs', () => {
  const now = Date.UTC(2026, 10, 11, 12, 0, 0);

  it('reads whole seconds and HTTP dates', () => {
    expect(tmRetryAfterMs('7', now)).toBe(7_000);
    expect(tmRetryAfterMs(' 0 ', now)).toBe(0);
    expect(tmRetryAfterMs('Wed, 11 Nov 2026 12:00:09 GMT', now)).toBe(9_000);
  });

  it('never waits a negative time, and ignores what it cannot read', () => {
    expect(tmRetryAfterMs('Wed, 11 Nov 2026 11:59:00 GMT', now)).toBe(0);
    expect(tmRetryAfterMs(null, now)).toBeNull();
    expect(tmRetryAfterMs('', now)).toBeNull();
    expect(tmRetryAfterMs('soon', now)).toBeNull();
    expect(tmRetryAfterMs('-3', now)).toBeNull();
  });
});

describe('tmParseVersionTags', () => {
  it('reads the tags by name', () => {
    expect(tmParseVersionTags('settings=1.9f04, permissions=1.3f2a')).toEqual({ settings: '1.9f04', permissions: '1.3f2a' });
  });

  it('is null when the header is absent or names none', () => {
    expect(tmParseVersionTags(null)).toBeNull();
    expect(tmParseVersionTags('')).toBeNull();
    expect(tmParseVersionTags('=x, y')).toBeNull();
  });
});

describe('tmParseStepUp', () => {
  it('reads the class and the age', () => {
    expect(tmParseStepUp('Bearer error="insufficient_user_authentication", acr_values="mfa", max_age=300')).toEqual({
      acr: 'mfa',
      maxAge: 300,
    });
    expect(tmParseStepUp('Bearer error="insufficient_user_authentication", acr_values="mfa"')).toEqual({ acr: 'mfa', maxAge: null });
  });

  it('is null for another challenge', () => {
    expect(tmParseStepUp(null)).toBeNull();
    expect(tmParseStepUp('Bearer error="invalid_token"')).toBeNull();
    expect(tmParseStepUp('Bearer error="insufficient_user_authentication"')).toBeNull();
  });
});

describe('tmDispositionFileName', () => {
  it('prefers filename*, then a quoted or a bare filename', () => {
    expect(tmDispositionFileName(`attachment; filename="a.xlsx"; filename*=UTF-8''%C3%A9t%C3%A9.xlsx`)).toBe('été.xlsx');
    expect(tmDispositionFileName('attachment; filename="Q1 \\"final\\".xlsx"')).toBe('Q1 "final".xlsx');
    expect(tmDispositionFileName('attachment; filename=report.xlsx')).toBe('report.xlsx');
  });

  it('falls back from a malformed filename* to filename', () => {
    expect(tmDispositionFileName(`attachment; filename*=UTF-8''%E0%A4; filename="plain.xlsx"`)).toBe('plain.xlsx');
  });

  it('is null without a name', () => {
    expect(tmDispositionFileName(null)).toBeNull();
    expect(tmDispositionFileName('inline')).toBeNull();
  });
});

describe('tmRandomHex', () => {
  it('writes two digits per byte', () => {
    expect(tmRandomHex(4, (array) => array.fill(0x0f))).toBe('0f0f0f0f');
  });

  it('never returns all zeros, which trace context forbids', () => {
    expect(tmRandomHex(8, (array) => array.fill(0))).toBe('0000000000000001');
  });
});

describe('tmCompareBuilds', () => {
  const ui = '1.4.0+3f9c2ab';

  it('orders by SemVer precedence, ignoring the commit', () => {
    expect(tmCompareBuilds(ui, '1.4.1+0000000')).toBe(1);
    expect(tmCompareBuilds(ui, '1.10.0')).toBe(1);
    expect(tmCompareBuilds(ui, '1.3.9+3f9c2ab')).toBe(-1);
    expect(tmCompareBuilds(ui, '1.4.0-rc.2')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc.10')).toBe(1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-2', '1.4.0-beta')).toBe(1);
    expect(tmCompareBuilds('1.4.0-beta', '1.4.0-alpha')).toBe(-1);
  });

  it('is 0 only for the identical build', () => {
    expect(tmCompareBuilds(ui, ui)).toBe(0);
  });

  it('is unordered for the same version with another commit, or for no version', () => {
    expect(tmCompareBuilds(ui, '1.4.0+9e8d7c6')).toBeNull();
    expect(tmCompareBuilds(ui, '1.4.0')).toBeNull();
    expect(tmCompareBuilds(ui, null)).toBeNull();
    expect(tmCompareBuilds(ui, 'nightly')).toBeNull();
    expect(tmCompareBuilds('dev', '1.4.0')).toBeNull();
  });
});

describe('TmApiError', () => {
  it('defaults its code to its kind and summarizes itself', () => {
    const error = new TmApiError({ kind: 'offline' });
    expect(error).toBeInstanceOf(Error);
    expect(error).toMatchObject({
      name: 'TmApiError',
      kind: 'offline',
      code: 'offline',
      status: 0,
      traceId: '',
      problem: null,
      retryAfterMs: null,
      challenge: null,
      message: 'The request failed: offline (offline).',
    });
  });
});
