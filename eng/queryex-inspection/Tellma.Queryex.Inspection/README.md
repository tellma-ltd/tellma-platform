# Tellma.Queryex.Inspection

A playground for the Queryex compiler. Automated suites prove that expressions compile to the right
thing; nothing but watching every stage react as you type tells you whether the compiler is
*understandable* — which is what you want when you are reviewing a change to it, or working out why
an expression means something you did not intend.

A tiny web host serves one static page and a JSON endpoint that compiles on every keystroke. The
page shows, for whatever you have typed: the tokens, the bound tree with each node's type and
nullity, the diagnostics with their codes and spans, the expression printed back in both its
shortest and its fully parenthesised form, and the emitted SQL with its parameter slots and result
columns.

Beside the clauses, more inputs mirror what a host composes a query from:

- **restrictions** — key-set restrictions separated by semicolons, each a path and a table name and
  optionally the keys the table holds when the query is run: `Customer.Id @tb0_parties = 1, 2; Id
  @tb0_ids`. When the query is run, each table is declared ahead of it holding those keys as text,
  which the server converts to whatever the key column is.
- **undeclared columns** — root columns the schema leaves out, separated by commas, such as
  `ExtrasJson, AuditJson`.
- **filter via** — a dotted navigation path the filter is rebased onto, so its paths resolve from
  the path's target: with `Customer` the filter `Name = 'Acme'` asks for invoices whose customer is
  Acme. With no filter, it asks only that the path reach a row.
- **exists** — semi-joins conjoined with the filter, separated by semicolons, each an entity, its
  navigation to the root, and optionally a colon and a criterion over the entity's rows:
  `InvoiceLine Invoice: Amount > 10`.
- **parameters** — the declared parameters, separated by commas, each a name and a type:
  `Customer numeric, AsOf date`. Every one may be absent, and binds no value when the query is run.
- **omitted parameters** — the declared parameters the compilation leaves out, separated by commas.
  A predicate that mentions one stops deciding anything, and anywhere else it is the absent value
  of its declared type; clear the box to see the same query with nothing left out. The token, tree
  and printed-back panes keep showing the select list as typed: what was left out shows in the SQL.

One input replaces the query altogether:

- **constant items** — the items of a constant statement, separated by semicolons, each a type and
  an expression that reads no row: `date startOfMonth(today()); numeric me()`. While there are any,
  the page compiles the one-row statement that answers them, the clauses are left alone, and the
  token, tree and printed-back panes follow the first item.

A malformed entry in any of them is a caller error, shown the way the engine's own caller errors
are.

Two more readings sit beside the compilation:

- **conjuncts** — tick the box to list the composed filter's top-level restrictions as discovery
  reads them: each atom reached through `and` alone that compares a path or an expression with a
  literal, with its leaf, its span, the operator and the value. Restrictions under `or`, `not`, a
  rebase or a semi-join are not listed.
- **measure** — a measure such as `sum(Amount, CustomerId = @Customer)`, whose row condition the
  page prints: the rows a list opened from that measure would show. It reads the declared and
  omitted parameters above; the page gives no parameter a value, so one that is not left out stays
  in the condition as written.

```bash
dotnet run --project eng/queryex-inspection/Tellma.Queryex.Inspection
```

It prints the URL it chose. The port is ephemeral, so several worktrees can run it at once, and
nothing it does writes to a tracked file.

| Option | Effect |
|---|---|
| `--connection "<connection string>"` | Deploy the fixture schema and rows to the database that string names at startup, and run compiled queries against it when the page's "run it" box is ticked. |

Supplied on the command line rather than read from a file, so a connection string never ends up
somewhere it could be committed. Without one the tool compiles and shows, and opens no connection at
all.

The string has to name a database, not only a server. The database need not exist yet — it is
created when it is missing — but whatever it names has its fixture tables dropped and rebuilt on
every start, so name one you are willing to lose. Name none and the tables land in whatever database
the login opens by default, which on a local instance is `master`.

```bash
dotnet run --project eng/queryex-inspection/Tellma.Queryex.Inspection -- --connection "Server=(localdb)\MSSQLLocalDB17;Database=TellmaQueryexDev;Integrated Security=true;TrustServerCertificate=true"
```

The `--` separates the SDK's own arguments from the tool's. `dotnet run` forwards what it does not
recognise anyway, so today the line works without it; it is what keeps the line working should an
argument ever be named one the SDK claims for itself.

A database of its own keeps a playground session and an integration run from rebuilding the same
tables underneath each other; both use the same schema and the same rows, so pointing this at the
suite's database works too.

It uses the same fixture schema and the same rows the conformance suites pin, so what you see here
is what CI proves. Never packed, never shipped; it is in the solution so that CI keeps it compiling.
