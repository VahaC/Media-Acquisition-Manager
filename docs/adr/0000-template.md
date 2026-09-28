# N. <The rule itself, as one declarative sentence>

Status: accepted (V<x.y>)

## Context

What forced the decision. Name the concrete failure the obvious choice would cause: the state it
leaves behind, who sees it, and when. If the roadmap/phase text says otherwise, quote it here and
say plainly that the code departs from it and why.

## Decision

**One bold sentence that states the decision.** Then the arguments, each as a short bold-led
paragraph. Use a small table when comparing the options you rejected.

Name the test that pins the decision, so nobody "fixes" it back:
`<FeatureHttpTests>.<Method_scenario_expected>` asserts it.

## Consequences

What this makes easier, what it makes harder, and the gap that is left on purpose (say it
honestly rather than implying full coverage).

<!-- Optional sections, only when they earn their place:
## Anti-consequence      — what this ADR does NOT license, so it isn't stretched into a blanket rule
## Note for future readers
## Enforcement (added V<x.y>)   — append amendments as new version-labelled sections; never rewrite
-->
