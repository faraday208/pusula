---
name: db-migrate
description: Create and apply a database migration. Run only when the user types /db-migrate.
disable-model-invocation: true
---
# Database migration

1. Ask for a short name for the change, such as `AddOrderNotes`.
2. Generate the migration with the project's migration tool.
3. Read the generated SQL before applying it.
4. Apply it to the local database first. Never apply to production from here.
5. Commit the migration together with the model change.
