# Scenario: fixture card (test-only)

Used by tools/test-agent-playtest-modes.ps1 to prove Read-ScenarioCard parses all four sections and
that the optional Backend predicate round-trips through its fenced JSON shape. Not a real scenario --
never referenced by -Scenario in a live run.

## Setup

fresh

## Brief

Open the shop and price one item.

## Expected observation

XYZZY_EXPECTED_MARKER_NEVER_IN_ACT_PROMPT: the shelf shows a priced item.

## Backend predicate

```json
{"kind":"action","field":"action","equals":"SendSupplyAction"}
```
