# Project setup and automation

Target repository: `Anheledir/SidePulse.NET`. Owner was verified through the
connected GitHub account. Repository creation and wiki publication are separate
steps; prepared local files are not evidence those remote steps have succeeded.

## Initial repository

Use `main`, enable issues and the wiki, and describe the project as an unofficial
.NET/Spectre.Console companion in the concept stage. MIT licensing and explicit
upstream credit are included. Choose repository visibility deliberately: GitHub
Free provides wikis for public repositories; private wiki availability depends on plan.

The initial files contain no C# project or application implementation.

## Workflows

| Workflow | Behavior now | Later behavior |
|---|---|---|
| Quality | Validate local docs/wiki links, JSON, formatting and Git whitespace | Same checks as docs grow |
| .NET CI | Explicitly skip build jobs when no solution exists | Restore, build and test on Windows, Linux and macOS |
| Wiki sync | Disabled until configured | Publish reviewed docs/wiki pages after main changes or manual dispatch |
| Dependabot | Weekly GitHub Actions updates | Add NuGet when package manifests exist |

Actions are pinned to verified upstream commit SHAs. Workflow permissions are
read-only by default. Wiki publication uses a dedicated credential and runs only
for the repository's default branch, never for pull request content.

The .NET CI workflow expects a single root-level solution with suffix `.slnx` or
`.sln`. Add test projects before marking the build milestone complete; a skipped
initial build is not an application test result. Native release publishing and
CodeQL should be introduced with the first actual application projects, rather
than emitting empty releases or meaningless scans now.

## Wiki publication

1. Enable Wiki in repository settings and create one initial page in GitHub's UI.
   GitHub only exposes the separate wiki Git repository after the first page exists.
2. Add repository Actions secret `WIKI_TOKEN` with a dedicated account credential
   that can push this wiki. Choose the narrowest supported permissions and test it;
   the ordinary workflow token is not assumed to support wiki pushes.
3. Set repository Actions variable `WIKI_SYNC_ENABLED` to `true`.
4. Run **Wiki sync** from `main` and verify the pages. Later changes to `docs/wiki`
   on the default branch are published automatically.

The publisher updates only pages listed in its ownership manifest, retains
unmanaged wiki pages and deletes previously managed pages only when removed from
source. Documentation changes are reviewed in the main repository.
If the secret is missing or the wiki is uninitialized, the workflow fails with a
useful message rather than reporting success. No token is included in repository files.

## Creating the remote repository

`scripts/publish-project.ps1` can create a repository and push this prepared
checkout using an already authenticated GitHub CLI. It defaults to private and
never changes an existing repository's visibility. Wiki seeding remains a GitHub UI
step; the publisher does not pretend a `docs/wiki` folder is already a live wiki.
Prepared backlog issues can be created separately after the concept review.
