# Security

- Secrets never go into the repository. Read keys and connection strings from environment variables or a vault.
- Validate input at the boundary and encode output at the sink.
- Use parameterized queries. Never build SQL by joining strings.
- Check tenant ownership on every query that reads customer data.
- Log events, not payloads: no personal data and no tokens in logs.
- Pin dependency versions and read the changelog before upgrading.
- Examples in docs and tests use the reserved address ranges 192.0.2.0/24 and 203.0.113.0/24.
