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
      if (message.type() === "error" && location.startsWith(localOrigin)) {
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

async function modelCount(page) {
  return page.evaluate(() => window.monaco?.editor.getModels().length ?? -1);
}

// Monaco editors and their models initialize asynchronously after each
// navigation, so wait until the count stops changing before recording it.
async function settledModelCount(page) {
  const deadline = Date.now() + 30_000;
  let last = null;
  let stableSince = 0;
  let current = -1;
  while (Date.now() < deadline) {
    current = await modelCount(page);
    if (current >= 0 && current === last && Date.now() - stableSince > 1000) {
      return current;
    }
    if (current !== last) {
      last = current;
      stableSince = Date.now();
    }
    await page.waitForTimeout(200);
  }
  throw new Error(`monaco models never settled, last count=${current}`);
}

async function openCase(page, route, heading) {
  await page.goto(route);
  await expect(page.getByRole("heading", { name: heading })).toBeVisible();
}

// Each mounted MonacoDiffView owns exactly its two models (covered precisely
// by the headless disposal tests); here the browser proves the page-level
// consequence: leaving a case returns the registry to its baseline.
test("diff models return to baseline after leaving a case", async ({ page }) => {
  test.setTimeout(240_000);
  await openCase(page, "", "SparseTypes");
  const homeBaseline = await settledModelCount(page);

  await openCase(page, "case-2", "Typed patch applied to the merged fragment");
  const caseCount = await settledModelCount(page);
  expect(caseCount).toBeGreaterThan(homeBaseline);

  await openCase(page, "", "SparseTypes");
  expect(await settledModelCount(page)).toBe(homeBaseline);
});

test("repeated case visits keep the model count stable", async ({ page }) => {
  test.setTimeout(300_000);
  const routes = [
    ["case-2", "Typed patch applied to the merged fragment"],
    ["case-3", "Keyed list: List<Quest>"],
    ["case-4", "EditSession with Undo/Redo"],
  ];

  await openCase(page, "", "SparseTypes");
  const baseline = await settledModelCount(page);

  for (let cycle = 0; cycle < 20; cycle++) {
    const [route, heading] = routes[cycle % routes.length];
    await openCase(page, route, heading);
    await settledModelCount(page);
    await openCase(page, "", "SparseTypes");
    expect(await settledModelCount(page)).toBe(baseline);
  }
});
