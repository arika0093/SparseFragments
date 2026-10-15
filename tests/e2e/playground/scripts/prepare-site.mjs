import { copyFile, readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

// Rewrites the published Blazor index.html from the root base href to the
// "/SparseTypes/" base path used by GitHub Pages, then adds the static files
// Pages needs (404.html fallback, .nojekyll). PLAYGROUND_SITE_DIR overrides
// the default publish output so the E2E site and the Pages deployment share
// this exact preparation instead of duplicating it in shell.
const siteDirectory = resolve(
  process.env.PLAYGROUND_SITE_DIR ?? resolve(import.meta.dirname, "../artifacts/wwwroot"),
);
const indexPath = resolve(siteDirectory, "index.html");

let index;
try {
  index = await readFile(indexPath, "utf8");
} catch {
  console.error(
    `[prepare-site] cannot read ${indexPath}. The dotnet publish output is missing; run 'npm run build' first.`,
  );
  process.exit(1);
}

const baseHref = '<base href="/" />';
const basePathHref = '<base href="/SparseTypes/" />';
const rootBaseHrefCount = index.split(baseHref).length - 1;
const basePathHrefCount = index.split(basePathHref).length - 1;

if (rootBaseHrefCount === 1 && basePathHrefCount === 0) {
  await writeFile(indexPath, index.replace(baseHref, basePathHref));
  console.log(`[prepare-site] rewrote base href to /SparseTypes/ in ${indexPath}`);
} else if (rootBaseHrefCount === 0 && basePathHrefCount === 1) {
  console.log(`[prepare-site] base href is already /SparseTypes/ in ${indexPath}; nothing to rewrite`);
} else {
  console.error(
    `[prepare-site] expected exactly one ${baseHref} or one ${basePathHref} in ${indexPath}, ` +
      `found ${rootBaseHrefCount} root and ${basePathHrefCount} base-path occurrences. ` +
      `The published index.html layout changed; update prepare-site.mjs to match.`,
  );
  process.exit(1);
}

await copyFile(indexPath, resolve(siteDirectory, "404.html"));
await writeFile(resolve(siteDirectory, ".nojekyll"), "");
console.log(`[prepare-site] wrote 404.html and .nojekyll in ${siteDirectory}`);
