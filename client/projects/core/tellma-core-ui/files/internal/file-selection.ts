// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmFileSelection } from '../tm-file-selection';

/** The guardrail options one selection runs under. */
export interface TmFileSelectionOptions {
  /** Native accept string: extensions and MIME types/wildcards, comma-separated. */
  readonly accept: string;
  /** `false` accepts the first file and rejects the rest with `'count'`. */
  readonly multiple: boolean;
  /** Per-file byte ceiling. */
  readonly maxFileSize: number;
  /** Selection-count ceiling (`null` = unbounded; `multiple: false` caps at 1). */
  readonly maxFiles: number | null;
}

/** One parsed accept pattern. */
type AcceptPattern =
  | { readonly kind: 'extension'; readonly suffix: string }
  | { readonly kind: 'mimeExact'; readonly type: string }
  | { readonly kind: 'mimePrefix'; readonly prefix: string };

function parseAccept(accept: string): AcceptPattern[] {
  return accept
    .split(',')
    .map((token) => token.trim().toLowerCase())
    .filter((token) => token !== '')
    .map((token) => {
      if (token.startsWith('.')) {
        return { kind: 'extension', suffix: token } as const;
      }
      if (token.endsWith('/*')) {
        return { kind: 'mimePrefix', prefix: token.slice(0, -1) } as const;
      }
      return { kind: 'mimeExact', type: token } as const;
    });
}

function matches(file: File, patterns: readonly AcceptPattern[]): boolean {
  if (patterns.length === 0) {
    return true;
  }
  const name = file.name.toLowerCase();
  const type = file.type.toLowerCase();
  return patterns.some((pattern) => {
    switch (pattern.kind) {
      case 'extension':
        return name.endsWith(pattern.suffix);
      case 'mimePrefix':
        return type.startsWith(pattern.prefix);
      case 'mimeExact':
        return type === pattern.type;
    }
  });
}

/**
 * The one selection engine behind `tmFilePicker` and `tm-dropzone`: runs
 * the files through the guardrails in a per-file order that never lets an
 * already-rejected file consume a count slot — folder, then type, then
 * size, then count.
 *
 * `folderFlags[i]` marks `files[i]` as a directory drop (only drops can
 * produce those); `null` when the source cannot contain folders.
 */
export function tmSelectFiles(
  files: readonly File[],
  folderFlags: readonly boolean[] | null,
  options: TmFileSelectionOptions,
): TmFileSelection {
  const patterns = parseAccept(options.accept);
  const limit = options.multiple ? (options.maxFiles ?? Number.POSITIVE_INFINITY) : 1;
  const selection: TmFileSelection = { accepted: [], rejected: [] };
  files.forEach((file, index) => {
    if (folderFlags?.[index] === true) {
      selection.rejected.push({ file, reason: 'folder' });
    } else if (!matches(file, patterns)) {
      selection.rejected.push({ file, reason: 'type' });
    } else if (file.size > options.maxFileSize) {
      selection.rejected.push({ file, reason: 'size' });
    } else if (selection.accepted.length >= limit) {
      selection.rejected.push({ file, reason: 'count' });
    } else {
      selection.accepted.push(file);
    }
  });
  return selection;
}
