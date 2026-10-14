<!-- Title: "RT-<n>: <task title>". One pull request per Paca task, from feature/RT-<n>-<short-name>
     or fix/RT-<n>-<short-name>. See README "Contributing". -->

## Paca task

RT-<n>: <link to the task in Paca>

## What changed

-

## How to test it

1.

## Checklist

- [ ] Build, lint and all tests pass locally (`dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`; `npm run lint`, `npm run test:ci`, `npm run build` in `src/Web`)
- [ ] New behaviour is covered by automated tests
- [ ] No secrets, connection strings, internal hostnames, personal data or real client data (the repository is public)
- [ ] Docs updated where behaviour or setup changed (README, `CLAUDE.md`, Paca docs such as *Developer Setup & Pipelines*)
- [ ] Specialist reviews requested on the task where needed (`review-security`, `review-ux`, …)
