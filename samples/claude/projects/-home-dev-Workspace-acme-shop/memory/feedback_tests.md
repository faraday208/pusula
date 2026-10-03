---
name: feedback-tests
description: Integration tests must hit a real database, not mocks
type: feedback
---
Do not mock the database in integration tests.

**Why:** a mocked migration test passed last quarter while the real migration failed in staging.

**How to apply:** use the containerized test database. Keep the explanation of the setup short, as described in [[user-role]].
