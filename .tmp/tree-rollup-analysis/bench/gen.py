"""Generates the benchmark's query matrix (scenarios.json) for run.ps1.

Variants, each emitting one pivot statement over dbo.Lines:
  L      level navigations, as spec 0010 §F.9 / 0033 §8.5 compile them (one key per level, product grouping sets)
  L6     L capped at 0033's default of 6 levels (only for trees deeper than 6)
  Lpre   L over a pre-aggregation of the facts by (leaf keys, column keys)
  C      virtual closure: OUTER APPLY k < GetLevel(), seek GetAncestor(k), masked self-twins
  Cpre   C over the same pre-aggregation
  Cmat   C through a materialized closure table instead of GetAncestor
"""
import itertools
import json
import sys

MAX_DEPTH = 32          # [Tree(MaxDepth)] default: the VALUES list the closure emits
TAKE = 250001           # MaxPivotRows + 1
ACCOUNTS = dict(depth=5, branch=4, leafPct=0)

TREES = {
    "D2": dict(depth=2, branch=40, leafPct=0),
    "D4": dict(depth=4, branch=6, leafPct=0),
    "D6": dict(depth=6, branch=3, leafPct=0),
    "D8": dict(depth=8, branch=2, leafPct=0),
    "R8": dict(depth=8, branch=3, leafPct=35),   # ragged: leaves at mixed depths
    "W5": dict(depth=5, branch=8, leafPct=0),    # wide: ~37k members
    "T3": dict(depth=3, branch=3, leafPct=0),    # small center tree for two-tree pivots: 39 members
    "B6": dict(depth=6, branch=2, leafPct=0),    # deeper small center tree for two-tree pivots: 126 members
}

TREE_DIMS = {
    "Centers": dict(table="Centers", fk="CenterId", nullable=True, tag="c"),
    "Accounts": dict(table="Accounts", fk="AccountId", nullable=False, tag="a"),
}
QUARTER = "DATEPART(QUARTER, T.PostingDate)"


class Q:
    """Accumulates one statement's select items, joins and grouping."""

    def __init__(self):
        self.items = []      # (sql) select items, aliased c0..cn in order
        self.joins = []

    def add(self, sql):
        self.items.append(sql)
        return len(self.items) - 1


def source(pre, col):
    """The fact source: the base table, or the pre-aggregation by leaf and column keys."""
    if not pre:
        return "dbo.Lines AS T", "T.Amount", None
    keys = ["T.CenterId"]
    if col == "accounts":
        keys.append("T.AccountId")
    if col == "quarter":
        keys.append(f"{QUARTER} AS q")
    group = [k.split(" AS ")[0] for k in keys]
    sql = (f"(SELECT {', '.join(keys)}, SUM(T.Amount) AS s, COUNT_BIG(*) AS n "
           f"FROM dbo.Lines AS T GROUP BY {', '.join(group)}) AS F")
    return sql, "F.s", "F.n"


def fk_ref(pre, dim):
    return f"{'F' if pre else 'T'}.{dim['fk']}"


def col_expr(pre):
    return "F.q" if pre else QUARTER


def finish(q, src, sets, keys):
    """Wraps items, joins and grouping sets into the final statement text."""
    select = ",\n       ".join(f"{s} AS c{i}" for i, s in enumerate(q.items))
    gsets = ",\n    ".join("(" + ", ".join(keys[k] for k in s) + ")" for s in sets)
    joins = "\n".join(q.joins)
    return (f"SELECT /*TAG*/ {select}\nFROM {src}\n{joins}\n"
            f"GROUP BY GROUPING SETS (\n    {gsets})\n"
            f"ORDER BY c0 OFFSET 0 ROWS FETCH NEXT {TAKE} ROWS ONLY")


# ---------------------------------------------------------------- level navigations

def level_query(levels_r, col, levels_c, pre, per_member=False, labels=True):
    """0033 §8.5: keys L1..Lp per tree, labels max() per level, one probe per tree, sets (i, j).

    per_member computes the level joins once per dimension member in a derived table that the
    facts hash-join by foreign key, instead of once per fact row."""
    q = Q()
    src, amt, cnt = source(pre, col)
    sum_x, cnt_x = f"SUM({amt})", (f"SUM({cnt})" if pre else "COUNT_BIG(*)")
    label = {}   # (tag, level, prop) -> column reference; (tag, 0, 'Lvl') -> the probe operand

    def level_join(table, alias, parent, j):
        return (f"LEFT JOIN dbo.{table} AS {alias} ON {alias}.Node = CASE WHEN {parent}.Node.GetLevel() >= {j} "
                f"THEN {parent}.Node.GetAncestor({parent}.Node.GetLevel() - {j}) END")

    def tree_levels(dim, levels):
        t = dim["tag"]
        join = "LEFT JOIN" if dim["nullable"] else "INNER JOIN"
        keys = []
        if per_member:
            cols = ["P.Id", "P.Node.GetLevel() AS Lvl"]
            joins = []
            for j in range(1, levels + 1):
                joins.append(level_join(dim["table"], f"L{j}", "P", j))
                cols += [f"L{j}.Id AS L{j}Id", f"L{j}.Name AS L{j}Name", f"L{j}.Name2 AS L{j}Name2", f"L{j}.Code AS L{j}Code"]
                keys.append(f"D{t}.L{j}Id")
                for p in ("Name", "Name2", "Code"):
                    label[(t, j, p)] = f"D{t}.L{j}{p}"
            q.joins.append(f"{join} (SELECT {', '.join(cols)} FROM dbo.{dim['table']} AS P {' '.join(joins)}) "
                           f"AS D{t} ON D{t}.Id = {fk_ref(pre, dim)}")
            label[(t, 0, "Lvl")] = f"D{t}.Lvl"
        else:
            q.joins.append(f"{join} dbo.{dim['table']} AS P{t} ON P{t}.Id = {fk_ref(pre, dim)}")
            for j in range(1, levels + 1):
                q.joins.append(level_join(dim["table"], f"L{t}{j}", f"P{t}", j))
                keys.append(f"L{t}{j}.Id")
                for p in ("Name", "Name2", "Code"):
                    label[(t, j, p)] = f"L{t}{j}.{p}"
            label[(t, 0, "Lvl")] = f"P{t}.Node.GetLevel()"
        return keys

    row_keys = tree_levels(TREE_DIMS["Centers"], levels_r)
    col_keys = []
    if col == "quarter":
        col_keys = [col_expr(pre)]
    elif col == "accounts":
        col_keys = tree_levels(TREE_DIMS["Accounts"], levels_c)
    keys = row_keys + col_keys
    for k in keys:
        q.add(k)
    m_sum, m_cnt = q.add(sum_x), q.add(cnt_x)
    # Labels: three per tree level (0033 §8.4), then one truncation probe per tree (§8.6).
    for t, n in [("c", levels_r)] + ([("a", levels_c)] if col == "accounts" else []):
        if labels:
            for j in range(1, n + 1):
                for p in ("Name", "Name2", "Code"):
                    q.add(f"MAX({label[(t, j, p)]})")
        q.add(f"MAX({label[(t, 0, 'Lvl')]})")
    ind = q.add("GROUPING_ID(" + ", ".join(keys) + ")")

    # Sets R1..Ri ∪ C1..Cj, finest first.
    nr, nc = len(row_keys), len(col_keys)
    sets, set_ij = [], []
    for i in range(nr, -1, -1):
        for j in range(nc, -1, -1):
            sets.append(list(range(i)) + [nr + x for x in range(j)])
            set_ij.append((i, j))
    meta = dict(kind="L", nr=nr, nc=nc, ind=ind, sum=m_sum, cnt=m_cnt, set_ij=set_ij, col=col)
    return finish(q, src, sets, keys), meta


# ---------------------------------------------------------------- virtual closure

def closure_query(col, pre, mat, per_member=False, labels=True):
    """One key per tree; masked twins per subset of absent tree dimensions.

    per_member builds the virtual closure (descendant, k, ancestor attributes) once per dimension
    member in a derived table that the facts hash-join by foreign key."""
    q = Q()
    src, amt, cnt = source(pre, col)
    trees = [TREE_DIMS["Centers"]] + ([TREE_DIMS["Accounts"]] if col == "accounts" else [])
    masks = {}
    values = ",".join(f"({k})" for k in range(MAX_DEPTH))

    for dim in trees:
        t = dim["tag"]
        fk = fk_ref(pre, dim)
        if per_member:
            join = "LEFT JOIN" if dim["nullable"] else "INNER JOIN"
            q.joins.append(
                f"{join} (SELECT P.Id AS DescId, K.k, A.Id, A.Name, A.Name2, A.Code, A.ParentId "
                f"FROM dbo.{dim['table']} AS P "
                f"CROSS APPLY (SELECT v.k FROM (VALUES {values}) AS v(k) WHERE v.k < P.Node.GetLevel()) AS K "
                f"JOIN dbo.{dim['table']} AS A ON A.Node = P.Node.GetAncestor(K.k)) AS A{t} ON A{t}.DescId = {fk}")
            masks[t] = f"ISNULL(A{t}.k, 0) = 0"
        elif mat:
            join = "LEFT JOIN" if dim["nullable"] else "INNER JOIN"
            q.joins.append(f"{join} dbo.{dim['table']}Closure AS K{t} ON K{t}.DescendantId = {fk}")
            q.joins.append(f"LEFT JOIN dbo.{dim['table']} AS A{t} ON A{t}.Id = K{t}.AncestorId")
            masks[t] = f"ISNULL(K{t}.Distance, 0) = 0"
        else:
            join = "LEFT JOIN" if dim["nullable"] else "INNER JOIN"
            q.joins.append(f"{join} dbo.{dim['table']} AS P{t} ON P{t}.Id = {fk}")
            q.joins.append(f"CROSS APPLY (SELECT v.k FROM (VALUES {values}) AS v(k) "
                           f"WHERE v.k < ISNULL(P{t}.Node.GetLevel(), 1)) AS K{t}")
            q.joins.append(f"LEFT JOIN dbo.{dim['table']} AS A{t} ON A{t}.Node = P{t}.Node.GetAncestor(K{t}.k)")
            masks[t] = f"K{t}.k = 0"

    keys = [f"A{d['tag']}.Id" for d in trees]
    if col == "quarter":
        keys.append(col_expr(pre))
    for k in keys:
        q.add(k)

    # Measure variants: one per subset of masked (absent) tree dimensions; the empty subset is plain.
    tags = [d["tag"] for d in trees]
    variants = {}
    for r in range(len(tags) + 1):
        for subset in itertools.combinations(tags, r):
            if not subset:
                s = q.add(f"SUM({amt})")
                c = q.add(f"SUM({cnt})" if pre else "COUNT_BIG(*)")
            else:
                cond = " AND ".join(masks[t] for t in subset)
                s = q.add(f"SUM(CASE WHEN {cond} THEN {amt} END)")
                c = q.add(f"SUM(CASE WHEN {cond} THEN {cnt} END)" if pre
                          else f"COUNT_BIG(CASE WHEN {cond} THEN 1 END)")
            variants["".join(subset)] = (s, c)
    # Ancestor attributes: labels and the parent link (without labels, a follow-up lookup supplies them).
    for t in tags if labels else []:
        for p in ("Name", "Name2", "Code", "ParentId"):
            q.add(f"MAX(A{t}.{p})")
    ind = q.add("GROUPING_ID(" + ", ".join(keys) + ")")

    n = len(keys)
    sets = []
    for r in range(n, -1, -1):
        for combo in itertools.combinations(range(n), r):
            sets.append(list(combo))
    meta = dict(kind="C", keys=n, tags=tags, ind=ind, variants=variants, col=col)
    return finish(q, src, sets, keys), meta


# ---------------------------------------------------------------- leaf grain (rollup in C#)

def leaf_query(col):
    """SQL side of the cube-builder rollup: one plain GROUP BY at leaf grain, no joins."""
    keys = ["T.CenterId"]
    if col == "quarter":
        keys.append(QUARTER)
    elif col == "accounts":
        keys.append("T.AccountId")
    select = ",\n       ".join(f"{k} AS c{i}" for i, k in enumerate(keys + ["SUM(T.Amount)", "COUNT_BIG(*)"]))
    return (f"SELECT /*TAG*/ {select}\nFROM dbo.Lines AS T\nGROUP BY {', '.join(keys)}\n"
            f"ORDER BY c0 OFFSET 0 ROWS FETCH NEXT {TAKE} ROWS ONLY")


# ---------------------------------------------------------------- validation

def truth_sql(col):
    """Brute-force subtree totals through IsDescendantOf: (R, C, s, n); -1 is ALL, -2 is None."""
    rm = ("SELECT T.Id AS LineId, a.Id AS R FROM dbo.Lines AS T JOIN dbo.Centers AS d ON d.Id = T.CenterId "
          "JOIN dbo.Centers AS a ON d.Node.IsDescendantOf(a.Node) = 1 "
          "UNION ALL SELECT T.Id, -2 FROM dbo.Lines AS T WHERE T.CenterId IS NULL "
          "UNION ALL SELECT T.Id, -1 FROM dbo.Lines AS T")
    if col == "quarter":
        cm = f"SELECT T.Id AS LineId, {QUARTER} AS C FROM dbo.Lines AS T UNION ALL SELECT T.Id, -1 FROM dbo.Lines AS T"
    elif col == "accounts":
        cm = ("SELECT T.Id AS LineId, a.Id AS C FROM dbo.Lines AS T JOIN dbo.Accounts AS d ON d.Id = T.AccountId "
              "JOIN dbo.Accounts AS a ON d.Node.IsDescendantOf(a.Node) = 1 "
              "UNION ALL SELECT T.Id, -1 FROM dbo.Lines AS T")
    else:
        cm = "SELECT T.Id AS LineId, -1 AS C FROM dbo.Lines AS T"
    return (f"DROP TABLE IF EXISTS #truth;\n"
            f"WITH RM AS ({rm}), CM AS ({cm})\n"
            f"SELECT RM.R, CM.C, SUM(T.Amount) AS s, COUNT_BIG(*) AS n INTO #truth\n"
            f"FROM dbo.Lines AS T JOIN RM ON RM.LineId = T.Id JOIN CM ON CM.LineId = T.Id GROUP BY RM.R, CM.C;\n")


def decode_sql(name, meta):
    """Decodes a variant's rows in #r_<name> into (R, C, s, n) subtree totals."""
    tbl = f"#r_{name}"
    parts = []
    if meta["kind"] == "L":
        nr, nc = meta["nr"], meta["nc"]
        k = nr + nc
        for (i, j) in meta["set_ij"]:
            rolled = list(range(i, nr)) + [nr + x for x in range(j, nc)]
            ind = sum(1 << (k - 1 - p) for p in rolled)
            conds = [f"c{meta['ind']} = {ind}"]
            if i == 0:
                r = "-1"
            else:
                # The member at level i, or the None member (all levels null) taken from i = 1 only.
                r = f"COALESCE(c{i - 1}, -2)"
                conds.append(f"(c{i - 1} IS NOT NULL OR ({i} = 1 AND c0 IS NULL))")
            if j == 0:
                c = "-1"
            elif meta["col"] == "quarter":
                c = f"c{nr}"
            else:
                c = f"c{nr + j - 1}"
                conds.append(f"c{nr + j - 1} IS NOT NULL")
            parts.append(f"SELECT {r} AS R, {c} AS C, c{meta['sum']} AS s, c{meta['cnt']} AS n "
                         f"FROM {tbl} WHERE {' AND '.join(conds)}")
    else:
        n = meta["keys"]
        tags = meta["tags"]
        for mask in range(1 << n):
            # mask bit p (MSB first) set = key p rolled up.
            rolled = [p for p in range(n) if mask & (1 << (n - 1 - p))]
            absent = "".join(t for idx, t in enumerate(tags) if idx in rolled)
            s, cn = meta["variants"][absent]
            r = "-1" if 0 in rolled else "COALESCE(c0, -2)"
            if n == 1:
                c = "-1"
            else:
                c = "-1" if 1 in rolled else ("c1" if meta["col"] == "quarter" else "c1")
            parts.append(f"SELECT {r} AS R, {c} AS C, c{s} AS s, c{cn} AS n FROM {tbl} WHERE c{meta['ind']} = {mask}")
    return "\nUNION ALL\n".join(parts)


def validate_sql(name, sql, meta):
    body = sql.replace("/*TAG*/", "/* validate */")
    return (f"DROP TABLE IF EXISTS #r_{name};\nSELECT q.* INTO #r_{name} FROM (\n{body}\n) AS q;\n"
            f"WITH D AS (\n{decode_sql(name, meta)}\n)\n"
            f"SELECT '{name}' AS variant,\n"
            f"  (SELECT COUNT(*) FROM #r_{name}) AS rows_returned,\n"
            f"  (SELECT COUNT(*) FROM (SELECT R, C FROM D GROUP BY R, C HAVING COUNT(*) > 1) x) AS dup_cells,\n"
            f"  (SELECT COUNT(*) FROM D FULL JOIN #truth AS t ON t.R = D.R AND t.C = D.C\n"
            f"     WHERE D.R IS NULL OR t.R IS NULL OR D.s <> t.s OR D.n <> t.n) AS mismatches,\n"
            f"  (SELECT COUNT(*) FROM #truth) AS truth_cells;\n")


# ---------------------------------------------------------------- scenarios

SLOW = {"L", "C"}   # per-fact hierarchyid calls: timed once, at 1M only
FACT_GRAIN = {"Ldim", "L6dim", "Cdim", "Cmat"}   # grouping sets over fact rows: timed once at 10M


def runs_for(name, n):
    return 1 if name in SLOW or (n >= 10_000_000 and name in FACT_GRAIN) else None


def variants_for(tree, col, n):
    depth = TREES[tree]["depth"]
    acc = ACCOUNTS["depth"]
    # Fact-grain variants are dominated by 10-30x at 1M, so 10M times only the pre-aggregated ones.
    fact_grain = n <= 1_000_000
    out = []
    if fact_grain:
        out.append(("L", *level_query(depth, col, acc, False)))
        out.append(("Ldim", *level_query(depth, col, acc, False, per_member=True)))
        if depth > 6:
            out.append(("L6dim", *level_query(6, col, min(acc, 6), False, per_member=True)))
    out.append(("Lpre", *level_query(depth, col, acc, True)))
    out.append(("LpreNL", *level_query(depth, col, acc, True, labels=False)))
    if depth > 6:
        out.append(("L6pre", *level_query(6, col, min(acc, 6), True)))
    if fact_grain:
        out.append(("C", *closure_query(col, False, False)))
        out.append(("Cdim", *closure_query(col, False, False, per_member=True)))
    out.append(("Cpre", *closure_query(col, True, False)))
    out.append(("CpreNL", *closure_query(col, True, False, labels=False)))
    if fact_grain:
        out.append(("Cmat", *closure_query(col, False, True)))
    return out


def main():
    plan = sys.argv[1] if len(sys.argv) > 1 else "all"
    only = set(sys.argv[2].split(",")) if len(sys.argv) > 2 else None
    scenarios = []
    matrix = []
    one_tree = ("D2", "D4", "D6", "D8", "R8", "W5")
    if plan in ("validate", "all"):
        for tree in one_tree:
            for col in ("none", "quarter"):
                matrix.append((tree, 100_000, col, True))
    if plan in ("validate", "validate2", "all"):
        matrix.append(("T3", 100_000, "accounts", True))
        matrix.append(("B6", 100_000, "accounts", True))
    if plan in ("1m", "all"):
        for tree in one_tree:
            for col in ("none", "quarter"):
                matrix.append((tree, 1_000_000, col, False))
        matrix.append(("T3", 1_000_000, "accounts", False))
        matrix.append(("B6", 1_000_000, "accounts", False))
    if plan in ("10m", "all"):
        for tree in ("D2", "D4", "D8", "R8", "W5"):
            for col in ("none", "quarter"):
                matrix.append((tree, 10_000_000, col, False))
        matrix.append(("T3", 10_000_000, "accounts", False))
        matrix.append(("B6", 10_000_000, "accounts", False))

    for tree, n, col, validate in matrix:
        sid = f"{tree}-{n // 1000}k-{col}"
        vs = variants_for(tree, col, n)
        if only:
            vs = [v for v in vs if v[0] in only]
        # Leaf has no grouping sets to decode, so it is timed but not validated.
        timed = vs + ([] if validate else [("Leaf", leaf_query(col), None)])
        scenarios.append(dict(
            id=sid, tree=tree, treeSpec=TREES[tree], n=n, col=col,
            runs=0 if validate else (3 if n >= 10_000_000 else 5),
            variants=[dict(name=name, sql=sql, runs=runs_for(name, n)) for name, sql, _ in timed],
            # Temp tables are dropped at the end: a later batch on the connection would otherwise
            # compile its decode against this batch's column list.
            validate=(truth_sql(col) + "".join(validate_sql(name, sql, meta) for name, sql, meta in vs)
                      + "".join(f"DROP TABLE #r_{name};\n" for name, _, _ in vs) + "DROP TABLE #truth;\n")
            if validate else None))
    json.dump(dict(accounts=ACCOUNTS, scenarios=scenarios), sys.stdout, indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main()
