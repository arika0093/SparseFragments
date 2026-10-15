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

async function monacoModelText(page) {
  return page.evaluate(
    () => window.monaco?.editor.getModels().map(model => model.getValue()).join("\n") ?? "",
  );
}

function fluentButton(scope, text) {
  const escapedText = text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return scope.locator("fluent-button").filter({ hasText: new RegExp(`^\\s*${escapedText}\\s*$`) });
}

async function enterText(input, value) {
  await input.click();
  await input.press("ControlOrMeta+A");
  await input.pressSequentially(value);
  await input.press("Tab");
}

async function openCase(page, route, heading) {
  await page.goto(route);
  await expect(page.getByRole("heading", { name: heading })).toBeVisible();
}

test("landing page and every case load by their direct URL", async ({ page }) => {
  test.setTimeout(150_000);
  const routes = [
    { path: "", title: "SparseTypes Playground", heading: "SparseTypes" },
    { path: "case-1", title: "01 Layers merge — SparseTypes Playground", heading: "Layers: merge Lower + Higher" },
    { path: "case-2", title: "02 Typed patch — SparseTypes Playground", heading: "Typed patch applied to the merged fragment" },
    { path: "case-3", title: "03 Keyed list — SparseTypes Playground", heading: "Keyed list: List<Quest>" },
    { path: "case-4", title: "04 Undo / Redo — SparseTypes Playground", heading: "EditSession with Undo/Redo" },
    { path: "case-5", title: "05 EF Core mapping — SparseTypes Playground", heading: "EF Core: ChangeSet to tracked entity" },
    { path: "case-6", title: "06 Enterprise master editing — SparseTypes Playground", heading: "Enterprise master editing with child sessions" },
  ];

  for (const route of routes) {
    await page.goto(route.path);
    await expect(page).toHaveTitle(route.title);
    await expect(page.getByRole("heading", { name: route.heading })).toBeVisible();
  }
});

test("case 1 edits a layer, updates the merged fragment, and resets it", async ({ page }) => {
  await openCase(page, "case-1", "Layers: merge Lower + Higher");
  const label = page.locator('input[placeholder="label text"]').first();

  await enterText(label, "e2e-lower-label");
  await expect.poll(() => monacoModelText(page)).toContain("e2e-lower-label");
  await fluentButton(page, "Reset").first().click();
  await expect(label).toHaveValue("default");
});

test("case 2 edits and resets a patch, then imports its ChangePayload", async ({ page }) => {
  await openCase(page, "case-2", "Typed patch applied to the merged fragment");
  const label = page.locator('input[placeholder="label text"]');

  await enterText(label, "e2e-patch-label");
  await expect.poll(() => monacoModelText(page)).toContain("e2e-patch-label");
  await fluentButton(page, "Reset").click();
  await expect(label).toHaveValue("patched!");

  await fluentButton(page, "Import ChangePayload…").click();
  const importDialog = page.locator(".pg-import-dialog");
  await expect(importDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(importDialog, "Apply to merged baseline").click();
  await expect.poll(() => monacoModelText(page)).toContain("Applied model:");
  await fluentButton(importDialog, "Close").click();
});

test("case 3 adds and reorders quests, then restores the baseline", async ({ page }) => {
  await openCase(page, "case-3", "Keyed list: List<Quest>");
  const rows = page.locator(".pg-grid-trio .pg-roster-list").nth(1).locator(".pg-roster-row");
  const afterCard = page.locator(".pg-grid-trio .pg-fluent-card").nth(1);
  const initialCount = await rows.count();
  const firstTitle = rows.nth(0).locator('input[placeholder="title"]');
  const initialFirstTitle = await firstTitle.inputValue();

  await fluentButton(afterCard, "Add quest").click();
  await expect(rows).toHaveCount(initialCount + 1);
  await rows.nth(0).getByTitle("Move down").click();
  await expect.poll(() => firstTitle.inputValue()).not.toBe(initialFirstTitle);

  await fluentButton(page, "Reset").nth(1).click();
  await expect(rows).toHaveCount(initialCount);
  await expect(firstTitle).toHaveValue(initialFirstTitle);
});

test("case 4 supports undo, redo, submit, and reset", async ({ page }) => {
  await openCase(page, "case-4", "EditSession with Undo/Redo");
  const label = page.locator('.pg-edit-fields input[placeholder="label"]');

  await enterText(label, "e2e-submitted-label");
  await page.locator('fluent-button[title="Undo"]').click();
  await expect(label).toHaveValue("Draft");
  await page.locator('fluent-button[title="Redo"]').click();
  await expect(label).toHaveValue("e2e-submitted-label");
  await fluentButton(page, "Submit").first().click();

  const submitDialog = page.locator(".pg-submit-preview-dialog");
  await expect(submitDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(submitDialog, "Submit").click();
  await expect(page.getByRole("heading", { name: "Changes submitted" })).toBeVisible();
  await fluentButton(page, "Close").click();

  await enterText(label, "e2e-reset-label");
  await fluentButton(page, "Reset").first().click();
  await expect(page.getByRole("heading", { name: "Changes reset" })).toBeVisible();
  await expect(label).toHaveValue("e2e-submitted-label");
});

test("case 5 saves an order edit and resets a later draft", async ({ page }) => {
  await openCase(page, "case-5", "EF Core: ChangeSet to tracked entity");
  const customer = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "Customer" }).locator("input").first();

  await enterText(customer, "e2e-customer");
  await fluentButton(page, "Save").first().click();
  const saveDialog = page.locator(".pg-submit-preview-dialog");
  await expect(saveDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(saveDialog, "Save").click();
  await expect(page.getByRole("table", { name: "Modified EF properties" }).getByText("e2e-customer")).toBeVisible();

  const notes = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "Notes" }).locator("input").first();
  await enterText(notes, "e2e-reset-notes");
  await fluentButton(page, "Reset draft").click();
  await expect(notes).toHaveValue("first order");
});

test("case 6 accepts a section draft and saves the catalog", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  const itemCode = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).locator("input").first();

  await enterText(itemCode, "e2e-item-code");
  await fluentButton(page.locator("#case-6-overview-panel"), "Save section draft").click();
  await expect(page.getByText("Section draft saved to the catalog session.")).toBeVisible();
  await fluentButton(page, "Save").first().click();

  const reviewDialog = page.locator(".pg-catalog-review-dialog");
  await expect(reviewDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(reviewDialog, "Save").click();
  await expect(page.getByText("No committed changes", { exact: true })).toBeVisible();
  await expect(itemCode).toHaveValue("e2e-item-code");
});
