// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmClipboardFingerprint,
  tmParseTsv,
  tmSerializeHtmlTable,
  tmSerializeTsv,
  tmSerializeTsvChunks,
  type TmGridClipboardMeta,
} from './tm-grid-clipboard-serialize';

/** Reverses the attribute escaping (`&amp;` last, so entities survive). */
function unescapeAttribute(text: string): string {
  return text
    .replaceAll('&quot;', '"')
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&');
}

describe('tmSerializeTsv', () => {
  it('joins plain cells with tabs and ends every row with CRLF, including the last', () => {
    expect(
      tmSerializeTsv([
        ['a', 'b'],
        ['c', 'd'],
      ]),
    ).toBe('a\tb\r\nc\td\r\n');
  });

  it('quotes only cells containing a tab, quote, or newline', () => {
    expect(tmSerializeTsv([['a, b c', 'x\ty', 'l\nf', 'c\rr']])).toBe(
      'a, b c\t"x\ty"\t"l\nf"\t"c\rr"\r\n',
    );
  });

  it('doubles embedded quotes', () => {
    expect(tmSerializeTsv([['say "hi"']])).toBe('"say ""hi"""\r\n');
  });

  it('round-trips through tmParseTsv for tabs, quotes, mixed newlines, and empty cells', () => {
    const matrix = [
      ['plain', 'tab\there', ''],
      ['quote "x"', 'crlf\r\nline', 'lf\nline'],
      ['', '""', 'end'],
    ];
    expect(tmParseTsv(tmSerializeTsv(matrix))).toEqual(matrix);
  });
});

describe('tmSerializeTsvChunks', () => {
  const matrix = [['r1'], ['r2'], ['r3'], ['r4'], ['r5']];

  it('yields rowsPerChunk rows per chunk, concatenating to the full serialization', () => {
    const chunks = [...tmSerializeTsvChunks(matrix, 2)];
    expect(chunks).toEqual(['r1\r\nr2\r\n', 'r3\r\nr4\r\n', 'r5\r\n']);
    expect(chunks.join('')).toBe(tmSerializeTsv(matrix));
  });

  it('floors a fractional rowsPerChunk', () => {
    expect([...tmSerializeTsvChunks(matrix, 2.9)]).toEqual([
      'r1\r\nr2\r\n',
      'r3\r\nr4\r\n',
      'r5\r\n',
    ]);
  });

  it('clamps rowsPerChunk below 1 to one row per chunk', () => {
    for (const rowsPerChunk of [0, 0.5, -3]) {
      const chunks = [...tmSerializeTsvChunks(matrix, rowsPerChunk)];
      expect(chunks).toHaveLength(5);
      expect(chunks.join('')).toBe(tmSerializeTsv(matrix));
    }
  });
});

describe('tmParseTsv', () => {
  it('parses unquoted cells split on tabs and row separators', () => {
    expect(tmParseTsv('a\tb\r\nc\td')).toEqual([
      ['a', 'b'],
      ['c', 'd'],
    ]);
  });

  it('parses quoted fields with embedded tabs, newlines, and doubled quotes', () => {
    expect(tmParseTsv('"a\tb"\t"c\nd"\t"e""f"')).toEqual([['a\tb', 'c\nd', 'e"f']]);
  });

  it('treats a quote appearing mid-field as a literal character', () => {
    expect(tmParseTsv('ab"cd\tx"y"z')).toEqual([['ab"cd', 'x"y"z']]);
  });

  it('takes the rest of the input for an unterminated quote', () => {
    expect(tmParseTsv('"abc\tdef\r\nghi')).toEqual([['abc\tdef\r\nghi']]);
  });

  it('splits rows on LF and CRLF alike', () => {
    expect(tmParseTsv('a\nb\r\nc\nd')).toEqual([['a'], ['b'], ['c'], ['d']]);
  });

  it('produces no phantom row for a single trailing newline', () => {
    expect(tmParseTsv('a\tb\r\n')).toEqual([['a', 'b']]);
    expect(tmParseTsv('a\tb\n')).toEqual([['a', 'b']]);
  });

  it('strips a leading BOM', () => {
    expect(tmParseTsv('\uFEFFa\tb')).toEqual([['a', 'b']]);
  });

  it('pads ragged rows to a rectangle with empty strings', () => {
    expect(tmParseTsv('a\tb\tc\nd\ne\tf')).toEqual([
      ['a', 'b', 'c'],
      ['d', '', ''],
      ['e', 'f', ''],
    ]);
  });

  it('returns an empty matrix for the empty string', () => {
    expect(tmParseTsv('')).toEqual([]);
  });
});

describe('tmSerializeHtmlTable', () => {
  it('produces a table whose meta attribute round-trips through attribute unescaping', () => {
    const meta: TmGridClipboardMeta = {
      v: 1,
      tenant: 'he said "hi" & left',
      locale: 'en',
      cols: [
        { key: 'a', type: 'text' },
        { key: null, type: 'custom' },
      ],
    };
    const html = tmSerializeHtmlTable({ matrix: [['x']], meta });
    expect(html.startsWith('<table data-tm-grid="')).toBe(true);
    expect(html.endsWith('</tbody></table>')).toBe(true);
    expect(html).toContain('<tbody><tr><td>x</td></tr></tbody>');
    const match = /data-tm-grid="([^"]*)"/.exec(html);
    if (match === null) {
      throw new Error('expected a data-tm-grid attribute');
    }
    expect(JSON.parse(unescapeAttribute(match[1]))).toEqual(meta);
  });

  it('prepends a thead row only when headerRow is given', () => {
    const withHeader = tmSerializeHtmlTable({
      matrix: [['x']],
      meta: { v: 1, headers: true },
      headerRow: ['H & <em>', 'H2'],
    });
    expect(withHeader).toContain('<thead><tr><th>H &amp; &lt;em&gt;</th><th>H2</th></tr></thead>');
    const without = tmSerializeHtmlTable({ matrix: [['x']], meta: { v: 1 } });
    expect(without).not.toContain('<thead>');
  });

  it('emits per-row data-tm-rowid when rowIds are given', () => {
    const html = tmSerializeHtmlTable({ matrix: [['x'], ['y']], meta: { v: 1 }, rowIds: [7, 'r"1'] });
    expect(html).toContain('<tr data-tm-rowid="7"><td>x</td></tr>');
    expect(html).toContain('<tr data-tm-rowid="r&quot;1"><td>y</td></tr>');
    const bare = tmSerializeHtmlTable({ matrix: [['x']], meta: { v: 1 } });
    expect(bare).toContain('<tr><td>x</td></tr>');
    expect(bare).not.toContain('data-tm-rowid');
  });

  it('emits data-tm-v only for JSON-safe primitive raw values', () => {
    const html = tmSerializeHtmlTable({
      matrix: [['s', '42', 'yes', '', 'date', 'obj', 'nan', 'none']],
      meta: { v: 1 },
      rawValues: [
        [
          { value: 's' },
          { value: 42 },
          { value: true },
          { value: null },
          { value: new Date(0) },
          { value: { x: 1 } },
          { value: Number.NaN },
          undefined,
        ],
      ],
    });
    // Each typed cell also carries data-tm-h — the fingerprint of its display
    // text — so a paste can tell a faithful round trip from a tampered one.
    expect(html).toContain(
      `<td data-tm-v="&quot;s&quot;" data-tm-h="${tmClipboardFingerprint('s')}">s</td>`,
    );
    expect(html).toContain(`<td data-tm-v="42" data-tm-h="${tmClipboardFingerprint('42')}">42</td>`);
    expect(html).toContain(
      `<td data-tm-v="true" data-tm-h="${tmClipboardFingerprint('yes')}">yes</td>`,
    );
    expect(html).toContain(`<td data-tm-v="null" data-tm-h="${tmClipboardFingerprint('')}"></td>`);
    // A value JSON would distort (or that is absent) renders a plain cell —
    // no data-tm-v, and so no data-tm-h either.
    expect(html).toContain('<td>date</td>');
    expect(html).toContain('<td>obj</td>');
    expect(html).toContain('<td>nan</td>');
    expect(html).toContain('<td>none</td>');
    expect(html).not.toContain('<td data-tm-h');
  });

  it('fingerprints the canonical display text (trimmed, <br>-free) for data-tm-h', () => {
    // A multi-line, padded display string: the fingerprint is taken over the
    // canonical form (surrounding whitespace trimmed, newlines as '\n') so it
    // matches what the reducer reconstructs from the cell (`<br>` → '\n',
    // trim). The raw newline is serialized as <br> in the cell text.
    const html = tmSerializeHtmlTable({
      matrix: [['  a\nb  ']],
      meta: { v: 1 },
      rawValues: [[{ value: 'a\nb' }]],
    });
    expect(html).toContain(`data-tm-h="${tmClipboardFingerprint('a\nb')}"`);
    expect(html).toContain('>  a<br>b  </td>');
  });

  it('escapes text content (& < >) and attribute values (")', () => {
    const html = tmSerializeHtmlTable({ matrix: [['a & b < c > d']], meta: { v: 1 } });
    expect(html).toContain('<td>a &amp; b &lt; c &gt; d</td>');
    expect(html).toContain('data-tm-grid="{&quot;v&quot;:1}"');
  });
});

describe('tmClipboardFingerprint', () => {
  it('is deterministic', () => {
    expect(tmClipboardFingerprint('hello\tworld\r\n')).toBe(
      tmClipboardFingerprint('hello\tworld\r\n'),
    );
  });

  it('differs for different strings of the same length', () => {
    expect(tmClipboardFingerprint('hello')).not.toBe(tmClipboardFingerprint('hellO'));
  });

  it('incorporates the input length', () => {
    expect(tmClipboardFingerprint('abc').startsWith('3-')).toBe(true);
    expect(tmClipboardFingerprint('x'.repeat(36)).startsWith('10-')).toBe(true);
    expect(tmClipboardFingerprint('a')).not.toBe(tmClipboardFingerprint('aa'));
  });
});
