import { expect, test as base } from "@playwright/test";

const localOrigin = "http://127.0.0.1:4173";

const test = base.extend({
  page: async ({ page }, use) => {
    const pageErrors = [];
    const consoleErrors = [];
    const failedResponses = [];

    page.on("pageerror", error => pageErrors.push(error.message));
    page.on("console", message => {
      const location = message.location().url;
      if (
        message.type() === "error"
        && location.startsWith(localOrigin)
        && consoleErrors.length < 3
      ) {
        consoleErrors.push(message.text());
      }
    });
    page.on("response", response => {
      if (response.url().startsWith(localOrigin) && response.status() >= 400) {
        failedResponses.push(`${response.status()} ${response.url()}`);
      }
    });

    await use(page);

    expect(pageErrors, "Uncaught browser exceptions").toEqual([]);
    expect(consoleErrors, "First-party browser console errors").toEqual([]);
    expect(failedResponses, "Failed first-party requests").toEqual([]);
    await expect(page.locator("#blazor-error-ui")).toBeHidden();
  },
});

async function diffModelUris(page) {
  return page.evaluate(() => {
    const editorHolders = window.blazorMonaco?.editors ?? [];
    return [
      ...new Set(
        editorHolders
          .filter(
            ({ id }) =>
              id.startsWith("monaco-diff-")
              && !id.endsWith("_original")
              && !id.endsWith("_modified"),
          )
          .flatMap(({ editor }) => {
            const model = editor?.getModel();
            return [model?.original?.uri?.toString(), model?.modified?.uri?.toString()];
          })
          .filter(uri => uri !== undefined),
      ),
    ].sort();
  });
}

async function liveDiffModelCount(page, modelUris) {
  return page.evaluate(
    uris =>
      window.monaco
        ?.editor.getModels()
        .filter(model => uris.includes(model.uri.toString())).length ?? -1,
    modelUris,
  );
}

// Diff editors initialize asynchronously after each route change.
async function settledDiffModelUris(page) {
  const deadline = Date.now() + 30_000;
  let last = null;
  let stableSince = 0;
  let current = [];
  while (Date.now() < deadline) {
    current = await diffModelUris(page);
    if (
      current.length > 0
      && JSON.stringify(current) === JSON.stringify(last)
      && Date.now() - stableSince > 1000
    ) {
      return current;
    }
    if (JSON.stringify(current) !== JSON.stringify(last)) {
      last = current;
      stableSince = Date.now();
    }
    await page.waitForTimeout(200);
  }
  throw new Error(`diff editor models never settled, last models=${current.length}`);
}

async function openCase(page, route, heading) {
  if (route === "") {
    await page.locator(".pg-home-link").click();
  } else {
    await page.locator(`fluent-anchor-button[href="${route}"]`).click();
  }
  await expect(page.getByRole("heading", { name: heading })).toBeVisible();
  const expectedPath = route ? `/SparseTypes/${route}` : "/SparseTypes/";
  await expect(page).toHaveURL(new URL(expectedPath, localOrigin).toString());
  expect(
    await page.evaluate(() => document === window.__initialDocument),
    "Route navigation must preserve the initial document",
  ).toBe(true);
}

async function navigateInApp(page, route, heading) {
  await page.evaluate(route => window.Blazor.navigateTo(`/SparseTypes/${route}`), route);
  await expect(page.getByRole("heading", { name: heading })).toBeVisible();
  await expect(page).toHaveURL(new URL(`/SparseTypes/${route}`, localOrigin).toString());
  expect(
    await page.evaluate(() => document === window.__initialDocument),
    "Route navigation must preserve the initial document",
  ).toBe(true);
}

async function loadHome(page) {
  await page.goto("");
  await expect(page.getByRole("heading", { name: "SparseTypes" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Samples" })).toBeVisible();
  await page.evaluate(() => {
    window.__initialDocument = document;
  });
}

// Capture models attached to diff editors so unrelated code-editor models
// cannot affect the disposal assertion.
test("diff models return to baseline after leaving a case", async ({ page }) => {
  test.setTimeout(240_000);
  await loadHome(page);

  await openCase(page, "case-2", "Typed patch applied to the merged fragment");
  const ownedModelUris = await settledDiffModelUris(page);
  const caseCount = await liveDiffModelCount(page, ownedModelUris);
  expect(caseCount).toBe(ownedModelUris.length);
  expect(caseCount).toBeGreaterThan(0);

  await openCase(page, "", "Samples");
  expect(await liveDiffModelCount(page, ownedModelUris)).toBe(0);
});

test("repeated in-app route cycles release diff-owned models", async ({ page }) => {
  test.setTimeout(300_000);
  await loadHome(page);
  const baselineUris = await diffModelUris(page);
  const baseline = await liveDiffModelCount(page, baselineUris);
  expect(baseline).toBe(0);

  await openCase(page, "case-2", "Typed patch applied to the merged fragment");
  let currentModelUris = await settledDiffModelUris(page);
  expect(await liveDiffModelCount(page, currentModelUris)).toBe(currentModelUris.length);

  for (let cycle = 0; cycle < 20; cycle++) {
    await navigateInApp(page, "not-found", "Not Found");
    expect(await liveDiffModelCount(page, currentModelUris)).toBe(baseline);

    await navigateInApp(page, "case-2", "Typed patch applied to the merged fragment");
    currentModelUris = await settledDiffModelUris(page);
    expect(await liveDiffModelCount(page, currentModelUris)).toBe(currentModelUris.length);
  }

  expect(
    await page.evaluate(() => document === window.__initialDocument),
    "All route cycles must remain in the initial document",
  ).toBe(true);
});
