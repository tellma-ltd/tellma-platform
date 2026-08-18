# Identity UI inspection

Tooling for looking at the identity server's pages by hand. Automated suites prove that flows
behave; nothing but a person in front of a browser tells you whether a page reads well, mirrors
correctly in Arabic, or renders the state it was supposed to reach. These two files make getting to
that state cheap.

- **`ui-inspection-guide.md`** — a walkthrough of every renderable page, in an order where each step
  leaves behind the state the next one needs, with the URLs and prerequisites written down. Start
  here; it also lists the one-time local setup (LocalDB, the dev certificate, user secrets).
- **`inspect.ps1`** — the same walkthrough as a menu. It launches the server (or attaches to one you
  already have running), drives the machine parts of each flow — device authorization, token
  redemption, the invitation and Temporary Access Pass APIs — reads sign-in codes and emailed links
  out of the server's own console, and prints instructions for the parts only a person can do.
  Step numbers in the menu match the guide's.

```bash
pwsh eng/identity-ui-inspection/inspect.ps1
```

Both files are tracked; everything the script produces while running — the captured server log, its
error log, the pid file — goes to the repository's gitignored `.tmp` directory, which the script
creates on demand. Nothing it writes lands next to it.
