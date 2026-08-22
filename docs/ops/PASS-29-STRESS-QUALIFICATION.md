# Pass 29 load, resilience and security stress qualification

`scripts/stress-qualification.sh` is an isolated destructive qualification harness for the built `DISRoute.Server`. It uses a temporary Development/FileCheckpoint environment and deliberately exercises concurrent mutation/read bursts, same-key idempotency storms, optimistic-concurrency contention, immediate SIGKILL after acknowledged durable writes, malformed and oversized requests, rate-limit behavior, readiness isolation, journal drain, and bounded process resource growth.

This harness is a regression/failure-safety test, **not a production capacity benchmark**. Its local latency/RSS/FD measurements describe only the qualification environment and are not an agency sizing recommendation or SLA. Production capacity, Windows/Raspberry Pi target acceptance, WAN characteristics, live vendor-provider latency, penetration testing, and agency operational acceptance remain separate release/field qualification activities.

Run after a Release build:

```bash
DOTNET_BIN=/path/to/dotnet ./scripts/stress-qualification.sh
```

Machine-readable evidence is written to `docs/ops/evidence/PASS-29-STRESS-EVIDENCE.json`.
