# Scenario: fixture card missing its Brief section (test-only)

Used by tools/test-agent-playtest-modes.ps1 to prove Read-ScenarioCard fails LOUDLY, naming the
missing section, rather than falling back to a plain run. Deliberately has no "## Brief" heading.

## Setup

fresh

## Expected observation

This card must never load -- its Brief section is missing on purpose.
