import { copyFile, readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const siteDirectory = resolve(import.meta.dirname, "../artifacts/wwwroot");
const indexPath = resolve(siteDirectory, "index.html");
const index = await readFile(indexPath, "utf8");
const baseHref = '<base href="/" />';
const basePathHref = '<base href="/SparseTypes/" />';
const rootBaseHrefCount = index.split(baseHref).length - 1;
const basePathHrefCount = index.split(basePathHref).length - 1;

if (rootBaseHrefCount === 1 && basePathHrefCount === 0) {
  await writeFile(indexPath, index.replace(baseHref, basePathHref));
} else if (rootBaseHrefCount !== 0 || basePathHrefCount !== 1) {
  throw new Error(`Expected one root base href in ${indexPath}.`);
}

await copyFile(indexPath, resolve(siteDirectory, "404.html"));
await writeFile(resolve(siteDirectory, ".nojekyll"), "");
