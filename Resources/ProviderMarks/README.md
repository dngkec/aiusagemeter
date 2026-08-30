# Provider marks

UsageMeter ships **drawn** marks (see `Sources/UsageMeter/Glyphs.swift`) rather than
vendor artwork, because redistributing another company's logo is the app owner's
call, not the app's.

To use official artwork instead, drop files here — no code change, no rebuild of
the glyph code, just `scripts/build-app.sh`:

| File | Rendering |
|---|---|
| `<providerID>.png` / `.pdf` | Tinted white, matching the rest of the rail |
| `<providerID>.color.png` / `.color.pdf` | Used exactly as supplied |

`<providerID>` is the raw value from `ProviderID`: `claude`, `codex`, `cursor`,
`grok`, `copilot`, `gemini`, `kimi`, `anthropicCost`, `openAIAPI`, `openRouter`,
`deepSeek`, `mistral`, `perplexity`, `windsurf`, `zai`, `openCode`,
`localModels`, `jetBrainsAI`, `warp`, `amp`, `kilo`, `augment`, `devin`,
`antigravity`, `custom`.

Square, transparent, at least 128 × 128 px (or a PDF) reads best. Anything not
supplied falls back to the drawn mark.
