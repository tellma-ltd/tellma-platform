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
