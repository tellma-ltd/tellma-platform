// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** The renderer families of the preview. */
export type TmPreviewKind = 'image' | 'svg' | 'pdf' | 'video' | 'audio' | 'text' | 'unsupported';

/** The plain-text MIME allowlist (never HTML). */
const TEXT_TYPES = new Set([
  'text/plain',
  'text/csv',
  'application/json',
  'text/json',
  'application/xml',
  'text/xml',
]);

const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'webp', 'gif', 'avif', 'bmp']);
const VIDEO_EXTENSIONS = new Set(['mp4', 'webm', 'ogv', 'm4v', 'mov']);
const AUDIO_EXTENSIONS = new Set(['mp3', 'wav', 'ogg', 'oga', 'm4a', 'flac', 'aac']);
const TEXT_EXTENSIONS = new Set(['txt', 'csv', 'json', 'xml', 'log']);

/**
 * Detects the renderer kind from the MIME type, else the file extension —
 * never from content sniffing; anything undetected is unsupported.
 * HTML is deliberately never a kind: blob URLs inherit the app origin, so
 * user-authored active content is a same-origin XSS vector — download-only
 * is the policy.
 */
export function tmDetectPreviewKind(name: string, type: string | undefined): TmPreviewKind {
  const mime = type?.toLowerCase().split(';')[0].trim() ?? '';
  if (mime !== '' && mime !== 'application/octet-stream') {
    if (mime === 'image/svg+xml') {
      return 'svg';
    }
    if (mime.startsWith('image/')) {
      return 'image';
    }
    if (mime === 'application/pdf') {
      return 'pdf';
    }
    if (mime.startsWith('video/')) {
      return 'video';
    }
    if (mime.startsWith('audio/')) {
      return 'audio';
    }
    if (TEXT_TYPES.has(mime)) {
      return 'text';
    }
    return 'unsupported';
  }
  const extension = name.toLowerCase().split('.').pop() ?? '';
  if (extension === 'svg') {
    return 'svg';
  }
  if (IMAGE_EXTENSIONS.has(extension)) {
    return 'image';
  }
  if (extension === 'pdf') {
    return 'pdf';
  }
  if (VIDEO_EXTENSIONS.has(extension)) {
    return 'video';
  }
  if (AUDIO_EXTENSIONS.has(extension)) {
    return 'audio';
  }
  if (TEXT_EXTENSIONS.has(extension)) {
    return 'text';
  }
  return 'unsupported';
}
