// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmDispositionFileName, tmReadDownload } from './tm-api-transfer';

// A download's body: the streamed read with its progress, and the file name.
// The upload's send is covered through the HTTP layer's own tests.

describe('tmReadDownload', () => {
  it('streams the body, reporting the bytes and rearming the watchdog at each chunk', async () => {
    const encoder = new TextEncoder();
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode('abc'));
        controller.enqueue(encoder.encode('defg'));
        controller.close();
      },
    });
    const response = new Response(body, {
      headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename="q1.csv"' },
    });
    const progress: number[] = [];
    let rearms = 0;
    const download = await tmReadDownload(response, () => rearms++, (bytes) => progress.push(bytes));
    expect(download).toMatchObject({ fileName: 'q1.csv', contentType: 'text/csv' });
    expect(await download.blob.text()).toBe('abcdefg');
    expect(progress).toEqual([3, 7]);
    expect(rearms).toBe(2);
  });

  it('names an empty, unnamed body "download"', async () => {
    const download = await tmReadDownload(new Response(null, { status: 204 }), () => undefined, undefined);
    expect(download).toMatchObject({ fileName: 'download', contentType: '' });
    expect(download.blob.size).toBe(0);
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
