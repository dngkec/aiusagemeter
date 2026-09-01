# Contributing to AIUsageMeter

AIUsageMeter has two native shells: Swift/AppKit for macOS and .NET 8 WPF for
Windows. Portable Windows logic lives in `src/AIUsageMeter.Core`; platform APIs
stay in their application projects.

## Getting set up

macOS requirements are macOS 14 or newer and Xcode or the Apple Command Line
Tools with Swift 6. Windows requires the .NET 8 SDK and the .NET desktop
workload. `windows-latest` CI is authoritative for WPF compilation and packaging.

```sh
swift build -c release  # compiled with -warnings-as-errors
./scripts/build-app.sh  # writes dist/AIUsageMeter.app
./scripts/run-demo.sh   # launches with deterministic demo data
./scripts/make-dmg.sh   # writes dist/AIUsageMeter-<version>.dmg

dotnet restore AIUsageMeter.Windows.sln
pwsh ./scripts/test-windows.ps1 -Configuration Release   # both suites
dotnet build src/AIUsageMeter.Windows/AIUsageMeter.Windows.csproj -c Release -r win-x64
pwsh ./scripts/package-windows.ps1 -Runtime win-x64
```

`test-windows.ps1` launches the Microsoft.Testing.Platform executables directly. `dotnet test`
wraps those same executables, but its .NET 10 SDK server mode reports `Zero tests ran` with exit
code 5 on some machines — a red suite would arrive looking green, so prefer the script.

Build products stay under `.build/` and `dist/`, both of which are ignored.

The fixture-backed Swift suite is kept in the maintainer's working copy, so there
is no `swift test` in a fresh clone. Portable Windows tests are published under
`WindowsTests/AIUsageMeter.Core.Tests`, and the WPF layer's under
`WindowsTests/AIUsageMeter.Windows.Tests`. Send only synthetic or redacted samples.

## Ground rules

These are the constraints the app is built around. A change that breaks one of
them needs a reason in the pull request.

1. **Read-only provider access.** AIUsageMeter reads a sign-in that another
   application already made. It never refreshes, rotates, or rewrites one, never
   signs in on a user's behalf, and never imports browser cookies.
2. **No secret leaves the platform vault.** Secrets the app owns go in its own
   Keychain item or Windows Credential Manager target. No credential and no
   provider response body is printed or written to preferences.
3. **No third-party runtime dependencies.** macOS uses Apple frameworks; Windows
   uses .NET/WPF, Win32, and Microsoft platform libraries. Release ZIPs are
   self-contained. No Node, Python, browser-cookie store, or vendor binary.
4. **No vendor artwork.** Provider marks are drawn in code in
   `Sources/AIUsageMeter/Glyphs.swift`. A user can drop their own files into
   `Resources/ProviderMarks`, but the repository ships none.
5. **Only documented endpoints.** If a service publishes usage solely to its own
   web app, the answer is Custom JSON, not a scraped private API.

Windows shared credentials must be discovered below `%USERPROFILE%` or
`%APPDATA%` with read-only, shared file access and a size bound. Do not invoke a
provider's bundled database executable, import browser cookies, refresh someone
else's OAuth token, or put a credential on a process command line.

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

For Windows parity, add the parser and connector to `AIUsageMeter.Core`, a
portable MSTest case with synthetic/redacted JSON, and an explicit availability
message if the platform cannot safely provide the credential.

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

Keep them focused and make sure `./scripts/build-app.sh` succeeds with no new
warnings. For Windows changes, also run the portable tests and WPF build above.
Do not claim Windows runtime verification from a macOS cross-build.

## Credits

The interface is inspired by the work of [@hivinz_](https://x.com/hivinz_), with
thanks. If you are changing how the overlay looks, please stay within that
language rather than introducing a second one.
