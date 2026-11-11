// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The notification catalogue of the Core chunk: camelCase mirrors of the
// server's notification types (spec 0021 §2.5; client mirror in spec 0028
// §5.1). Members the server leaves null are absent.

/** How urgent a notification is. */
export type TmNotificationSeverity = 'Info' | 'Success' | 'Warning' | 'Error';

/** One notification type the composition registers. */
export interface TmNotificationTypeMetadata {
  /** The type's key. */
  readonly key: string;
  /** The category users manage their notification preferences by. */
  readonly category: string;
  /** Whether users may turn the type off. */
  readonly mutable: boolean;
  /** The type's severity. */
  readonly severity: TmNotificationSeverity;
  /** Whether the type shows a toast when it arrives. */
  readonly toast: boolean;
  /** Whether the type lands in the inbox. */
  readonly inbox: boolean;
  /** The resource a notification of the type links to. */
  readonly targetResource?: string;
}
