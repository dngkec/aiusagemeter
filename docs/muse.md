# Meta Muse (catalog entry)

AIUsageMeter lists **Meta Muse** (`muse`) as a catalog provider on both macOS and
Windows. Live built-in fetch is intentionally omitted: Meta’s published Model API
reference documents generation, files, models, and status only — not a usage,
balance, or organisation cost endpoint.

Use **Custom JSON** or **Manual budget** until Meta documents a read-only usage
API. Prefer Meta’s [Usage dashboard](https://dev.meta.ai/help/usage-and-limits/api-usage)
for account spend.

## Verified from Meta (do not invent beyond this)

Sources checked for this entry:

- [Pricing and rate limits](https://dev.meta.ai/docs/pricing-rate-limits)
- [Muse Wonder](https://dev.meta.ai/models/muse-wonder)
- [API reference](https://dev.meta.ai/docs/api-reference)
- [Muse Code subscriptions (help)](https://dev.meta.ai/help/subscriptions/what-is-a-muse-code-subscription)
- [Authentication and billing](https://dev.meta.ai/docs/muse-code/auth)

### Pay-as-you-go token models (USD per 1M tokens)

| Model ID | Cached input | Input | Output | Notes |
|---|---:|---:|---:|---|
| `muse-wonder-1.0` | $0.20 | $4.00 | $20.00 | Standard mode only on Model API |
| `muse-spark-1.3` / `1.2` / `1.1` | $0.15 | $1.25 | $4.25 | Standard mode |
| `muse-spark-1.3-contributor` / `1.2-contributor` | $0.002 | $0.10 | $0.20 | Contributor mode |

Also published on the same pricing page:

- Web search grounding: **$2.50 per 1,000** search queries (text models)
- `muse-image-1.0`: **$0.01 per image**
- `muse-voice-transcribe-1.0`: **$0.18 per hour** of audio
- SAM 3.1: **$2.50 / 1,000 images**, **$0.20 / 1,000 video frames**

### Rate limits (per team, per model)

| Model | RPM | TPM | Other |
|---|---:|---:|---|
| `muse-wonder-1.0` | 3,000 | 4,000,000 | |
| `muse-spark-1.3` / `1.2` / `1.1` | 3,000 | 4,000,000 | |
| `muse-spark-*-contributor` | 150 | 3,000,000 | |
| `muse-image-1.0` | 150 | — | no TPM |
| `muse-voice-transcribe-1.0` | — | — | 256 concurrent streams; 16,000 streams/hour |
| Background responses | 600 submissions/min/team | | in addition to model limits |

### Muse Code subscriptions (flat monthly)

| Plan | Price | Notable access |
|---|---:|---|
| Everyday Usage | $5/month | Muse Spark, Muse Wonder, Muse Image |
| High Usage | $15/month | 5× Everyday |
| Power Usage | $50/month | includes **Muse Wonder Full** |
| Maximum Usage | $200/month | 80× Everyday |

Muse Wonder Full is **not** listed as a pay-as-you-go Model API model; Meta
describes it as included with Muse Code Power / Maximum subscriptions.

### API surface (documented)

- Base URL: `https://api.meta.ai/v1`
- Auth: `Authorization: Bearer $MODEL_API_KEY` (env `MODEL_API_KEY`)
- Documented resources: responses, chat completions, messages, files, models, status

## TODO / unknown (not invented)

- **Documented usage / cost / balance HTTP endpoint:** none found in Meta’s API
  reference or help centre. Third-party tools mention private shapes; AIUsageMeter
  does not call those.
- **Exact Muse Code prompt quotas as hard numbers:** Meta publishes ranges such as
  “10–50 prompts every 5 hours” for Everyday and multipliers for higher tiers, not
  a single fixed prompt count for every plan.
- **Per-key rate-limit budgets separate from team limits:** Meta states limits are
  per team, not per API key.
- **Regional price variants:** subscription docs note benefits/availability may
  vary by region; no alternate regional price table was verified for this entry.
