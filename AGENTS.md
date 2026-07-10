# AGENTS.md

This is a production repo; all checked-in code must be of the highest quality.

## Rules
- [ARCHITECTURE.md](./ARCHITECTURE.md) is a living document that captures the latest decisions, not an authority. When given specs that don't align with it, flag the misalignment so that one can be brought in line with the other.
- Add XML documentation comments to every C# class and member.
- Add TSDoc comments to every TypeScript declaration: class, interface, type, and member.
- Nothing inside `docs/` should be referenced in C# XML comments, TSDocs, error messages, or test names/descriptions. Only reference `docs/` in inline code comments.
- Add inline comments in every source file to demarcate non-trivial sections of markup and explain what non-trivial logic does.
- All code and scripts must build and run on both Windows and Linux.
- Maintain a README.md at the root of every project explaining its purpose.
