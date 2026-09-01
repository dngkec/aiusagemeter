# Security policy

AIUsageMeter reads credentials that belong to other applications and stores its own
in macOS Keychain or Windows Credential Manager, so anything touching those is
treated as a security issue rather than a bug.

## Reporting

Please report privately, through
[GitHub's private advisory form](https://github.com/dngkec/aiusagemeter/security/advisories/new),
rather than in a public issue. Include the version, what an attacker could reach,
and the smallest reproduction you have. Please do not include a real key or token.

You can expect an acknowledgement within a week.

## What AIUsageMeter promises

- Provider access is read-only. AIUsageMeter never refreshes, rotates, or rewrites
  a credential another application owns.
- Secrets the app owns go in its own Keychain item
  (`app.aiusagemeter.AIUsageMeter`) or Windows Credential Manager target
  (`AIUsageMeter/*`), never into preferences and never into a log.
- Preferences live in a plain JSON file at
  `~/Library/Application Support/AIUsageMeter/preferences.json` on macOS or
  `%LOCALAPPDATA%\AIUsageMeter\preferences.json` on Windows and contain
  configuration only.
- Requests use system TLS, no browser-cookie import or HTTP cookie store,
  explicit endpoints, strict timeouts, and a 1–2 MB streaming response cap.
  Custom HTTP is limited to localhost; other endpoints require HTTPS. Windows
  redirects are disabled so authorization headers cannot cross hosts.
- Credentials and provider response bodies are never printed.
- Support links are allowlisted in the platform core. Provider dashboards are
  hard-coded with their connector or, for Custom JSON, explicitly configured by
  the user and validated by the same HTTPS/localhost policy.
- Shared Windows CLI credential files are opened read-only and bounded to 2 MB.
- There are no third-party runtime dependencies in either release application.

## Scope

In scope: credential handling, Keychain/Credential Manager use, the URL policy, the file reads that
discover other applications' credentials, and anything that could send provider
data somewhere it should not go.

Out of scope: a provider changing its own API, an incorrect reading caused by an
upstream response, and the absence of notarisation on ad-hoc-signed builds (see
the README for what that means for installation).
