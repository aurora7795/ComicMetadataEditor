# Agent Rules & Guidelines (AGENTS.md)

## Git Branching Rules
- **Never modify code directly on `main`**: Before executing code edits or file modifications, always check the current branch. If the current branch is `main`, automatically create and switch to a descriptive branch (e.g., `feat/feature-name` or `fix/issue-description`) before making changes.
- **Exceptions**: Only remain on the current branch if the work is explicitly a continuation or follow-up related to the current non-main branch.

## Task & Issue Tracking Rules
- Use the GitHub CLI (`gh`) for tracking deferred work and major changes.
- When we encounter a bug or feature that we decide NOT to fix immediately, automatically create a GitHub Issue:
  `gh issue create --title "<Brief Title>" --body "<Detailed description + affected files>"`
- When implementing a feature based on an existing GitHub issue:
  1. Fetch the issue context: `gh issue view <issue-number>`
  2. Reference the issue in commit messages: `Fixes #<issue-number>: <description>`
- Keep issues concise, actionable, and tagged appropriately.
