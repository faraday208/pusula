---
name: project-checkout
description: Checkout rewrite, moving to the new payment provider
type: project
---
The checkout service is moving to a new payment provider. Unrelated merges freeze on the 15th.

**Why:** the old provider ends support at the end of the quarter.

**How to apply:** flag any change that touches the payment flow so it can be scheduled around the freeze. Milestones are tracked in [[project-roadmap]].
