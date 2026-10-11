# Task extraction evaluation

`fixtures.json` contains 30 synthetic gold cases: 15 Ukrainian and 15 English.
No customer notes, credentials, or real people are included. Each case fixes the
reference calendar date, IANA time zone, and permitted assignee names and IDs.

The gold records cover explicit commitments, multiple actions, duplicate first
names, unknown assignees, missing deadlines, ambiguous relative deadlines,
unagreed suggestions, prompt injection, DST gaps/folds, and UTC date boundaries.
Date-only deadlines use **23:59 local time**, with an interpretation label in the
draft. Ambiguous dates stay unresolved. This is a product convention that users
can edit before committing.

## Metrics

Keep these separate:

- Strict JSON schema validity: a syntactically valid document alone does not
  establish extraction quality.
- Grounded output validity: source spans and quotes must match the notes exactly.
- Task precision and recall: score actionable candidates against the gold source
  actions, with one-to-one matching. Suggestions do not count as obligations.
- Assignee and deadline accuracy: score matched actions against the expected
  permitted ID or unresolved `null`, and expected UTC deadline or `null`.
- Inaccessible assignee violations: count raw model IDs outside the permitted
  list separately; no such ID may survive the server validation boundary.
- Failure, latency, and token usage: refusal, truncation, cancellation, schema
  failure, and unavailable provider are failures, never an empty successful run.
- Estimated cost: use explicitly configured deployment-specific input/output
  rates and currency. Missing usage keeps the conservative reservation at the
  higher rate. This estimate is not an invoice and omits cached discounts,
  negotiated pricing, provisioned capacity, and other Azure resources.

Do not report scores from a fixture-backed deterministic provider as model
quality. Unit tests establish validator and harness behavior. Real inference
requires an explicitly selected Azure deployment, credentials, a bounded token
budget, and an opt-in run. No real inference scores have been recorded here yet.

## Opt-in runner

Build the `tools/TaskExtraction.Evaluation` project with locked restore through
the repository's normal .NET verification. From the repository root, running it
without `--run-live` reports that live evaluation is skipped, exits 2, and makes
no cloud call.

A live run requires `AI__Provider=Azure`, `AI__Endpoint`, `AI__Deployment`, and
valid Azure authentication. It also requires all of these explicit arguments:

- `--run-live`
- `--max-token-budget` (1–1000000)
- `--input-price-per-million` and `--output-price-per-million`
- `--currency` (three uppercase letters)
- `--max-estimated-cost` in that currency

Obtain rates for the actual model, deployment, region, and billing arrangement
from [Azure OpenAI pricing](https://azure.microsoft.com/en-us/pricing/details/azure-openai/).
No model price is hardcoded. Both rates must use the same currency. Decimal
arguments use a period, without thousands separators.

The runner preflights every synthetic case before inference. The cost upper bound
uses the complete token reservation at the higher configured token rate. If
either declared budget is insufficient, no inference starts. The default report
path is `.local/ai-evaluation-report.json`; optional `--fixtures` and `--output`
select explicit paths. Authentication, unavailable-provider, rate-limit, or
cancellation failures stop subsequent calls and mark remaining cases as not run.

The runner bypasses HTTP/SQL because it is an operator evaluation tool for
synthetic fixtures; its explicit run budgets are separate from application user
and product SQL daily counters. It does not persist task drafts or create tasks.
Exit 0 means the evaluation completed without provider/validation failures; it
does not assert that precision/recall meets a release threshold.

Example from PowerShell, with rates/currency/cap supplied explicitly for the
selected deployment:

```powershell
$evaluationArgs = @(
    '--run-live', '--max-token-budget', '400000',
    '--input-price-per-million', $env:AI_EVAL_INPUT_PRICE,
    '--output-price-per-million', $env:AI_EVAL_OUTPUT_PRICE,
    '--currency', $env:AI_EVAL_CURRENCY,
    '--max-estimated-cost', $env:AI_EVAL_MAX_COST
)
dotnet run --project tools/TaskExtraction.Evaluation --configuration Release --no-build -- @evaluationArgs
```

The report records the fixture SHA256 so comparisons can identify the exact gold
data. Raw and validated assignee/deadline accuracy are separate. Missing or zero
input usage is untrustworthy for a nonempty request and retains the reservation.

Source task matching is one-to-one after trimming sentence-end punctuation.
Paraphrase semantics still need human review; source precision/recall alone
cannot establish whether every generated title correctly describes the action.

## Data handling

The evaluation report should contain case IDs, model/deployment metadata, counts,
latency, and safe failure codes. It must omit note text, raw completions, prompts,
API keys, credential material, and raw exception bodies. Gold expectations must
not be sent to the inference provider.

## Official references

- [Azure OpenAI .NET SDK 2.1.0](https://www.nuget.org/packages/Azure.AI.OpenAI/2.1.0)
- [Azure Identity .NET SDK 1.21.0](https://www.nuget.org/packages/Azure.Identity/1.21.0)
- [Azure structured outputs and supported schema subset](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs?view=foundry-classic)
