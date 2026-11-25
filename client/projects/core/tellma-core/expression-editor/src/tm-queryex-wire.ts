// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson } from '@tellma/core';

// The wire of `queryex/discover` (spec 0029 §9), as the server's JSON
// options spell it: camelCase members, the Queryex types and the profile by
// their names, and null members left out. The draft builds the request and
// reads the answer; the test fake answers with the same shapes.

/** A Queryex type, as the wire names it. */
export type TmQueryexType = 'Bool' | 'Numeric' | 'String' | 'Guid' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'HierarchyId' | 'Geography';

/** A parameter type the author confirmed, sent with the request. */
export interface TmQueryexParameterInput {
  /** The name, without `@`. */
  readonly name: string;
  /** The confirmed type. */
  readonly type: TmQueryexType;
}

/** The body of a `queryex/discover` call. */
export interface TmQueryexDiscoverRequest {
  /** The entity the clauses read, such as `acme.SalesInvoiceLine`. */
  readonly root: string;
  /** The select list. */
  readonly select?: string;
  /** The filters, joined with `and`. */
  readonly filters?: readonly string[];
  /** The filter over the aggregated rows; only with `aggregate`. */
  readonly having?: string;
  /** The order terms. */
  readonly orderBy?: string;
  /** A pivot's or a summary's measures; only with `aggregate`. */
  readonly measures?: string;
  /** Whether the query aggregates. */
  readonly aggregate?: boolean;
  /** The types the author confirmed; the others are inferred. */
  readonly parameters?: readonly TmQueryexParameterInput[];
  /** How the filter is validated: as a query compiles it, or as a role's save does. */
  readonly profile?: 'Query' | 'Permission';
  /** Whether the answer lists the filters' top-level conjuncts. */
  readonly conjuncts?: boolean;
}

/** Where a clause's diagnostic, parameter or path sits. */
export interface TmQueryexSite {
  /** The clause, as the wire names it: `select`, `filters[0]`, `having`, `orderBy` or `measures`. */
  readonly clause: string;
  /** The node's place in the clause's tree, such as `Filter.And[0]`; absent for a whole clause. */
  readonly location?: string;
  /** The first UTF-16 code unit, in the clause's text. */
  readonly start: number;
  /** The span's length in UTF-16 code units. */
  readonly length: number;
}

/** One problem the server found. */
export interface TmQueryexWireDiagnostic extends TmQueryexSite {
  /** The code, such as `QX3001` or `Query.PathNotReachable`. */
  readonly code: string;
  /** The message's arguments. */
  readonly arguments?: Readonly<Record<string, TmJson>>;
}

/** One parameter the clauses use, with what the server inferred. */
export interface TmQueryexParameterInfo {
  /** The name, without `@`, as first spelled. */
  readonly name: string;
  /** Every place it is written. */
  readonly sites: readonly TmQueryexSite[];
  /** The confirmed type, else the inferred one; absent when unconstrained or conflicting. */
  readonly type?: TmQueryexType;
  /** The types its uses disagree on; empty when they agree. */
  readonly conflicts: readonly TmQueryexType[];
  /** The path it is compared with. */
  readonly comparedWith?: readonly string[];
  /** The entity that path ends at, when it ends at a key or a foreign key. */
  readonly entity?: string;
}

/** One path the clauses read. */
export interface TmQxPathUse {
  /** The path's names, such as `['Invoice', 'PostingDate']`. */
  readonly segments: readonly string[];
  /** Where it is written. */
  readonly site: TmQueryexSite;
  /** The entity that owns its last name. */
  readonly entity: string;
  /** Whether the user may read it, by the traversal rule. */
  readonly reachable: boolean;
}

/** One top-level conjunct of a filter, when asked. */
export interface TmQueryexConjunct {
  /** The index into the filters. */
  readonly filter: number;
  /** The path the conjunct compares, dotted; or absent for an expression. */
  readonly path?: string;
  /** The expression the conjunct compares, as canonical text; or absent for a path. */
  readonly expression?: string;
  /** The comparison. */
  readonly op: string;
  /** The literal, in the wire encoding; absent for null. */
  readonly value?: TmJson;
  /** The literal's type. */
  readonly type: TmQueryexType;
}

/** The answer of a `queryex/discover` call. */
export interface TmQueryexDiscoverResult {
  /** Every problem, syntax errors included. */
  readonly diagnostics: readonly TmQueryexWireDiagnostic[];
  /** The parameters, with their types. */
  readonly parameters: readonly TmQueryexParameterInfo[];
  /** The paths the clauses read. */
  readonly paths: readonly TmQxPathUse[];
  /** Whether the clauses aggregate. */
  readonly usesAggregation: boolean;
  /** The filters' conjuncts, when asked. */
  readonly conjuncts?: readonly TmQueryexConjunct[];
}
