# Contributing to AIUsageMeter

AIUsageMeter is a small, dependency-free macOS app, and the goal is to keep it that
way.

## Getting set up

Requirements: macOS 14 or newer, and either Xcode or the Apple Command Line
Tools with Swift 6. `Package.swift` detects which toolchain is selected.

```sh
swift build -c release  # compiled with -warnings-as-errors
./scripts/build-app.sh  # writes dist/AIUsageMeter.app
./scripts/run-demo.sh   # launches with deterministic demo data
./scripts/make-dmg.sh   # writes dist/AIUsageMeter-<version>.dmg
```

Build products stay under `.build/` and `dist/`, both of which are ignored.

The fixture-backed test suite is kept in the maintainer's working copy rather
than in this repository, so there is no `swift test` to run here. Send a redacted
sample response with a provider change and it will be turned into a fixture.

## Ground rules

These are the constraints the app is built around. A change that breaks one of
them needs a reason in the pull request.

1. **Read-only provider access.** AIUsageMeter reads a sign-in that another
   application already made. It never refreshes, rotates, or rewrites one, never
   signs in on a user's behalf, and never imports browser cookies.
2. **No secret leaves the Keychain.** Secrets the app owns go in its own Keychain
   item. No credential and no provider response body is printed or written to
   preferences.
3. **No runtime dependencies.** Apple system frameworks only. No Node, no Python,
   no third-party binary.
4. **No vendor artwork.** Provider marks are drawn in code in
   `Sources/AIUsageMeter/Glyphs.swift`. A user can drop their own files into
   `Resources/ProviderMarks`, but the repository ships none.
5. **Only documented endpoints.** If a service publishes usage solely to its own
   web app, the answer is Custom JSON, not a scraped private API.

## Adding a provider

1. Add the case to `ProviderID` in `Sources/AIUsageMeterCore/Models.swift`.
2. Add its connector in `Sources/AIUsageMeterCore/Providers.swift` and its parser
   in `Parsers.swift`.
3. Include a **redacted** sample of the response in the pull request, covering
   the shapes that actually occur: current, legacy, zero, over-limit, and
   malformed. These become the fixtures the parser is tested against.
4. Give it a glyph in `Glyphs.swift` and a line of setup copy in `SupportCopy`
   in `Sources/AIUsageMeter/SettingsView.swift`.
5. Add a row to the provider table in `README.md`.

A sample response must never contain a real token, account ID, or email address.

## Style

Match the file you are editing. Comments are rare and explain why something is
the way it is — an inverted ledger, a hover exit that can go missing — never what
the next line does.

The overlay's geometry derives from `Metrics` in `Design.swift`. Put a new
measurement there rather than inline, so the three overlay sizes keep moving
together.

## Visual changes

`./scripts/capture-demo.sh` renders the overlay to `docs/aiusagemeter-demo.png`
without needing screen-recording permission. It takes an output path as an
optional first argument, `AIUSAGEMETER_DEMO_EXPANDED=0` leaves every card closed,
and `AIUSAGEMETER_SNAPSHOT_TARGET=settings` with `AIUSAGEMETER_SNAPSHOT_PANE`
captures a Settings pane instead. The README lists the exact command behind each
of its screenshots; regenerate the ones a change affects, and keep the pointer
away from the overlay while a notch capture runs, since a hovered gauge opens
its card into the image. A before/after pair in the pull request is the fastest
way to review a change to the notch.

## Pull requests

Keep them focused, make sure `./scripts/build-app.sh` succeeds with no new
warnings, and describe what changed and why.

## Credits

The interface is inspired by the work of [@hivinz_](https://x.com/hivinz_), with
thanks. If you are changing how the overlay looks, please stay within that
language rather than introducing a second one.
