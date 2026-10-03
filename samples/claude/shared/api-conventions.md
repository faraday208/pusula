# API conventions

- Resources are plural nouns: `/orders` and `/orders/{id}`.
- Status codes carry the outcome: 201 on create, 404 when missing, 409 on conflict, 422 for validation errors.
- Errors are problem details (RFC 9457) with a stable `code` field.
- Lists are paginated with `limit` and `cursor`. Never return an unbounded list.
- Every response carries a request id header for tracing.
- A breaking change needs a new version prefix, such as `/v2/orders`.
