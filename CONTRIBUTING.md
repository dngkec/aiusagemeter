# Contributing to UsageMeter

Thanks for being here. UsageMeter is a small, dependency-free macOS app, and the
goal is to keep it that way.

## Getting set up

Requirements: macOS 14 or newer, and either Xcode or the Apple Command Line
Tools with Swift 6. Both work — `Package.swift` detects which toolchain is
selected.

```sh
swift build -c release  # compiled with -warnings-as-errors
./scripts/build-app.sh  # writes dist/UsageMeter.app
./scripts/run-demo.sh   # launches with deterministic demo data
./scripts/make-dmg.sh   # writes dist/UsageMeter-<version>.dmg
```

Build products stay under `.build/` and `dist/`, both of which are ignored.

The fixture-backed test suite lives in the maintainer's working copy rather than
in this repository, so there is no `swift test` for you to run. Send a redacted
sample response with a provider change and it will be turned into a fixture.

## Ground rules

These are the constraints the app is built around. A change that breaks one of
them needs a good reason in the pull request.

1. **Read-only provider access.** UsageMeter reads a sign-in another application
   already made. It never refreshes, rotates, or rewrites one, and it never
   signs in on a user's behalf or imports browser cookies.
2. **No secret leaves the Keychain.** Secrets the app owns go in its own Keychain
   item. Nothing about a credential — or a provider's response body — is ever
   printed or written to preferences.
3. **No runtime dependencies.** Apple system frameworks only. No Node, no Python,
   no third-party binary.
4. **No vendor artwork.** Provider marks are drawn in code in
   `Sources/UsageMeter/Glyphs.swift`. A user can drop their own files into
   `Resources/ProviderMarks`, but the repository ships none.
5. **Only documented endpoints.** If a service publishes usage solely to its own
   web app, the answer is Custom JSON, not a scraped private API.

## Adding a provider

1. Add the case to `ProviderID` in `Sources/UsageMeterCore/Models.swift`.
2. Add its connector in `Sources/UsageMeterCore/Providers.swift` and its parser
   in `Parsers.swift`.
3. Include a **redacted** sample of the response in the pull request, covering
   the shapes that actually occur: current, legacy, zero, over-limit, and
   malformed. These become the fixtures the parser is tested against.
4. Give it a glyph in `Glyphs.swift` and a line of setup copy in `SupportCopy`.
5. Add a row to the provider table in `README.md`.

A sample response must never contain a real token, account ID, or email address.

## Style

Match the file you are editing. Comments explain *why* a thing is the way it is —
the geometry that keeps a card pointed at its gauge, the grace period that stops
a dropped hover from stranding a card — not what the next line does.

The overlay's geometry all derives from `Metrics` in `Design.swift`. If you add a
measurement, put it there rather than inline, so the three overlay sizes keep
moving together.

## Visual changes

`./scripts/capture-demo.sh` renders the overlay to `docs/usagemeter-demo.png`
without needing screen-recording permission, and
`USAGEMETER_SNAPSHOT_TARGET=settings` captures the Settings window instead.
A before/after pair in the pull request is the fastest way to review a change to
the notch.

## Pull requests

Keep them focused, make sure `./scripts/build-app.sh` succeeds with no new
warnings, and describe what you changed and why. Small, well-explained changes
get merged much faster than large ones.

## Credits

The interface design is by [@hivinz_](https://x.com/hivinz_). If you are changing
how the overlay looks, please stay within that language rather than introducing a
second one.
