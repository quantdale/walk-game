---
description: Resume the planner-generated or native active campaign
---
First prove repository identity (`sh scripts/assert-repo-identity.sh` or `./scripts/Assert-RepoIdentity.ps1`); on any mismatch STOP without modifying anything. Then read `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, `.agent/EXECUTION_PROMPT.md` if present, and native state. Reconcile `$ARGUMENTS` with current Git. Resume an ACTIVE prompt from the first incomplete requirement through completion; otherwise use native continuation or require planning. Preserve stricter local rules.