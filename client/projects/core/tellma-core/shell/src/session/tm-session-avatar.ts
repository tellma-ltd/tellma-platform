// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TmAvatar, type TmAvatarSize } from '@tellma/core-ui/avatar';

import { TmShellIdentity } from '../app-bar/tm-shell-identity';

// The user's avatar in the tenant shell (spec 0027 §7.4, §10.1): the photo
// when the user has one, else the initials on the tone of the user's record
// key, so they read the same here as in every list of the tenant. The
// session button shows it at 24 px in place of its first frame's initials,
// and the session menu's header at 28 px. It is decorative: the button and
// the header carry the user's name.

/** The signed-in user's avatar, decorative, from who the shell says the user is. */
@Component({
  selector: 'tm-session-avatar',
  imports: [TmAvatar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents' },
  template: `<tm-avatar decorative [size]="size()" [name]="identity.userName()" [src]="identity.avatarSrc()" [colorKey]="identity.colorKey()" />`,
})
export class TmSessionAvatar {
  /** Who the user is. */
  protected readonly identity = inject(TmShellIdentity);

  /** The diameter; `sm` (24 px) on the session button. */
  readonly size = input<TmAvatarSize>('sm');
}
