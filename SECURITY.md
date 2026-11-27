# Security

## Git credential hygiene

- Never embed tokens in remote URLs. Use `https://github.com/OWNER/REPO.git` or SSH.
- Store credentials in a secret manager or via `gh auth login`; never in `.git/config`,
  `.git-credentials`, scripts, or docs.
- `scripts/check-git-secrets.sh` scans every git config file git reads (local, global,
  system, includes) and plaintext credential stores for GitHub token patterns and URLs
  with embedded userinfo. It prints only file/line and exits non-zero on findings.
- The check runs in the lefthook `pre-push` hook.

### If a token was exposed

1. Revoke it in GitHub: Settings → Developer settings → Personal access tokens
   (classic or fine-grained) → delete the token.
2. Create a least-privilege replacement: fine-grained, scoped to the required
   repository, granting only Contents and Pull requests (read/write), with an expiry.
3. Store it in a secure secret store and load it at runtime (for example
   `gh auth login` or a secret-manager export). Do not paste it into URLs or files.
4. Strip credentials from remotes:
   `git remote set-url origin https://github.com/OWNER/REPO.git`
5. Re-run `scripts/check-git-secrets.sh` and confirm it passes.
6. Audit recent token usage and revoke any additional sessions/SSO authorizations.
