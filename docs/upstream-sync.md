# Automatic upstream synchronization

This fork tracks [KotMatrosk1n/KM-Editor](https://github.com/KotMatrosk1n/KM-Editor)
while preserving its own portable Windows build.

## How it works

The `.github/workflows/sync-upstream.yml` workflow runs every six hours
(at minute 17 UTC), and can also be launched through Actions > Sync upstream
and publish portable > Run workflow.

1. Fetch the original repository's `master`.
2. If that commit is already an ancestor of this fork's `master`, do nothing.
3. Otherwise perform a normal, non-force merge into this fork's `master`.
4. Push the merge commit only when the merge succeeds.
5. Call the reusable Portable Windows ZIP workflow with the **exact new commit**.
6. Only after all build and packaging steps pass, create a GitHub Release with
   the ZIP attached and mark it as latest.

Tags are `portable-v<version>-g<fork-sha12>`. Multiple upstream commits with
the same product version remain distinct releases. The upload-artifact ZIP
remains available in the workflow's Artifacts list for 30 days.

## Important safeguards

- On merge conflicts, the sync workflow fails and aborts; it neither overwrites
  the fork's portable modifications nor creates a release. Review the failed
  Actions run and manually resolve the conflicts.
- No force-push, `git reset --hard upstream/master`, or automatic conflict
  preference such as `-X theirs` is used.
- A GitHub Actions push made with `GITHUB_TOKEN` does not fire another
  `push` workflow. The sync workflow therefore calls the portable build
  directly with the merge commit SHA, rather than relying on a push trigger.
- Ordinary pushes to `master` also produce a new portable GitHub Release after
  a successful build. Pull requests validate the portable ZIP without publishing.
- Fork Actions require permission to write repository contents and create releases.
  If the repository has restricted workflow-token permissions or master branch
  protection disallowing bot pushes, update those repository settings or adapt
  the workflow to open a PR for manual merge.
- GitHub scheduled workflows can run later than their cron time and may be
  disabled for inactive repositories; use a manual workflow dispatch to verify
  operation after setup.

Release content is an unsigned (not Authenticode-signed) portable build and is
not an upstream vendor release. The installed app's WiX packaging/updater
pipeline remains separate and is not invoked here.

For runtime data layout see [portable.md](portable.md).
