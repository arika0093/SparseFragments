import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, resolve, sep } from "node:path";

const siteDirectory = resolve(import.meta.dirname, "../artifacts/wwwroot");
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

server.listen(port, "127.0.0.1", () => {
  console.log(`Serving the Playground at http://127.0.0.1:${port}${basePath}/`);
});
