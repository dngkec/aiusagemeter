# Security policy

AIUsageMeter reads credentials that belong to other applications and stores its own
in the macOS Keychain, so anything touching those is treated as a security issue
rather than a bug.

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
  (`app.aiusagemeter.AIUsageMeter`), never into preferences and never into a log.
- Preferences live in a plain JSON file at
  `~/Library/Application Support/AIUsageMeter/preferences.json` and contain
  configuration only.
- Requests use an ephemeral `URLSession`, system TLS defaults, explicit
  endpoints, 15-second request and 25-second resource timeouts, and a response
  size cap.
- Credentials and provider response bodies are never printed.
- The only outward links the app can open are the four in
  `Sources/AIUsageMeterCore/Support.swift`, and it checks a URL against that list
  before opening it.
- There are no third-party runtime dependencies.

## Scope

In scope: credential handling, Keychain use, the URL policy, the file reads that
discover other applications' credentials, and anything that could send provider
data somewhere it should not go.

Out of scope: a provider changing its own API, an incorrect reading caused by an
upstream response, and the absence of notarisation on ad-hoc-signed builds (see
the README for what that means for installation).
