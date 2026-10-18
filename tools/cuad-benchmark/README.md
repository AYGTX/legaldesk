# CUAD benchmark of LegalDesk's rule engine

Measures how LegalDesk's deterministic rules (`ContractRuleEngine`) detect clause categories in **CUAD**, the
Contract Understanding Atticus Dataset: 510 real commercial contracts in which lawyers labelled 41 clause categories
([github.com/The-Atticus-Project/cuad](https://github.com/The-Atticus-Project/cuad)).

The dataset is downloaded locally and **not committed**; no CUAD clause is copied into the product. The repository
does not state its license in the README — check the Atticus Project's terms before redistributing any of it.

## How to run

```powershell
# 1. Download and extract the dataset (18 MB) outside the repository
curl -L -o data.zip https://raw.githubusercontent.com/The-Atticus-Project/cuad/main/data.zip
Expand-Archive data.zip -DestinationPath cuad

# 2. Export one line per contract with its labelled categories
python tools/cuad-benchmark/export_cuad.py cuad/CUADv1.json contracts.jsonl

# 3. Run LegalDesk's rule engine on each contract (~4 s for 510 contracts)
dotnet run --project tools/cuad-benchmark -- contracts.jsonl predictions.jsonl

# 4. Score
python tools/cuad-benchmark/score.py contracts.jsonl predictions.jsonl report.md
```

## Method

- **Contract-level.** For a category, a contract is *positive* when lawyers highlighted at least one clause of that
  category; it is *predicted positive* when LegalDesk raised a rule mapped to the category.
- **Rules only.** No language model, no meaning-based matching, no corpus service.
- **Only comparable categories.** Seven CUAD categories map closely enough to an existing rule; the others have no
  rule yet (see Harvey's coverage page) or a rule that asks a different question.
- **English contracts, US practice.** The rules were written mainly for French Tunisian contracts; the English
  wording they know is narrower.

## Results — first run (18 October 2026)

| CUAD category | LegalDesk rule | Contracts labelled | Raised | Correct | Precision | Recall | F1 |
|---|---|---:|---:|---:|---:|---:|---:|
| Uncapped Liability | liability | 111 | 4 | 0 | 0% | 0% | 0.00 |
| Renewal Term | auto_renewal | 176 | 72 | 72 | 100% | 41% | 0.58 |
| Exclusivity | exclusivity | 180 | 403 | 177 | 44% | 98% | 0.61 |
| Non-Compete | exclusivity | 119 | 403 | 111 | 28% | 93% | 0.43 |
| Liquidated Damages | penalty | 61 | 93 | 39 | 42% | 64% | 0.51 |
| Price Restrictions | resale_price | 15 | 30 | 1 | 3% | 7% | 0.04 |
| No-Solicit of Customers | market_sharing | 34 | 27 | 2 | 7% | 6% | 0.07 |

## What it shows

- **Automatic renewal is precise**: every one of the 72 contracts flagged is right (100% precision), but only 41% are
  found — English renewal wording beyond the phrases the rule knows is missed.
- **Exclusivity finds almost everything (98%) but over-fires**: it is raised on 403 of 510 contracts. Words such as
  "exclusive" also appear in "non-exclusive licence" or "exclusive jurisdiction", which are not exclusivity
  commitments.
- **Uncapped liability is missed**: in US contracts it is rarely written as "unlimited liability"; it appears as
  exclusions from the liability cap ("the foregoing limitation shall not apply to…").
- **Penalty clauses** are found two times out of three, with false alarms on other uses of "penalty".
- **Resale price and customer non-solicitation** are measured on few contracts and the rules target the French
  wording of Tunisian law; the scores say little about French contracts.

## Results — after refining three rules (18 October 2026)

Changes, each with its own unit tests (`tests/SignFlow.UnitTests/Application/RuleRefinementTests.cs`):

- **Exclusivity** ignores uses of "exclusive" that are not a commitment: exclusive property, ownership, remedy,
  warranty or liability, exclusive jurisdiction or venue, "non-exclusive" grants, "exclusive of", "exclusively related
  to" — and their French equivalents (« propriété exclusive », « compétence exclusive », « à titre non exclusif »).
- **Uncapped liability** is also found as a **cap with broad exceptions**: a liability cap that leaves indemnities,
  confidentiality, intellectual property or data outside it. Exceptions for fraud or gross negligence alone are
  standard (and required in Tunisian law) and are deliberately not flagged — a legal choice that costs recall against
  CUAD, whose label counts every exception.
- **Automatic renewal** knows English wording: "shall renew automatically", "automatically extended", "successive
  periods of", "unless either party gives notice of its intention not to renew".

| CUAD category | LegalDesk rule | Contracts labelled | Raised | Correct | Precision | Recall | F1 | F1 before |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Uncapped Liability | liability | 111 | 102 | 40 | 39% | 36% | **0.38** | 0.00 |
| Renewal Term | auto_renewal | 176 | 134 | 119 | 89% | 68% | **0.77** | 0.58 |
| Exclusivity | exclusivity | 180 | 298 | 169 | 57% | 94% | **0.71** | 0.61 |
| Non-Compete | exclusivity | 119 | 298 | 101 | 34% | 85% | **0.48** | 0.43 |
| Liquidated Damages | penalty | 61 | 93 | 39 | 42% | 64% | 0.51 | 0.51 |
| Price Restrictions | resale_price | 15 | 30 | 1 | 3% | 7% | 0.04 | 0.04 |
| No-Solicit of Customers | market_sharing | 34 | 27 | 2 | 7% | 6% | 0.07 | 0.07 |

All 262 LegalDesk unit tests still pass: French contracts behave as before.

The remaining exclusivity "false alarms" are a long tail of one-off phrases, some of them genuine exclusivity that
CUAD did not label (an "exclusive supplier operating agreement", a clause titled "Exclusivity"). Tuning further
would start fitting this dataset rather than improving the rule.

## Next steps

1. Penalty: separate liquidated damages from other uses of "penalty" (tax or regulatory penalties).
2. Non-compete: a rule of its own, apart from exclusivity, for "shall not compete" restrictions.
3. Feed CUAD-style English examples into meaning-based matching once their license is confirmed.
4. Re-run the benchmark after each rule change and keep this table up to date.
