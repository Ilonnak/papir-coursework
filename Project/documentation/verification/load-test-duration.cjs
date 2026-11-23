const fs = require("node:fs");
const path = require("node:path");
const { performance } = require("node:perf_hooks");

const url = "http://localhost:5150/api/products";
const durationMs = 30000;
const levels = [1, 5, 10];
const startedAt = new Date().toISOString();
const folder = path.resolve(
  "load-test-duration-" + startedAt.replace(/[:.]/g, "-")
);

async function request() {
  const start = performance.now();
  let status = 0;
  let error = "";

  try {
    const response = await fetch(url, {
      signal: AbortSignal.timeout(10000)
    });
    status = response.status;
    await response.json();
    if (status !== 200) error = "HTTP " + status;
  } catch (err) {
    error = err.message;
  }

  return {
    status,
    ok: status === 200 && error === "",
    ms: performance.now() - start,
    error
  };
}

async function main() {
  console.log("Checking API:", url);

  const check = await request();
  if (!check.ok) throw new Error("API check failed: " + check.error);

  console.log("Warming up...");
  for (let i = 0; i < 5; i++) {
    const result = await request();
    if (!result.ok) throw new Error("Warm-up failed: " + result.error);
  }

  fs.mkdirSync(folder, { recursive: true });
  const summaries = [];
  const rows = [];

  for (const concurrency of levels) {
    console.log(
      "Running concurrency =", concurrency, "for 30 seconds..."
    );

    let next = 0;
    const results = [];
    const runStart = performance.now();
    const deadline = runStart + durationMs;

    async function worker() {
      while (performance.now() < deadline) {
        const id = ++next;
        const result = await request();
        results.push(result);
        rows.push({ concurrency, request: id, ...result });
      }
    }

    await Promise.all(
      Array.from({ length: concurrency }, () => worker())
    );

    const elapsed = (performance.now() - runStart) / 1000;
    const total = results.length;
    const times = results.map(r => r.ms).sort((a, b) => a - b);
    const passed = results.filter(r => r.ok).length;

    const summary = {
      concurrency,
      requests: total,
      passed,
      failed: total - passed,
      duration_s: Number(elapsed.toFixed(3)),
      throughput_rps: Number((total / elapsed).toFixed(2)),
      avg_ms: Number(
        (times.reduce((a, b) => a + b, 0) / total).toFixed(2)
      ),
      p95_ms: Number(
        times[Math.ceil(total * 0.95) - 1].toFixed(2)
      ),
      max_ms: Number(times[total - 1].toFixed(2))
    };

    summaries.push(summary);
    console.log("Completed:", JSON.stringify(summary));
  }

  fs.writeFileSync(
    path.join(folder, "results.json"),
    JSON.stringify({
      startedAt,
      finishedAt: new Date().toISOString(),
      node: process.version,
      platform: process.platform,
      cpu: "Intel Core i5-7300U, 2 cores, 4 logical processors",
      ramGiBReported: 7.9,
      url,
      warmupRequests: 5,
      targetDurationPerLevel_s: durationMs / 1000,
      summaries,
      requests: rows
    }, null, 2)
  );

  const csv = rows.map(r => [
    r.concurrency,
    r.request,
    r.status,
    r.ok,
    r.ms.toFixed(3),
    JSON.stringify(r.error)
  ].join(","));

  fs.writeFileSync(
    path.join(folder, "requests.csv"),
    "concurrency,request,status,ok,ms,error\n" +
    csv.join("\n") + "\n"
  );

  console.table(summaries);
  console.log("Results saved to:", folder);
}

main().catch(err => {
  console.error("TEST STOPPED:", err.message);
  process.exitCode = 1;
});