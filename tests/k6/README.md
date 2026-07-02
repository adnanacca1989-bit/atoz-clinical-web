# k6 Load & Stress Testing

Scripts for measuring A to Z Clinical under concurrent load.

## Prerequisites

- [k6](https://k6.io/docs/get-started/installation/) installed
- Target environment URL (default: production Render URL)

## Load test scenarios

Run each scenario separately and record output.

```bash
# 100 concurrent users (default)
k6 run tests/k6/load-test.js

# 250 users
k6 run -e VUS=250 tests/k6/load-test.js

# 500 users
k6 run -e VUS=500 tests/k6/load-test.js

# 1000 users
k6 run -e VUS=1000 tests/k6/load-test.js
```

### Optional environment variables

| Variable | Purpose |
|----------|---------|
| `BASE_URL` | Target site (default `https://atoz-clinical.onrender.com`) |
| `VUS` | Virtual users for load test |
| `USERNAME` / `PASSWORD` | Authenticated dashboard path in load test |
| `HEALTH_TOKEN` | `X-Health-Token` for extended `/health` metrics |

### Example with auth

```bash
k6 run -e VUS=100 -e BASE_URL=https://atoz-clinical.onrender.com \
  -e USERNAME=vendor -e PASSWORD='your-password' tests/k6/load-test.js
```

## Stress test (find breaking point)

Ramps request rate until errors dominate:

```bash
k6 run tests/k6/stress-test.js
```

## Metrics captured

- Average response time (`http_req_duration.avg`)
- 95th percentile (`http_req_duration p(95)`)
- Throughput (`http_reqs`)
- Error rate (`http_req_failed`, custom `errors`)
- Virtual users (`vus_max`)

For CPU, memory, and database saturation, pair k6 with Render metrics or your host monitoring during the run.

## Safety

- Do not run high VU counts against production without approval.
- Prefer a staging clone with the same `DATABASE_URL` pool limits for destructive stress tests.
- Auth POST endpoints are rate-limited; expect 429s under aggressive login load.
