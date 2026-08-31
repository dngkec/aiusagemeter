# Third-party notices

## Design

The AIUsageMeter interface — the side notch, the gauges, the detail card and its
pointer, and the app icon in `Resources/icons` — is inspired by the work of
[@hivinz_](https://x.com/hivinz_), with thanks. The drawings here are original;
the design language they follow is theirs. If you fork or redistribute
AIUsageMeter, please keep that credit intact.

## Code

AIUsageMeter is an original implementation. Endpoint-shape research and product
behaviour were informed by these MIT-licensed projects:

- **UsageMonitor**, copyright © 2026 Jaco Veldsman — MIT License.
- **Riah Usage**, copyright © 2026 Riah Reckless — MIT License.

Their MIT license terms are reproduced below and are also available in each
upstream project.

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

AIUsageMeter uses Apple system frameworks on macOS and Microsoft .NET/WPF plus
Windows platform APIs on Windows. Release applications ship no third-party
runtime binary dependencies. The portable test project uses the Microsoft-owned,
MIT-licensed MSTest SDK; it is not included in release artifacts.

Provider names and the marks drawn beside them identify the services AIUsageMeter
reads. The marks are original vector drawings built from geometric primitives in
`Sources/AIUsageMeter/Glyphs.swift`. No vendor artwork is bundled, and no
affiliation or endorsement is implied.
