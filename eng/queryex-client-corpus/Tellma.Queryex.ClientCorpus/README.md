# Tellma.Queryex.ClientCorpus

Writes the two files the client's Queryex suites in `@tellma/core/entity` and
`@tellma/core/expression-editor` are held to, from the engine's own lexer, parser and printer:

- `client/projects/core/tellma-core/entity/src/queryex-lexer-corpus.json`: for each text, the
  tokens the engine reads (with the runs it cannot read as `error` tokens), its parameters grouped
  by name, and its QX1xxx problems. The client's `tmQxLex` and `tmQxParameters` must agree with
  every case.
- `client/projects/core/tellma-core/entity/src/queryex-print-golden.json`: expression trees in the
  client's `TmQx` shape and the text the engine's canonical printer writes for each. The client's
  `tmQxPrint` must print every tree as its text.

The texts and rows, and the writer, live in `Tellma.Queryex.Testing` (`Client/`), which alone holds
the engine's internals. This tool only runs the writer and saves its output. The engine suite
(`Tellma.Core.Queryex.Tests`, `Client/`) fails when a checked-in file differs from what the engine
writes now, and checks that every print row reads back to its own text, means what its grouped
source means, and reads cleanly against the fixture schema when it is a filter.

## When to run it

When the engine's lexer or canonical printer changes, or when a text or row is added to the
corpus. A change to either file is a change to what the client must do: review it as such, then
run the client suites.

## How to run it

From the repository root, on Windows or Linux:

```sh
dotnet run --project eng/queryex-client-corpus/Tellma.Queryex.ClientCorpus
```

It writes both files as UTF-8 without a byte-order mark and with line feeds, on every OS. Pass the
repository root as the only argument to run it from elsewhere.
