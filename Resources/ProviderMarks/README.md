# Provider marks

AIUsageMeter draws its own provider marks (see `Sources/AIUsageMeter/Glyphs.swift`
and `src/AIUsageMeter.Windows/Design/ProviderGlyphs.cs`) rather than bundling
vendor artwork, so the repository redistributes no third-party logo. Meta Muse
uses a drawn six-petal bloom on both shells; drop `muse.png` / `muse.pdf` here
only if you want to override that mark locally.

To use official artwork instead, drop files here and run
`scripts/build-app.sh`. No code change is needed.

| File | Rendering |
|---|---|
| `<providerID>.png` / `.pdf` | Tinted white, matching the rest of the rail |
| `<providerID>.color.png` / `.color.pdf` | Used exactly as supplied |

`<providerID>` is the raw value from `ProviderID`: `claude`, `anthropicCost`,
`codex`, `grok`, `cursor`, `copilot`, `gemini`, `kimi`, `muse`, `openAIAPI`,
`openRouter`, `deepSeek`, `mistral`, `xaiAPI`, `moonshot`, `perplexity`,
`windsurf`, `zai`, `openCode`, `localModels`, `jetBrainsAI`, `warp`, `amp`,
`kilo`, `augment`, `devin`, `antigravity`, `custom`.

Square, transparent, at least 128 × 128 px, or a PDF, reads best. Anything not
supplied falls back to the drawn mark.
