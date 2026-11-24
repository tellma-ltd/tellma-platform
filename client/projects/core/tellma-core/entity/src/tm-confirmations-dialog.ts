// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, type Injector, type Signal } from '@angular/core';
import { TmApiError, TmDisplayNames, TmLocalization, TmServerText, type TmId, type TmJson, type TmJsonObject, type TmStringTree } from '@tellma/core';
import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TM_MODAL_DATA, TmModal, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

import type { TmConfirmationAttempt, TmConfirmationContext } from './tm-confirmations';

// The confirmation loop's rounds after the first warning (spec 0031 §9.3),
// which `TmConfirmations` loads on the first 409 `confirmation-required`:
// the alert dialog that lists the warnings, each with up to five of its rows
// by display name, and the resends. Its strings ship here, under
// `tm.confirmations`, since only this chunk reads them.

/**
 * The confirmation dialog's English strings, under `tm.confirmations`.
 * Internal: the dialog registers them.
 */
export const ɵTM_CONFIRMATIONS_STRINGS_EN = {
  title: 'Confirm to continue',
  continue: 'Continue',
  skip: '{n, plural, one {Continue without this one} other {Continue without these #}}',
  more: 'and {n} more',
} as const satisfies TmStringTree;

/** The most warning responses one run accepts: the next one fails as a server error. */
const MAX_ROUNDS = 4;

/** The most keys one request confirms, as `TmApi.write` admits them. */
const MAX_KEYS = 16;

/** The most rows the dialog names per warning, before "and {n} more". */
const MAX_ROWS = 5;

/** One warning of a 409 `confirmation-required`, as `errorDetails.confirmations` lists them. */
interface TmWarning {
  /** The warning's key, which a resend confirms. */
  readonly key: string;
  /** The rows it names; none when it is about the whole request. */
  readonly ids: readonly TmId[];
  /** Its message's arguments. */
  readonly arguments?: TmJson;
}

/** One warning as the dialog shows it. */
interface TmWarningView {
  /** The warning's message. */
  readonly message: Signal<string>;
  /** The display names of its first rows. */
  readonly rows: readonly Signal<string | null>[];
  /** "and {n} more", when it names more rows than it shows. */
  readonly more: Signal<string> | null;
}

/** What the confirmation dialog shows. */
interface TmConfirmationsData {
  /** The pending warnings. */
  readonly warnings: readonly TmWarningView[];
  /** The label of "Continue without these", when it is offered. */
  readonly skip: Signal<string> | null;
  /** The warning list's id, the dialog's description. */
  readonly listId: string;
}

/** The dialog's answers: confirm every warning, or leave out the rows they name. */
type TmConfirmationAnswer = 'continue' | 'skip';

/** The warning lists' per-dialog ids. */
let nextId = 0;

/**
 * The confirmation dialog's body and footer: each warning's message, its
 * rows and "and {n} more", then Cancel, "Continue without these" when it
 * applies, and Continue. Internal: opened by the confirmation loop.
 */
@Component({
  selector: 'tm-confirmations-dialog',
  imports: [TmButton, TmModalFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .tm-confirmations {
      margin: 0;
      padding: 0;
      list-style: none;
      display: grid;
      gap: var(--space-3);
      color: var(--text-body);
      font-size: var(--modal-message-font-size);
      overflow-wrap: anywhere;
    }
    .tm-confirmations p {
      margin: 0;
    }
    .tm-confirmations ul {
      margin: var(--space-1) 0 0;
      padding-inline-start: var(--space-5);
      color: var(--text-secondary);
    }
  `,
  template: `
    <!-- Each warning's message, with up to five of its rows -->
    <ul class="tm-confirmations" [id]="data.listId">
      @for (warning of data.warnings; track $index) {
        <li>
          <p>{{ warning.message() }}</p>
          @if (warning.rows.length > 0) {
            <ul>
              @for (row of warning.rows; track $index) {
                <li>{{ row() ?? '…' }}</li>
              }
              @if (warning.more; as more) {
                <li>{{ more() }}</li>
              }
            </ul>
          }
        </li>
      }
    </ul>
    <!-- Cancel takes focus, so a stray Enter confirms nothing -->
    <div tmModalFooter>
      <button tmButton type="button" variant="secondary" cdkFocusInitial (click)="ref.close()">{{ cancel() }}</button>
      @if (data.skip; as skip) {
        <button tmButton type="button" variant="secondary" (click)="ref.close('skip')">{{ skip() }}</button>
      }
      <button tmButton type="button" variant="primary" (click)="ref.close('continue')">{{ continue() }}</button>
    </div>
  `,
})
export class ɵTmConfirmationsDialog {
  /** The warnings and the choices. */
  protected readonly data = inject<TmConfirmationsData>(TM_MODAL_DATA);

  /** The dialog, closed with the answer. */
  protected readonly ref = inject<TmModalRef<TmConfirmationAnswer>>(TmModalRef);

  /** Cancel's label, the component library's. */
  protected readonly cancel = inject(TM_UI_TRANSLATE)('confirm.cancel');

  /** Continue's label. */
  protected readonly continue = inject(TmLocalization).ɵtext('tm.confirmations.continue');
}

/**
 * The confirmation loop's rounds from the first warning on: each asks the
 * user, then resends with the confirmed keys or without the named rows, until
 * the write succeeds, the user declines, or the server keeps warning.
 * Internal: `TmConfirmations.run` calls it.
 *
 * @param injector - The root injector.
 * @param send - Sends the write.
 * @param context - The write's entity, rows and keys already confirmed.
 * @param first - The first warning response.
 * @returns The write's result; `null` when the user declined.
 */
export async function ɵtmConfirmationRounds<T>(
  injector: Injector,
  send: (attempt: TmConfirmationAttempt) => Promise<T>,
  context: TmConfirmationContext,
  first: TmApiError,
): Promise<T | null> {
  injector.get(TmLocalization).ɵregisterStrings('confirmations', ɵTM_CONFIRMATIONS_STRINGS_EN);
  const keys = new Set(context.confirmations ?? []);
  let skip: readonly TmId[] = [];
  let error = first;
  for (let round = 1; ; round++) {
    const warnings = warningsOf(error);
    // A fifth warning response, or one that names nothing to confirm, ends the run.
    if (round > MAX_ROUNDS || warnings.length === 0) {
      throw refused(error, 'The server kept asking for confirmations; the write was not completed.');
    }

    // --- The rows "Continue without these" would leave out: those the warnings name, of the rows still sent ---
    const named = new Set(warnings.flatMap((warning) => warning.ids.map(String)));
    const remaining = (context.ids ?? []).filter((id) => !skip.some((skipped) => String(skipped) === String(id)));
    const without = remaining.filter((id) => named.has(String(id)));
    const offered = without.length > 0 && without.length < remaining.length ? without : [];

    const answer = await ask(injector, context.entity, warnings, error, offered.length);
    if (answer === undefined) {
      return null;
    }
    if (answer === 'skip') {
      skip = [...skip, ...offered];
    } else {
      warnings.forEach((warning) => keys.add(warning.key));
      if (keys.size > MAX_KEYS) {
        throw refused(error, `The write asks for more than ${MAX_KEYS} confirmations; it was not completed.`);
      }
    }
    try {
      return await send({ confirmations: [...keys], skip });
    } catch (next) {
      if (!(next instanceof TmApiError) || next.kind !== 'confirmation') {
        throw next;
      }
      error = next;
    }
  }
}

/**
 * Opens the dialog over one warning response.
 *
 * @param injector - The root injector.
 * @param entity - The entity the warnings' rows belong to.
 * @param warnings - The warnings.
 * @param error - The response, whose detail renders the first warning when no pack holds its key.
 * @param skip - How many rows "Continue without these" leaves out; `0` for none.
 * @returns The answer; `undefined` when the user declined.
 */
async function ask(
  injector: Injector,
  entity: string | undefined,
  warnings: readonly TmWarning[],
  error: TmApiError,
  skip: number,
): Promise<TmConfirmationAnswer | undefined> {
  const names = injector.get(TmDisplayNames);
  const serverText = injector.get(TmServerText);
  const localization = injector.get(TmLocalization);
  const detail = error.problem?.['detail'];
  const views = warnings.map((warning, index): TmWarningView => {
    const shown = entity === undefined ? [] : warning.ids.slice(0, MAX_ROWS);
    if (entity !== undefined) {
      names.resolve(entity, shown);
    }
    // The server's own text renders the first warning; a key no pack holds shows as itself otherwise.
    const fallback = index === 0 && typeof detail === 'string' && detail !== '' ? detail : warning.key;
    return {
      message: serverText.error({ code: warning.key, message: fallback, ...(warning.arguments !== undefined && { arguments: warning.arguments }) }, entity),
      rows: shown.map((id) => names.label(entity!, id)),
      more: shown.length < warning.ids.length && entity !== undefined ? localization.ɵtext('tm.confirmations.more', { n: warning.ids.length - shown.length }) : null,
    };
  });
  const listId = `tm-confirmations-${nextId++}`;
  const ref = injector.get(TmModal).open<TmConfirmationAnswer>(ɵTmConfirmationsDialog, {
    size: 'sm',
    role: 'alertdialog',
    describedBy: listId,
    title: localization.ɵtext('tm.confirmations.title')(),
    data: { warnings: views, skip: skip > 0 ? localization.ɵtext('tm.confirmations.skip', { n: skip }) : null, listId } satisfies TmConfirmationsData,
  });
  const result = await ref.closed;
  return result.via === 'api' ? result.value : undefined;
}

/**
 * The warnings a 409 `confirmation-required` lists in `errorDetails.confirmations`.
 *
 * @param error - The response.
 * @returns The warnings with a key; malformed items are left out.
 */
function warningsOf(error: TmApiError): readonly TmWarning[] {
  const details = error.problem?.['errorDetails'];
  const items = details !== null && typeof details === 'object' && !Array.isArray(details) ? (details as TmJsonObject)['confirmations'] : undefined;
  return (Array.isArray(items) ? (items as readonly TmJson[]) : []).flatMap((entry): TmWarning[] => {
    const item = entry !== null && typeof entry === 'object' && !Array.isArray(entry) ? (entry as TmJsonObject) : null;
    const key = item?.['key'];
    if (item === null || typeof key !== 'string') {
      return [];
    }
    const ids = item['ids'];
    return [
      {
        key,
        ids: Array.isArray(ids) ? (ids as readonly TmJson[]).filter((id): id is TmId => typeof id === 'number' || typeof id === 'string') : [],
        ...(item['arguments'] !== undefined && { arguments: item['arguments'] }),
      },
    ];
  });
}

/**
 * A warning response the loop gives up on, as the `server` error it fails with.
 *
 * @param error - The last warning response.
 * @param message - Why the loop gave up.
 * @returns The error.
 */
function refused(error: TmApiError, message: string): TmApiError {
  return new TmApiError({ kind: 'server', status: error.status, code: error.code, traceId: error.traceId, problem: error.problem, message });
}
