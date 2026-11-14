# Internal release runbook

Release automation is manual and uses the repository GITHUB_TOKEN; no PAT is needed. Merge these workflows into the default branch before dispatching them. Both workflows accept dispatches only from the default branch. Each preparation uses the selected dispatch's full `github.sha`, so subsequent branch advancement does not change its source.

1. Commit matching `X.Y.Z` versions in `src/LunaPlaynite/extension.yaml` and `src/LunaPlaynite/LunaPlaynite.csproj`. Do not create the tag.
2. Run **Prepare release** from the default branch with `expected_version: X.Y.Z`. It runs the same shared Windows/Linux build and checks as CI, including Python tests, WPF smoke, pinned Playnite packaging, and installed dependency smoke.
3. Wait for the entire run to succeed. The candidate job summary links the normal draft release and records its source SHA, candidate ID (`run_id.run_attempt.full_source_sha`), package name, and SHA256. Preparation creates no Git tag. Download the attached `.pext`, verify its hash against the summary, and test those bytes in Playnite. Edit the draft release notes as needed; the first draft starts with GitHub-generated notes.
4. Run **Publish release** from the default branch, entering the same version and the exact candidate ID you tested. It validates the current draft, the latest successful preparation attempt, committed and packaged versions, asset IDs, and SHA256 values. It publishes the attached bytes without rebuilding and lets GitHub create `vX.Y.Z` at the recorded full source SHA.

To fix a candidate, commit the fix without changing its unpublished version, then start a **new Prepare release run**. Existing hand-edited notes are preserved. The replacement candidate must be tested again and has a different candidate ID. Preparation and publication share a per-version concurrency group with cancellation disabled. GitHub concurrency retains at most one pending run and does not guarantee FIFO ordering; stale preparation runs are also rejected using the prepare workflow's run number and attempt.

The reserved `<!-- luna-candidate:... -->` comment in release notes and the attached `luna-candidate.json` belong to automation. Do not edit them. Preparation writes a `preparing` marker before changing assets and seals a `ready` marker only after uploads and downloaded bytes verify. Any partial mutation therefore blocks publication; repair by a new prepare run. A ready marker alone is insufficient: publication also requires the entire exact prepare run to have succeeded, with no later rerun attempt. A failed run before any draft mutation leaves the previous candidate intact.

Existing published releases are never edited, and existing tags are never moved. A matching tag left by a partially completed publication may be used to finish that publication. Repeating Publish release for the exact already-published candidate validates it and reports success without mutation. A tag pointing elsewhere fails closed. Preparation refuses an existing tag entirely. Failures caused by network/API errors may be retried; use a new preparation run when replacing a failed or changed candidate.

Only candidate/publish jobs have `contents: write`; tests run with read permission and checkout credentials disabled. Publish also has `actions: read` to verify the preparation run. The workflow files and release scripts on the default branch are part of the trust boundary. Publication does not rely on token-generated tag/release events triggering another workflow.

Maintainers can still use GitHub's native publish button, bypassing these workflow checks. This automation cannot prevent that. Protect the default branch and restrict who can dispatch workflows according to the repository's normal maintenance policy.

GitHub may reject release API operations if the recorded source has different workflow files from the current default branch and the token lacks workflow modification rights. Prepare a new candidate; never retarget an old candidate to the latest branch head to bypass this check. See the [GitHub Releases REST API](https://docs.github.com/en/rest/releases/releases#create-a-release).

Local protocol tests use a fake API and make no GitHub writes:

```powershell
python -m unittest discover -s tools/Release -v
```
