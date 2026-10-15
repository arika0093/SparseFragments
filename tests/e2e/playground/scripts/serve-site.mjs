import { createReadStream } from "node:fs";
import { readFile, stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, resolve, sep } from "node:path";

// Serves a previously published and prepared site directory under
// /SparseTypes/, mirroring the GitHub Pages layout. This script never builds:
// CI publishes, prepares, and verifies in earlier workflow steps, so a
// startup failure here means serving itself failed rather than an earlier
// stage hiding behind a Playwright webServer exit code.
const siteDirectory = resolve(
  process.env.PLAYGROUND_SITE_DIR ?? resolve(import.meta.dirname, "../artifacts/wwwroot"),
);
const basePath = "/SparseTypes";
const port = Number(process.env.PORT ?? 4173);
const contentTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".ico", "image/x-icon"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".png", "image/png"],
  [".svg", "image/svg+xml"],
  [".wasm", "application/wasm"],
  [".woff", "font/woff"],
  [".woff2", "font/woff2"],
]);

// Preflight the publish output so a missing or unprepared site fails here
// with an actionable message instead of serving 404s to every browser route.
try {
  if (!(await stat(siteDirectory)).isDirectory()) {
    throw new Error("not a directory");
  }
} catch {
  console.error(
    `[serve-site] site directory ${siteDirectory} is missing. Run 'npm run build && npm run prepare-site' first.`,
  );
  process.exit(1);
}

let indexHtml;
try {
  indexHtml = await readFile(resolve(siteDirectory, "index.html"), "utf8");
} catch {
  console.error(
    `[serve-site] index.html is missing in ${siteDirectory}. Run 'npm run build && npm run prepare-site' first.`,
  );
  process.exit(1);
}

if (!indexHtml.includes('<base href="/SparseTypes/" />')) {
  console.error(
    `[serve-site] index.html in ${siteDirectory} has no <base href="/SparseTypes/" />. Run 'npm run prepare-site' to rewrite the base path.`,
  );
  process.exit(1);
}

const server = createServer(async (request, response) => {
  let pathname;
  try {
    pathname = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname);
  } catch {
    response.writeHead(400).end("Bad request");
    return;
  }

  if (pathname === basePath) {
    response.writeHead(308, { Location: `${basePath}/` }).end();
    return;
  }

  if (!pathname.startsWith(`${basePath}/`)) {
    response.writeHead(404).end("Not found");
    return;
  }

  const relativePath = pathname.slice(basePath.length + 1);
  const candidate = resolve(siteDirectory, relativePath);
  if (candidate !== siteDirectory && !candidate.startsWith(`${siteDirectory}${sep}`)) {
    response.writeHead(400).end("Bad request");
    return;
  }

  let filePath = candidate;
  try {
    if (!(await stat(filePath)).isFile()) {
      throw new Error("Not a file");
    }
  } catch {
    filePath = resolve(siteDirectory, "404.html");
  }

  response.writeHead(200, {
    "Cache-Control": "no-store",
    "Content-Type": contentTypes.get(extname(filePath)) ?? "application/octet-stream",
  });
  createReadStream(filePath).pipe(response);
});

server.on("error", error => {
  if (error?.code === "EADDRINUSE") {
    console.error(
      `[serve-site] port ${port} is already in use. Stop the other server or set PORT to a free port.`,
    );
  } else {
    console.error(`[serve-site] failed to start: ${error?.message ?? error}`);
  }
  process.exit(1);
});

server.listen(port, "127.0.0.1", () => {
  console.log(`[serve-site] serving ${siteDirectory} at http://127.0.0.1:${port}${basePath}/`);
});
