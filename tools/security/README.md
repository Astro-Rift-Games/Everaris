# Secret scanner

Run from the repository root:

```sh
node tools/security/scan-secrets.js
```

An optional directory argument selects another working tree. The scanner checks
text files up to 25 MB and all Git history (`git log --all -p -U0`). History uses
the same ignored file names, directories and binary extensions as the working-tree
scan. Findings include the commit, path and new-file line number from unified diff
hunks; synthetic parser input without a hunk header falls back to line 1.

- Exit **0**: scan completed without critical findings. Classified development
  markers remain informational and redacted.
- Exit **1**: critical findings **or an incomplete scan**. Git spawn failures,
  stream failures, nonzero exits and signal termination are errors, never a clean
  history result. Success is reported only after Git's process `close` event.

Git stderr is drained to avoid blocking the child process. Failure diagnostics
report the exit code and whether stderr was received, but withhold its contents
and raw exception messages to avoid leaking credentials through diagnostics.
The pre-push hook and CI already reject a nonzero scanner exit; no caller changes
are required.

Focused tests (fake Git processes and streams, no network):

```sh
node --test backend/tests/security.scan.test.js
```
