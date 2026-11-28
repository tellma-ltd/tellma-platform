// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Injectable,
  Injector,
  signal,
  untracked,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { TmClientCache } from '@tellma/core-ui';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import { TmButton } from '@tellma/core-ui/button';
import { TmConfirm } from '@tellma/core-ui/confirm';
import { TM_MODAL_DATA, TmModal, TmModalFooter } from '@tellma/core-ui/modal';
import { TmToasts } from '@tellma/core-ui/toast';

import { TM_API_HOST } from '../api/tm-api-host';
import { TmApi } from '../api/tm-api';
import type { TmStringTree } from '../app/tm-app-config';
import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmLocalization } from '../l10n/tm-localization';
import { TmTelemetry } from '../observe/tm-telemetry';
import type { TmBusMessage } from '../state/bus/tm-bus-messages';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TM_DRAFT_KEY_PREFIX, TM_DRAFTS_STORAGE, tmDraftPrefix, tmRemoveDrafts, tmStorageKeys } from '../state/memory/tm-draft-keys';
import { TmClientStore } from '../state/store/tm-client-store';
import { TM_STORE_DATABASE } from '../state/store/tm-store-layout';
import { TM_BOOT_RECORD_KEY } from '../state/tm-boot-record';
import { TM_STATE_PARTS } from '../state/tm-state-parts';
import { TmUrls } from '../urls/tm-urls';
import { tmParseProfileCookie } from './tm-profile';
import { TmSession } from './tm-session';
import { TM_SESSION_HOST } from './tm-session-host';
import { TM_PROFILE_COOKIE_DELETE, TmSessionPaths } from './tm-session-paths';

// The session's flows across tabs (spec 0024 §8.4-§8.8, spec 0025 §2.6,
// §6.2), in a chunk the primary loads after the first render:
//
// - sign-out: the busy overlay at once, the drafts query repeated every
//   second while any tab saves (at most the 60 s write watchdog), the one
//   confirm about what is still at risk, then the telemetry flush, the
//   signing-out broadcast, `/bff/logout`, the session clean-up and the
//   signed-out broadcast; a failure restores the session and shows a toast;
// - the other tabs' side: the signing-out overlay, lifted after 15 s with no
//   signed-out message; the signed-out page; the session-ended dialog; the
//   reload after the user signed in again; another user's session;
// - signed out elsewhere, which the leader tab hears from its hub;
// - the expiry path: the clean-up, the stale cookie, the broadcast and the
//   session-ended dialog, or the sign-in-failed page right after a sign-in;
// - the changed-user path, and the re-check triggers of §8.8.

/** The session flows' English strings, under `tm.session`. */
export const TM_SESSION_STRINGS_EN = {
  signingOut: 'Signing out…',
  waiting: 'Waiting for a save to finish…',
  signOut: 'Sign out',
  dirty: {
    title: 'Sign out and discard unsaved changes?',
    message: 'You have unsaved changes in {count, plural, one {# tab} other {# tabs}}.',
  },
  busy: {
    title: 'Sign out while a save is running?',
    message: "A save is still running. If you sign out now, you won't see whether it finished.",
  },
  failed: "Couldn't sign out. Check your connection and try again.",
  ended: {
    title: 'Your session ended',
    message: 'Sign in to continue. Your work is kept.',
  },
} as const satisfies TmStringTree;

/** The sign-out write's route. */
const LOGOUT_ROUTE = '/bff/logout';

/** How long a drafts query collects the other tabs' replies, in milliseconds. */
export const TM_DRAFTS_QUERY_MS = 300;

/** How often a waiting sign-out asks again, in milliseconds. */
const WAIT_STEP_MS = 1_000;

/** The longest a sign-out waits for running saves: the write watchdog's 60 s. */
export const TM_SIGN_OUT_WAIT_MS = 60_000;

/** How long another tab's sign-out may run before this tab lifts its overlay, in milliseconds. */
export const TM_SIGNING_OUT_LIFT_MS = 15_000;

/** The time box of the clean-up's sweep: the boot script's expiry delete's. */
const SWEEP_BOX_MS = 1_000;

/**
 * How long after a sign-in an expiry counts as the sign-in failing: the
 * window of the boot script's own sign-in loop guard.
 */
const SIGN_IN_LOOP_WINDOW_MS = 60_000;

/** What the user's tabs answered a drafts query: how many hold unsaved drafts, and whether any saves. */
interface TmDraftsAnswers {
  /** How many tabs hold unsaved drafts of the user. */
  readonly dirty: number;
  /** Whether any tab has a write in flight. */
  readonly busy: boolean;
}

/** What the session-ended dialog shows, and its one action. */
interface TmSessionEndedData {
  /** The id of the message, which describes the alert dialog. */
  readonly messageId: string;
  /** The message. */
  readonly message: string;
  /** The button's label. */
  readonly label: string;
  /** Signs in again. */
  readonly signIn: () => void;
}

/** A promise that settles after a delay. */
function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

let nextMessageId = 0;

/**
 * The session-ended dialog's content: the message, and the Sign in button,
 * which takes the focus. Internal.
 */
@Component({
  selector: 'tm-session-ended',
  imports: [TmButton, TmModalFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The message, which describes the alert dialog. -->
    <p [id]="data.messageId">{{ data.message }}</p>
    <!-- The one way on: signing in again. -->
    <div tmModalFooter>
      <button tmButton variant="primary" type="button" cdkFocusInitial (click)="data.signIn()">{{ data.label }}</button>
    </div>
  `,
})
export class TmSessionEndedDialog {
  /** What the dialog shows. */
  protected readonly data = inject<TmSessionEndedData>(TM_MODAL_DATA);
}

/** The session's flows across tabs. Internal: `TmSessionPaths` loads and calls them. */
@Injectable({ providedIn: 'root' })
export class TmSessionFlows {
  readonly #injector = inject(Injector);
  readonly #paths = inject(TmSessionPaths);
  readonly #host = inject(TM_SESSION_HOST);
  readonly #telemetry = inject(TmTelemetry);
  readonly #bus = inject(TmTabBus);
  readonly #l10n = inject(TmLocalization);
  readonly #now = inject(TM_NOW);
  readonly #boot = inject(TM_BOOT_RESULT);
  readonly #parts = inject(TM_STATE_PARTS);
  readonly #storage = inject(TM_DRAFTS_STORAGE);

  /** The sign-out in progress, which a second call joins. */
  #signingOut: Promise<void> | null = null;
  /** Whether the tab is leaving for good: signed out, or restarting for another user. */
  #leaving = false;
  /** Whether the session-ended dialog is open. */
  #ended = false;
  /** Lifts the overlay another tab's sign-out showed here, while it shows. */
  #lift: (() => void) | null = null;

  /** Registers the strings and starts the re-check triggers. */
  constructor() {
    this.#l10n.ɵregisterStrings('session', TM_SESSION_STRINGS_EN);
    // --- Re-check triggers (§8.8): the tab comes into view, or back from the back-forward cache ---
    // A `pageshow` after this chunk loaded is always a return from the
    // cache: the document's own first one fired long before.
    const host = inject(TM_API_HOST);
    const recheck = (): void => this.#recheck();
    const stops = [
      host.listen('document', 'visibilitychange', () => host.visible() && recheck()),
      host.listen('window', 'pageshow', recheck),
    ];
    inject(DestroyRef).onDestroy(() => stops.forEach((stop) => stop()));
  }

  /**
   * Handles another tab's session message or drafts query.
   *
   * @param message - The message.
   */
  received(message: TmBusMessage): void {
    const own = this.#sub();
    switch (message.type) {
      case 'drafts.query':
        // The query travels on the user's channel, so it is this user's.
        if (own !== null) {
          this.#bus.post({ type: 'drafts.reply', replyTo: message.replyTo, ...this.#answer(own) });
        }
        break;
      case 'session.signing-out':
        if (message.sub === own) {
          this.#otherSigningOut();
        }
        break;
      case 'session.signed-out':
        if (message.sub === own) {
          void this.#signedOut(own, 'other-tab');
        }
        break;
      case 'session.expired':
        if (message.sub === own && untracked(this.#paths.state) === 'active') {
          // The tab that found the expiry cleaned up; this one keeps its drafts.
          this.#paths.state.set('expired');
          this.#showEnded(() => void this.#session().signIn());
        }
        break;
      case 'session.started':
        if (message.sub !== own) {
          this.changedUser();
        } else if (untracked(this.#paths.state) !== 'active') {
          // The user signed in again in another tab: this one restores its drafts on reload.
          this.#host.location.reload();
        }
        break;
      case 'session.changed':
        if (message.sub !== own) {
          this.changedUser();
        }
        break;
    }
  }

  /**
   * Tells the user's other tabs that the session started, so the ones that
   * saw it end reload.
   *
   * @param sub - The signed-in user's subject identifier.
   */
  started(sub: string): void {
    this.#bus.post({ type: 'session.started', sub });
  }

  /**
   * Signs the user out of every tab.
   *
   * @returns A promise that never settles once the tab leaves; it resolves
   *   when the user cancels or no one is signed in, and rejects when the
   *   sign-out fails.
   */
  signOut(): Promise<void> {
    return (this.#signingOut ??= this.#signOut().finally(() => (this.#signingOut = null)));
  }

  /**
   * The rest of the expiry path, once the session turned expired: the
   * session clean-up, the stale cookie's delete and the broadcast, then the
   * session-ended dialog, whose Sign in waits for them. Right after a
   * sign-in the sign-in is failing, so the boot's sign-in-failed page shows
   * instead, and its Try again signs in.
   */
  expired(): void {
    const sub = this.#sub();
    // Spec 0024 §8.6 step 2: the expiry keeps the drafts, and the boot record's user and tenant.
    const done = this.#cleanUp(sub, false).then(() => {
      // After a 401 the cookie may still be present; deleting it keeps the
      // returning boot from running the app again into the same 401.
      this.#host.writeCookie(TM_PROFILE_COOKIE_DELETE);
      if (sub !== null) {
        this.#bus.post({ type: 'session.expired', sub });
      }
    });
    const signIn = (): void => void done.then(() => this.#session().signIn());
    // The boot's loop guard (spec 0024 §4.2 step 3b) only sees a sign-in that
    // comes back without a cookie; this one catches a sign-in that comes back
    // with a cookie the server then refuses.
    const boot = this.#boot;
    const since = boot?.mark ? this.#now() - boot.mark.at : -1;
    if (boot !== null && since >= 0 && since < SIGN_IN_LOOP_WINDOW_MS) {
      boot.showPage('sign-in-failed', signIn);
    } else {
      this.#showEnded(signIn);
    }
  }

  /**
   * Runs the changed-user path, once: the previous user's drafts go, the
   * other tabs hear of the new user, and the app restarts at the root,
   * whose boot sweeps the previous user's stored data.
   */
  changedUser(): void {
    if (this.#leaving) {
      return;
    }
    this.#leaving = true;
    this.#telemetry.event('tm.session.changed-user');
    this.#telemetry.flush();
    const previous = this.#sub();
    if (previous !== null) {
      this.#clearDrafts(previous);
    }
    const now = tmParseProfileCookie(this.#host.cookies());
    if (now !== null) {
      this.#bus.post({ type: 'session.changed', sub: now.sub });
    }
    this.#host.location.replace('/');
  }

  /**
   * Runs the signed-out-elsewhere path: this tab cleans up
   * for every tab, tells them, and shows the signed-out page.
   */
  signedOutElsewhere(): void {
    const sub = this.#sub();
    if (sub !== null) {
      void this.#signedOut(sub, 'elsewhere');
    }
  }

  // Spec 0024 §8.4.
  /** The sign-out's steps. */
  async #signOut(): Promise<void> {
    const sub = this.#sub();
    if (sub === null || untracked(this.#paths.state) !== 'active') {
      return;
    }
    // --- 1-2. The overlay at once, and the wait while any tab saves ---
    const overlay = this.#injector.get(TmBusyOverlay);
    const waiting = signal(false);
    const signingOut = this.#text('signingOut');
    const waitingFor = this.#text('waiting');
    const options = { label: computed(() => (waiting() ? waitingFor() : signingOut())), immediate: true };
    const answers = await overlay.run(
      (async () => {
        const found = await this.#wait(sub, this.#now() + TM_SIGN_OUT_WAIT_MS, waiting);
        // --- 3-4. Nothing at risk: sign out with no question ---
        if (found.dirty === 0 && !found.busy) {
          await this.#leave(sub);
        }
        return found;
      })(),
      options,
    );
    // --- 3. Something at risk: the overlay closes and the confirm asks once ---
    if (!(await this.#confirm(answers))) {
      return;
    }
    // --- 4. A confirmed sign-out waits, without asking again, for a save
    // that started while the confirm was open; a save it already waited the
    // full minute for is not waited for again ---
    waiting.set(false);
    await overlay.run(
      (async () => {
        if (!answers.busy) {
          await this.#wait(sub, this.#now() + TM_SIGN_OUT_WAIT_MS, waiting);
          waiting.set(false);
        }
        await this.#leave(sub);
      })(),
      options,
    );
  }

  /**
   * Asks the user's tabs about their unsaved work until none saves, or
   * until the deadline passes.
   *
   * @param sub - The user.
   * @param deadline - When the wait ends, in epoch milliseconds.
   * @param waiting - Set while a save runs, so the overlay says so.
   * @returns The last answers.
   */
  async #wait(sub: string, deadline: number, waiting: WritableSignal<boolean>): Promise<TmDraftsAnswers> {
    let answers = await this.#ask(sub);
    while (answers.busy && this.#now() < deadline) {
      waiting.set(true);
      // One query a second: the query's own window, then the rest of the second.
      await delay(WAIT_STEP_MS - TM_DRAFTS_QUERY_MS);
      answers = await this.#ask(sub);
    }
    return answers;
  }

  /**
   * Posts a drafts query and collects the answers of the tabs that reply
   * within the window, this tab's own included.
   *
   * @param sub - The user.
   * @returns The answers.
   */
  async #ask(sub: string): Promise<TmDraftsAnswers> {
    const replyTo = crypto.randomUUID();
    const replies = [this.#answer(sub)];
    const subscription = this.#bus.messages.subscribe((message) => {
      if (message.type === 'drafts.reply' && message.replyTo === replyTo) {
        replies.push(message);
      }
    });
    this.#bus.post({ type: 'drafts.query', replyTo });
    await delay(TM_DRAFTS_QUERY_MS);
    subscription.unsubscribe();
    return { dirty: replies.filter((reply) => reply.dirty > 0).length, busy: replies.some((reply) => reply.busy) };
  }

  /**
   * This tab's answer to a drafts query: its unsaved drafts of the user,
   * counted by the drafts service once it exists and in session storage
   * before, and whether it has a write in flight.
   *
   * @param sub - The user.
   * @returns The answer.
   */
  #answer(sub: string): { dirty: number; busy: boolean } {
    const service = this.#parts.draftsService;
    return {
      dirty: service ? untracked(service.dirtyCount) : tmStorageKeys(this.#storage, tmDraftPrefix(sub)).length,
      busy: untracked(this.#injector.get(TmApi).writesInFlight) > 0,
    };
  }

  /**
   * Asks once about what is still at risk, when anything is: unsaved
   * drafts, a save still running, or both.
   *
   * @param answers - The tabs' last answers.
   * @returns Whether to sign out.
   */
  #confirm(answers: TmDraftsAnswers): Promise<boolean> {
    const dirty = answers.dirty > 0;
    const parts = [dirty && this.#text('dirty.message', { count: answers.dirty })(), answers.busy && this.#text('busy.message')()];
    return this.#injector.get(TmConfirm).confirm({
      title: this.#text(dirty ? 'dirty.title' : 'busy.title')(),
      message: parts.filter((part) => part !== false).join(' '),
      confirmLabel: this.#text('signOut')(),
      destructive: true,
    });
  }

  /**
   * Leaves: the telemetry goes first, then every tab turns signing-out, the
   * BFF ends the session, the clean-up runs, the other tabs hear it, and
   * the tab follows the identity server's sign-out. A failure restores the
   * session and says so.
   *
   * @param sub - The user.
   * @returns A promise that never settles once the tab leaves.
   */
  async #leave(sub: string): Promise<never> {
    this.#telemetry.event('tm.session.signed-out', { origin: 'self' });
    this.#telemetry.flush();
    this.#paths.state.set('signing-out');
    this.#bus.post({ type: 'session.signing-out', sub });
    let redirect: unknown;
    try {
      redirect = (await this.#injector.get(TmApi).write<{ readonly redirectUrl?: unknown } | null>(LOGOUT_ROUTE, { scope: 'global' }))
        ?.redirectUrl;
    } catch (error) {
      // Spec 0028 §8.1: a failure is a sticky toast; the other tabs lift their overlay after 15 s.
      this.#paths.state.set('active');
      this.#injector.get(TmToasts).show({ tone: 'error', title: this.#text('failed')(), duration: 'sticky' });
      throw error;
    }
    await this.#cleanUp(sub, true);
    this.#bus.post({ type: 'session.signed-out', sub });
    // The BFF answers its end-session URL; the signed-out page is the
    // local sign-out's, and the fallback for an answer without one.
    this.#host.location.assign(typeof redirect === 'string' && redirect !== '' ? redirect : this.#injector.get(TmUrls).signedOut());
    return new Promise<never>(() => undefined);
  }

  /**
   * Shows the signing-out overlay of another tab's sign-out. Without the
   * signed-out message within 15 s, the sign-out failed or its tab closed,
   * so the overlay lifts and the tab re-checks its cookie.
   */
  #otherSigningOut(): void {
    if (untracked(this.#paths.state) !== 'active') {
      return;
    }
    this.#paths.state.set('signing-out');
    let lift!: () => void;
    const lifted = new Promise<void>((resolve) => (lift = resolve));
    void this.#injector.get(TmBusyOverlay).run(lifted, { label: this.#text('signingOut'), immediate: true });
    const timer = setTimeout(() => {
      this.#lift = null;
      lift();
      this.#paths.state.set('active');
      this.#recheck();
    }, TM_SIGNING_OUT_LIFT_MS);
    this.#lift = () => {
      clearTimeout(timer);
      this.#lift = null;
    };
  }

  /**
   * Leaves for the signed-out page: the user signed out in another tab, or
   * the server ended the session. The tab that heard the server cleans up
   * for every tab first and tells them; every other tab clears its own
   * session storage.
   *
   * @param sub - The user.
   * @param origin - Where the sign-out came from.
   */
  async #signedOut(sub: string, origin: 'other-tab' | 'elsewhere'): Promise<void> {
    if (this.#leaving) {
      return;
    }
    this.#leaving = true;
    this.#lift?.();
    this.#paths.state.set('signing-out');
    this.#telemetry.event('tm.session.signed-out', { origin });
    if (origin === 'elsewhere') {
      await this.#cleanUp(sub, true);
      this.#bus.post({ type: 'session.signed-out', sub });
    } else {
      // The signing-out tab swept the shared stores; a tab's session storage is its own.
      await this.#injector.get(TmClientCache).clearAll({ keep: (_name, area) => area !== 'session-storage' });
      this.#clearDrafts(sub);
    }
    this.#host.writeCookie(TM_PROFILE_COOKIE_DELETE);
    this.#host.location.replace(this.#injector.get(TmUrls).signedOut());
  }

  /**
   * The session clean-up: every user's stored data, in a
   * time box; every other `tm` store but the store's database and the boot
   * record, and on expiry the drafts; then, on a sign-out, the boot
   * record's user and last tenant and the user's drafts. An expiry whose
   * sweep did not apply leaves the boot record naming no user, so the next
   * launch sweeps again; it keeps the last tenant.
   *
   * @param sub - The user, or `null`.
   * @param signOut - Whether the session ended by signing out, rather than expiring.
   */
  async #cleanUp(sub: string | null, signOut: boolean): Promise<void> {
    const store = this.#injector.get(TmClientStore);
    let box: ReturnType<typeof setTimeout> | undefined;
    const swept = await Promise.race([
      // A sweep that could not reach the disk counts as cut short.
      store.sweep('session-ended').catch(() => false),
      new Promise<false>((resolve) => (box = setTimeout(() => resolve(false), SWEEP_BOX_MS))),
    ]);
    clearTimeout(box);
    await this.#injector.get(TmClientCache).clearAll({
      keep: (name) => name === TM_STORE_DATABASE || name === TM_BOOT_RECORD_KEY || (!signOut && name.startsWith(TM_DRAFT_KEY_PREFIX)),
    });
    if (signOut) {
      store.boot.patch({ sub: null, lastTenantId: null });
      if (sub !== null) {
        this.#clearDrafts(sub);
      }
    } else if (!swept) {
      store.boot.patch({ sub: null });
    }
  }

  /**
   * Opens the session-ended dialog, once: an alert dialog whose only way
   * on is Sign in, so Escape, the backdrop and history navigation do nothing.
   *
   * @param signIn - What Sign in does.
   */
  #showEnded(signIn: () => void): void {
    if (this.#ended) {
      return;
    }
    this.#ended = true;
    const messageId = `tm-session-ended-${nextMessageId++}`;
    this.#injector.get(TmModal).open(TmSessionEndedDialog, {
      size: 'sm',
      role: 'alertdialog',
      title: this.#text('ended.title')(),
      describedBy: messageId,
      showClose: false,
      backdropDismiss: false,
      escapeDismiss: false,
      // Back or Forward must not close it either: the session is gone, and
      // only Sign in leads on.
      closeOnNavigation: false,
      data: {
        messageId,
        message: this.#text('ended.message')(),
        label: this.#l10n.ɵtext('tm.shell.signedOut.signIn')(),
        signIn,
      } satisfies TmSessionEndedData,
    });
  }

  /**
   * Re-checks the profile cookie of a signed-in tab, as a tab does when it
   * comes back into view: no session runs the expiry path, and another
   * user's the changed-user path.
   */
  #recheck(): void {
    const session = this.#session();
    if (untracked(this.#paths.state) === 'active' && untracked(session.profile) !== null) {
      const check = session.check();
      if (check === 'missing') {
        this.#paths.expired('cookie');
      } else if (check === 'changed') {
        this.changedUser();
      }
    }
  }

  /** Deletes a user's drafts: through the drafts service once it exists, so no pending save comes back. */
  #clearDrafts(sub: string): void {
    const service = this.#parts.draftsService;
    if (service) {
      service.clearAll(sub);
    } else {
      tmRemoveDrafts(this.#storage, sub);
    }
  }

  /** The tab's session. */
  #session(): TmSession {
    return this.#injector.get(TmSession);
  }

  /** The tab's user, or `null`. */
  #sub(): string | null {
    return untracked(this.#session().profile)?.sub ?? null;
  }

  /** One of the flows' strings. */
  #text(key: string, params?: Readonly<Record<string, unknown>>): Signal<string> {
    return this.#l10n.ɵtext(`tm.session.${key}`, params);
  }
}
