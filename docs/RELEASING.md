# Releasing

A version is **a git tag and nothing else.** No `.csproj`, `package.json` or source constant carries
one; `deploy/debian/control` holds `@VERSION@` and `build-deb.sh` substitutes the tag at build time.

```bash
git checkout main && git pull
git tag v0.8.0
git push origin v0.8.0
```

That fires `.github/workflows/release.yml`, which runs the API suite, builds the tarball and `.deb`,
writes `SHA256SUMS`, and publishes a GitHub Release. **The suite runs before packaging on purpose** —
an untested artifact must never reach the plant's server.

The app is **published once and packaged twice**: `build-release.sh --app-out DIR` keeps its published
tree, and `build-deb.sh --from DIR` packages that same tree. Before 2026-08-20 each script ran its own
identical `dotnet publish`, so a release compiled the self-contained app twice — the slowest part of
the job, and it meant the tarball and the `.deb` were two separate builds that merely ought to agree
rather than the same software in two wrappers. Run either script on its own and it still publishes for
itself, so an ad-hoc local build needs no extra flags.

Restore, build and test are three steps rather than one so the job summary says *which* was slow. The
job also carries `timeout-minutes: 45` — well above a healthy run (~10 minutes) and far below GitHub's
6-hour default, because a release that hangs invites someone to cancel it part-way through publishing.

## When to tag: at the end of a batch

**Tag when a coherent piece of work is finished, not on a calendar.** A batch is a thing you could
describe to the plant in a sentence: "the label subsystem", "the Oracle correctness sweep", "warehouse
skid management". When one lands, tag it.

This is a real lesson rather than a preference. `v0.7.0` was cut on 2026-07-24 and the next tag was
`v0.8.0` on 2026-08-06 — **86 commits later**, spanning three separate subsystems. Nothing broke, but
by then the release notes had to be reconstructed by reading the log, and there was no single artifact
anyone could point at and say what was in it. During `0.6` tags were cut steadily (`0.6.1` … `0.6.16`);
the habit simply lapsed, and nothing in the repository noticed.

Now something does — see **drift** below.

## Which number

Pre-1.0, so semver's usual promises do not apply, but the shape still holds:

| bump | when | example |
|---|---|---|
| **minor** `0.7.0 → 0.8.0` | a new subsystem, a new capability, or a change users would notice | the label subsystem |
| **patch** `0.8.0 → 0.8.1` | fixes and hardening within what already exists | a bad bind, a wrong scale |

The milestone line is in [REMAINING_WORK.md](REMAINING_WORK.md): `0.5.0` EDI complete, `0.6`–`0.8`
feature-gap batches, `0.9.x` parity and hardening, `1.0.0` cutover-ready.

**A tag marks what is in the tree, not what is commissioned on the plant floor.** `v0.8.0` shipped the
label subsystem without a single label having printed in production, and the changelog says so under
*Known limitations*. That is the honest split: the tag says the code exists and passes; the changelog
says what has and has not been proven on real hardware.

## Notes

Write the entry in [`CHANGELOG.md`](../CHANGELOG.md) **before** tagging. The release workflow prefers
that section over GitHub's auto-generated list, because a list of PR titles describes what was merged
and not what changed for anyone using it.

Group by area, and include a **Known limitations** section. Anyone reading a release note is deciding
whether to deploy it; what is *not* finished is the part that decides that.

## Drift

`ci.yml` runs a `version-drift` job on `main`. It counts commits since the newest tag and **warns** in
the job summary past a threshold. It does not fail the build — an unreleased commit is not an error,
and a check that blocks merges would just get ignored. It exists so the gap is visible while it is
still small enough to describe.
