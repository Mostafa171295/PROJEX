# GitHub repository setup — PROJEX

This checklist is for the repository owner after creating `PROJEX` on GitHub.

## Repository creation

- Repository name: `PROJEX`
- Recommended visibility: **Private** until the code, security posture, licensing, and documentation have been reviewed for public release.
- Initialize with **no** README, `.gitignore`, or license (the source package already contains project files; avoid an unrelated initial commit).
- Default branch: `main`.

## Branch protection for `main`

In **Settings → Rules → Rulesets** (or Branches → Add branch protection rule), create a rule targeting `main`:

- Require a pull request before merging.
- Require at least 1 approval if collaborators are available.
- Dismiss stale approvals when new commits are pushed.
- Require conversation resolution.
- Require status checks to pass before merging; select `Build and test (.NET 10)`.
- Require branches to be up to date before merging.
- Block force pushes and branch deletion.
- Do not allow bypassing the rules except for a deliberate administrator emergency process.

For a solo repository, the approval requirement may be impractical; keep required CI checks and enable approval when collaborators join.

## Actions and security

- **Settings → Actions → General**: allow GitHub Actions required by the included workflows.
- **Settings → Code security and analysis**: enable Dependabot alerts and security updates where available.
- Enable private vulnerability reporting if the repository is private and the feature is available.
- Keep the default `GITHUB_TOKEN` permissions read-only; the workflows declare only the permissions they need.
- Review Dependabot pull requests before merging.

## First push

Run from the extracted project directory:

```bash
git init
git add .
git status
git diff --cached
git commit -m "chore: initial PROJEX enterprise import"
git branch -M main
git remote add origin https://github.com/<YOUR_GITHUB_USERNAME>/PROJEX.git
git push -u origin main
```

Do not paste a GitHub Personal Access Token into source files or chat. Authenticate using Git Credential Manager, GitHub CLI (`gh auth login`), or the browser sign-in prompt.

## Workflow validation

After pushing, open the repository's **Actions** tab and verify:

1. `CI` completes restore, build, and tests.
2. `CodeQL` completes C# analysis.
3. Dependabot creates scheduled dependency update pull requests.

A green workflow is not a substitute for production security review or database migration validation.
