# Release vX.Y.Z-mN: <milestone theme>

<!-- Copy to docs/releases/<tag>.md (for example v0.1.0-m0.md) in the milestone's release pull
     request. This file becomes the text of the tag's GitHub Release. The rules are in Paca,
     Delivery/00 - Milestones & Git Release Plan §5. Delete these comments. -->

| Item | Value |
| --- | --- |
| Tag | `vX.Y.Z-mN` (annotated) |
| Verified commit | `<sha>` on `main`: the commit the exit criteria were checked on. The tag points to the release-notes commit on top of it |
| Date | YYYY-MM-DD (UTC) |
| Milestone | MN: <theme> (*Delivery/00* §3) |
| Previous tag | `v…`, or none for M0 |

## What's new

<!-- One row per task merged since the previous tag, release tasks left out:
     git log --first-parent --format=%s <previous tag>..<verified commit> | grep -o 'RT-[0-9]*' | sort -u -t- -k2n -->

| Task | What a user or developer can now do |
| --- | --- |
| RT-… | … |

## Demo

The demo outcome from *Delivery/00* §3, the steps followed, and the result: who ran it, on which
date, and where it was shown.

## Run this release from a clean clone

```powershell
git clone https://github.com/tamer196/ReportingTool.git reporting-tool
cd reporting-tool
git checkout vX.Y.Z-mN
```

Then follow the README's quick start. List any new prerequisites or steps here.

## Exit criteria (*Delivery/00* §4)

| # | Criterion | Result and evidence |
| --- | --- | --- |
| 1 | Every story in scope is Done and verified by QA | |
| 2 | `docker compose up` from a clean clone, with the seeded data | |
| 3 | All CI checks pass on `main`; no open Blocker or Major bugs | CI run for `<sha>` |
| 4 | The demo script runs end to end | See "Demo" |
| 5 | Licence inventory updated (*ADR-002* §8) | |
| 6 | Local-only check: no outbound calls, no hard-coded hosts or secrets | |
| 7 | Migrations run forward from the previous tag | |
| 8 | CHANGELOG entry and these notes written; tag and GitHub Release created | |

## Database migrations

The migrations added since the previous tag. They run forward from it.

## Licence inventory changes

Components added, removed or re-pinned since the previous tag (*ADR-002* §8).

## Known issues

Open Minor or Trivial bugs, and anything deferred to a later milestone.

## Upgrade notes for developers

Changes an existing clone needs, for example new configuration keys, secrets to regenerate with
`scripts/init-local-secrets`, or a `docker compose down -v`.
