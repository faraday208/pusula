# Code style

- Indent YAML, JSON and TypeScript with two spaces; C# and Python with four.
- Name things for what they are: `OrderTotal`, not `Calc2`.
- Keep functions under 40 lines. Extract one when a comment starts a new paragraph.
- Prefer early returns over nested conditionals.
- Delete commented-out code; git remembers it.
- Public APIs get a doc comment. Private helpers do not need one.
- HTTP endpoints follow the [API conventions](../shared/api-conventions.md).
