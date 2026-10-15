import { readFile, stat } from "node:fs/promises";
import { resolve } from "node:path";

// Readiness probe for the E2E static site. Runs after publish and prepare but
// before Playwright starts the server, so a missing or unprepared output
// fails this stage with a specific error instead of an opaque webServer exit.
const siteDirectory = resolve(
  process.env.PLAYGROUND_SITE_DIR ?? resolve(import.meta.dirname, "../artifacts/wwwroot"),
);
const basePathHref = '<base href="/SparseTypes/" />';
const readinessUrl = `http://127.0.0.1:${process.env.PORT ?? 4173}/SparseTypes/`;

const failures = [];

async function requireFile(name) {
  try {
    if (!(await stat(resolve(siteDirectory, name))).isFile()) {
      throw new Error("not a file");
    }
  } catch {
    failures.push(`${name} is missing in ${siteDirectory}`);
  }
}

await requireFile("index.html");
await requireFile("404.html");
await requireFile(".nojekyll");

let index = "";
try {
  index = await readFile(resolve(siteDirectory, "index.html"), "utf8");
} catch {
  // Already reported by requireFile above.
}

if (index && !index.includes(basePathHref)) {
  failures.push(`index.html does not contain ${basePathHref}; run 'npm run prepare'`);
}

if (failures.length > 0) {
  for (const failure of failures) {
    console.error(`[verify-site] ${failure}`);
  }
  console.error("[verify-site] site verification failed; publish and prepare must succeed first.");
  process.exit(1);
}

console.log(`[verify-site] site ready in ${siteDirectory}; expecting readiness at ${readinessUrl}`);
