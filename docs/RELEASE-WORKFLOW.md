# PadForge PRs and releases

Review the PR diff, CI result, latest review findings and any Windows VM evidence before confirmation. The confirmation belongs to one PR head and one `main` commit. It expires after 30 minutes. Cancel the workflow run to withdraw a pending confirmation.

Start **Confirm and merge PR** on `main`. Enter the PR number and the two full commit SHAs. Leave the release version empty for an ordinary PR. A new commit on either side requires a new review and dispatch. The workflow records the confirmation and the verified merge commit in the Actions summary.

For a release, add or update `release-intent.json` in the PR and enter the same version when starting the workflow. Use `-beta.N` for a prerelease. A version with no suffix and `prerelease: false` explicitly selects stable. For changes under `src/`, record the VM test and tested revision in `vmEvidence`.

```json
{
  "version": "0.3.0-beta.1",
  "prerelease": true,
  "channels": ["github"],
  "vmEvidence": "Virtual controller smoke test passed on Windows 11, tested commit <sha>."
}
```

Release builds use the exact merged commit. Check the run for the Windows package verification and GitHub Release result. If publication fails, rerun only the failed job from the original approved run. It verifies the existing tag and any uploaded files before resuming the draft. The Windows ZIP is unsigned. Verify its SHA-256 sidecar before running it.

**Package release candidate** remains available for a manual beta preview in Actions. It produces the ZIP and checksum without creating a tag or GitHub Release.

The merge path requires the private merge app, a `main`-only environment and the exclusive update ruleset. Changes under `.github/` need a separately reviewed maintenance path.
