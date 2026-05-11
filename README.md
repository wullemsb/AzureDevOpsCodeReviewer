[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/wullemsb/AzureDevOpsCodeReviewer)

# Azure DevOps Copilot Reviewer

This console app listens for Azure DevOps pull request webhook events, detects when a target reviewer is assigned, asks GitHub Copilot to review the changed files, and posts a summary comment back to the PR.

## Prerequisites

- .NET 8 SDK
- GitHub Copilot CLI installed and authenticated
- Azure DevOps Personal Access Token (PAT) with access to the repo

## Configuration

Update appsettings.json with your values:

- AzureDevOps.Organization
- AzureDevOps.Project
- AzureDevOps.RepositoryId
- AzureDevOps.Pat
- AzureDevOps.TargetReviewer (email or user id)
- Webhook.Token (optional shared secret)

If you want to provide a GitHub token explicitly, set Copilot.GitHubToken. Otherwise, the app uses your logged-in Copilot CLI session.

## Run

```bash
dotnet run
```

## Azure DevOps Service Hook

Create a service hook that sends pull request events to:

```
http://<host>:<port>/webhook
```

If you set Webhook.Token, include the same value in the request header defined by Webhook.TokenHeaderName.

## Notes

- Reviews are posted as general PR comments (not inline on code lines).
- The app limits review size by file count and file size; adjust Review settings as needed.
