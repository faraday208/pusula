---
paths:
  - "**/*.test.ts"
  - "**/*Tests.cs"
  - "tests/**"
---
# Testing

- Name tests `Method_Scenario_Expectation`.
- One behavior per test: arrange, act, assert, in that order.
- Prefer a real test database over mocks for the data layer.
- Cover the failure path of every public function, not only the happy path.
- Keep tests deterministic: inject the clock and seed random numbers.
- Aim for 80% line coverage overall and 100% on payment and auth code.
