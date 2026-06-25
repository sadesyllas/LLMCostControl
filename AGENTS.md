# AGENTS

## Rules

1. **Adhere to `SPEC.md`.** All code, configuration, and decisions must conform
   to the specification in `SPEC.md`. If a task would require deviating from
   `SPEC.md`, stop and surface the conflict for resolution rather than
   improvising.

2. **Follow `MILESTONES.md` strictly, in order.** Work on milestone `Mn+1` only
   after milestone `Mn` is complete. No skipping, no reordering, no parallel work
   across milestones. Always state which milestone a change belongs to before
   starting it.

3. **A milestone is complete only when it is both `Done?` and `Tested?`.** The
   acceptance criteria in `MILESTONES.md` must be met **and** its required tests
   must pass before marking the milestone's `Done?` and `Tested?` checkboxes as
   `[x]`. Do not begin the next milestone until both boxes for the current one
   are checked.

4. **No untested code advances to the next milestone.** Run the tests defined for
   the current milestone (unit, integration, contract as applicable) and confirm
   they are green before declaring the milestone complete.

5. **Commit at every step.** Create a git commit for each unit of work along the
   way — not just one per milestone. Each commit must be focused, have a clear
   message that references the milestone it belongs to (e.g. `M0: scaffold src
   projects`), and leave the repository in a buildable state. Never commit
   secrets.

6. **Document all types and public methods.** Every class, record, struct,
   enum, interface, and every public method/property must have an XML doc
   comment (`///`) that concisely explains what it does. This applies to
   production code; test code is exempt.

7. **Strictly adhere to the DRY (Don't Repeat Yourself) principle.** Do not duplicate logic, boilerplate, or query patterns across handlers, repositories, or tests. If logic or patterns are repeated, abstract them into reusable helper methods, extension methods, or shared helper classes. Avoid copy-pasting code blocks.

