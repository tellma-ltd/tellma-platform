// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Injector } from '@angular/core';
import { TmApi, TmEntities, type TmActionMetadata, type TmId, type TmJson, type TmJsonObject } from '@tellma/core';

import { TmConfirmations } from './tm-confirmations';
import {
  ɵtmLoad,
  ɵtmOfferable,
  ɵtmTakesNoArguments,
  type TmEntitiesResult,
  type TmEntityActionOptions,
  type TmEntityActionOutcome,
} from './tm-entity-actions';

// An entity action's request and outcome (spec 0031 §10.4), which
// `TmEntityActions.run`, `TmEntityActions.dialog` and the action dialog load
// on first use: nothing on a page's first frame sends an action. The body
// follows spec 0016 §2.3, and the send goes through the confirmation loop
// (§9.3). The choice between running an action at once and opening its
// dialog is here too, so the dialog's code loads only when one opens.

/**
 * The body an entity action posts: the rows and options of an action that
 * returns entities, the one id of a single-target action, else the ids;
 * then the arguments. Internal: the action's run sends it.
 *
 * @param action - The action's metadata.
 * @param ids - The rows.
 * @param options - The arguments, extras and whether to return the entities.
 * @param extras - The entity's extras, the default `include`.
 * @returns The body.
 * @throws Error for a single-target action given other than one row.
 */
export function ɵtmActionBody(
  action: Pick<TmActionMetadata, 'name' | 'result' | 'singleTarget'>,
  ids: readonly TmId[],
  options: TmEntityActionOptions,
  extras: readonly string[],
): TmJsonObject {
  const args: TmJsonObject = options.arguments === undefined ? {} : { arguments: options.arguments };
  if (action.result === 'entities') {
    const returnEntities = options.returnEntities ?? true;
    return { ids: [...ids], returnEntities, ...(returnEntities && { include: [...(options.include ?? extras)] }), ...args };
  }
  if (action.singleTarget) {
    if (ids.length !== 1) {
      throw new Error(`The action '${action.name}' takes exactly one record; it was given ${ids.length}.`);
    }
    return { id: ids[0]!, ...args };
  }
  return { ids: [...ids], ...args };
}

/**
 * Runs an action on rows, through the server's warnings. Internal:
 * `TmEntityActions.run` and the action dialog call it.
 *
 * @param injector - The root injector.
 * @param entity - The entity's name.
 * @param name - The action's name.
 * @param ids - The rows.
 * @param options - The arguments, the extras and whether to return the entities.
 * @param sent - Told the rows of each send, after any the user left out, so a row error's index can be placed.
 * @returns The outcome; `declined` when the user declined a warning.
 * @throws Error for an action the entity does not have; TmApiError when the action fails.
 */
export async function ɵtmRunAction(
  injector: Injector,
  entity: string,
  name: string,
  ids: readonly TmId[],
  options: TmEntityActionOptions,
  sent?: (ids: readonly TmId[]) => void,
): Promise<TmEntityActionOutcome> {
  const entities = injector.get(TmEntities);
  await entities.load();
  const descriptor = entities.get(entity);
  const action = descriptor.actions.find((candidate) => candidate.name === name);
  if (action === undefined || descriptor.segment === null) {
    throw new Error(`The entity '${entity}' has no action '${name}' to run.`);
  }
  const api = injector.get(TmApi);
  const route = `${descriptor.segment}/${action.name}`;

  // --- The send, again for each warning the user confirms; the body is wrapped, since an action may answer null ---
  const answer = await injector.get(TmConfirmations).run(
    async ({ confirmations, skip }) => {
      const rows = skip.length === 0 ? ids : ids.filter((id) => !skip.some((skipped) => String(skipped) === String(id)));
      sent?.(rows);
      return { body: await api.write<TmJson>(route, ɵtmActionBody(action, rows, options, descriptor.extras), { confirmations }) };
    },
    { entity, ids },
  );

  // --- The outcome, by what the action returns ---
  if (answer === null) {
    return { kind: 'declined' };
  }
  const body = answer.body ?? null;
  if (action.result === 'entities') {
    return { kind: 'entities', result: body as unknown as TmEntitiesResult };
  }
  const jobId = action.result === 'own' ? jobOf(body) : null;
  return jobId === null ? { kind: 'own', result: body } : { kind: 'job', jobId };
}

/**
 * The job a 202 `JobAccepted` answer started: a body that is exactly a job
 * id and the resource it works on.
 *
 * @param body - The action's own result.
 * @returns The job's id, or `null` for any other result.
 */
function jobOf(body: TmJson): number | null {
  if (body === null || typeof body !== 'object' || Array.isArray(body)) {
    return null;
  }
  const { jobId, resourceId, ...rest } = body as TmJsonObject;
  const valid = Number.isSafeInteger(jobId) && (resourceId === undefined || resourceId === null || Number.isSafeInteger(resourceId));
  return valid && Object.keys(rest).length === 0 ? (jobId as number) : null;
}

/**
 * Opens an action's dialog, or runs at once an action that takes no
 * arguments and declares no dialog. Internal: `TmEntityActions.dialog`
 * calls it.
 *
 * @param injector - The root injector.
 * @param entity - The entity's name.
 * @param name - The action's name.
 * @param ids - The rows.
 * @param options - The initial arguments, the extras and whether to return the entities.
 * @returns The outcome of the dialog's last successful run, however it closed; `null` when nothing ran.
 * @throws Error for an action the entity does not have, or whose arguments no dialog can take.
 */
export async function ɵtmDialog(
  injector: Injector,
  entity: string,
  name: string,
  ids: readonly TmId[],
  options: TmEntityActionOptions,
): Promise<TmEntityActionOutcome | null> {
  const entities = injector.get(TmEntities);
  await entities.load();
  const action = entities.get(entity).actions.find((candidate) => candidate.name === name);
  if (action === undefined) {
    throw new Error(`The entity '${entity}' has no action '${name}'.`);
  }
  if (action.declaration?.dialog === undefined) {
    if (ɵtmTakesNoArguments(action)) {
      return ɵtmRunAction(injector, entity, name, ids, options);
    }
    if (!ɵtmOfferable(action)) {
      throw new Error(`The action '${name}' of '${entity}' needs a declared dialog.`);
    }
  }
  // The dialog's code loads on first use.
  const { ɵtmOpenActionDialog } = await ɵtmLoad(() => import('./tm-action-dialog'));
  return ɵtmOpenActionDialog(injector, entity, action, ids, options);
}
