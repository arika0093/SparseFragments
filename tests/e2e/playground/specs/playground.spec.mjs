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

test("case 5 gates and orders the real-time edit simulation", async ({ page }) => {
  await openCase(page, "case-5", "EF Core: ChangeSet to tracked entity");
  const options = page.locator(".pg-case5-rebase-options");
  const checkboxes = options.locator("fluent-checkbox");
  const simulateAnotherUser = checkboxes.nth(1);
  const simulateRealTime = checkboxes.nth(2);
  const optionText = await options.innerText();
  const simulateIndex = optionText.indexOf("Simulate another user editing before save");
  const conflictIndex = optionText.indexOf("Conflict handling");
  const realTimeIndex = optionText.indexOf("Simulate real-time edits every 10 seconds");

  expect(simulateIndex).toBeGreaterThanOrEqual(0);
  expect(conflictIndex).toBeGreaterThan(simulateIndex);
  expect(realTimeIndex).toBeGreaterThan(conflictIndex);
  await expect(simulateRealTime).toHaveAttribute("disabled", "");
  await simulateAnotherUser.click();
  await expect(simulateRealTime).not.toHaveAttribute("disabled");
  await simulateAnotherUser.click();
  await expect(simulateRealTime).toHaveAttribute("disabled", "");

  const githubIcon = page.locator(".pg-gh svg");
  await expect.poll(() => githubIcon.evaluate(icon =>
    getComputedStyle(icon).fill === getComputedStyle(icon).color,
  )).toBe(true);
});

test("case 5 keeps the saved entity diff fixed during later real-time edits", async ({ page }) => {
  test.setTimeout(45_000);
  await openCase(page, "case-5", "EF Core: ChangeSet to tracked entity");
  await page.getByText("Simulate another user editing before save", { exact: true }).click();
  await page.getByText("Simulate real-time edits every 10 seconds", { exact: true }).click();

  const notices = page.locator(".pg-case5-update-notice");
  const initialNotice = notices.first();
  await expect(initialNotice).toBeVisible({ timeout: 20_000 });
  const remoteNoticeColor = await initialNotice.evaluate(notice => getComputedStyle(notice).color);
  const total = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "Total" }).locator("input").first();
  await enterText(total, "9876.54");
  await fluentButton(page, "Save").first().click();
  const saveDialog = page.locator(".pg-submit-preview-dialog");
  await fluentButton(saveDialog, "Save").click();
  await expect(page.getByRole("table", { name: "Modified EF properties" }).getByText("9876.54")).toBeVisible();
  const customer = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "Customer" }).locator("input").first();
  const serverCustomer = await page.evaluate(() => {
    const entityModel = window.monaco.editor.getModels()
      .map(model => model.getValue())
      .find(value => value.includes('"CustomerName":') && value.includes('"Revision":'));
    return entityModel ? JSON.parse(entityModel).CustomerName : null;
  });
  expect(serverCustomer).toBeTruthy();
  await expect(customer).toHaveValue(serverCustomer);
  const serverNotice = page.locator(".pg-case5-server-update-notice").first();
  await expect(serverNotice).toHaveText("Updated from server value");
  await expect.poll(() => serverNotice.evaluate(notice => getComputedStyle(notice).color)).not.toBe(remoteNoticeColor);

  const savedEntity = await page.evaluate(() =>
    window.monaco.editor.getModels()
      .map(model => model.getValue())
      .filter(value => value.includes('"Total": 9876.54') && value.includes('"Revision":'))
      .at(-1),
  );
  expect(savedEntity).toBeTruthy();

  await expect(serverNotice).toBeHidden({ timeout: 7_000 });
  const followingPeriodicNotice = page.locator(".pg-case5-update-notice:not(.pg-case5-server-update-notice)");
  await expect(followingPeriodicNotice.first()).toBeVisible({ timeout: 15_000 });
  await expect.poll(() => page.evaluate(saved =>
    window.monaco.editor.getModels().some(model => model.getValue() === saved),
  savedEntity)).toBe(true);
});

test("case 5 refreshes conflict choices when the dialog revision becomes stale", async ({ page }) => {
  test.setTimeout(45_000);
  await openCase(page, "case-5", "EF Core: ChangeSet to tracked entity");
  await page.getByText("Simulate another user editing before save", { exact: true }).click();
  await page.getByText("Simulate real-time edits every 10 seconds", { exact: true }).click();

  const notices = page.locator(".pg-case5-update-notice");
  await expect(notices.first()).toBeVisible({ timeout: 20_000 });
  const customer = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "Customer" }).locator("input").first();
  await enterText(customer, "e2e-local-customer");
  await fluentButton(page, "Save").first().click();
  const saveDialog = page.locator(".pg-submit-preview-dialog");
  await fluentButton(saveDialog, "Save").click();

  const conflictDialog = page.locator(".pg-conflict-dialog");
  await expect(conflictDialog.locator("dialog[open]")).toBeVisible();
  await expect(conflictDialog.locator(".pg-conflict-table thead th")).toHaveCount(5);
  await expect(conflictDialog).toHaveAttribute("size", "full");
  const openDialog = conflictDialog.locator("dialog[open]");
  await expect.poll(
    () => openDialog.evaluate(dialog => dialog.getBoundingClientRect().width - window.innerWidth),
  ).toBeGreaterThan(-64);
  const conflictRow = conflictDialog.locator(".pg-conflict-table tbody tr").first();
  const capturedChoice = fluentButton(conflictRow.locator("td").nth(2), "Use captured value");
  const currentChoice = fluentButton(conflictRow.locator("td").nth(3), "Use current value");
  await expect(capturedChoice).toHaveCount(1);
  await expect(currentChoice).toHaveCount(1);
  await capturedChoice.click();
  await expect(capturedChoice).toHaveAttribute("aria-pressed", "true");

  const initialNotices = (await notices.allTextContents()).join("|");
  await expect.poll(
    async () => (await notices.allTextContents()).join("|"),
    { timeout: 20_000 },
  ).not.toBe(initialNotices);
  await fluentButton(conflictDialog, "Apply resolutions").click();
  await expect(conflictDialog.locator("dialog[open]")).toBeVisible();
  await expect(fluentButton(conflictDialog, "Use captured value").first()).toHaveAttribute("aria-pressed", "false");
  await expect(conflictDialog.getByText(/selected conflict resolution was rejected/)).toHaveCount(0);

  await fluentButton(conflictDialog, "Use captured value").first().click();
  await fluentButton(conflictDialog, "Apply resolutions").click();
  await expect(conflictDialog.locator("dialog[open]")).toBeHidden();
  await expect(page.getByRole("table", { name: "Modified EF properties" }).getByText("e2e-local-customer")).toBeVisible();
});

test("case 5 keeps a focused added row across key assignment", async ({ page }) => {
  await openCase(page, "case-5", "EF Core: ChangeSet to tracked entity");
  const rows = page.locator(".pg-task-row");
  const initialCount = await rows.count();

  await page.locator('fluent-button[title="Add a random line"]').click();
  await expect(rows).toHaveCount(initialCount + 1);
  const addedRow = rows.last();
  const product = addedRow.locator(".pg-roster-field").nth(1).locator("input").first();
  await enterText(product, "e2e-pending-product");
  await page.getByText("Wait 10 seconds before applying save", { exact: true }).click();

  await fluentButton(page, "Save").first().click();
  const saveDialog = page.locator(".pg-submit-preview-dialog");
  await fluentButton(saveDialog, "Save").click();
  await expect(page.getByRole("status").getByText("Save is pending")).toBeVisible();

  await product.focus();
  await product.press("End");
  await product.pressSequentially("-after-submit");
  await expect(product).toHaveValue("e2e-pending-product-after-submit");
  await expect(product).toBeFocused();
  await expect(addedRow.locator(".pg-roster-field").first().locator("input").first()).toHaveValue("#new");

  await expect
    .poll(
      () => addedRow.locator(".pg-roster-field").first().locator("input").first().inputValue(),
      { timeout: 20_000 },
    )
    .not.toBe("#new");
  await expect(product).toHaveValue("e2e-pending-product-after-submit");
  await expect(product).toBeFocused();
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

test("case 6 keeps a post-save edit as a new pending draft", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  const itemCode = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).locator("input").first();
  const reviewDialog = page.locator(".pg-catalog-review-dialog");

  await enterText(itemCode, "e2e-item-code");
  await fluentButton(page.locator("#case-6-overview-panel"), "Save section draft").click();
  await expect(page.getByText("Section draft saved to the catalog session.")).toBeVisible();
  await fluentButton(page, "Save").first().click();
  await expect(reviewDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(reviewDialog, "Save").click();
  await expect(page.getByText("No committed changes", { exact: true })).toBeVisible();
  await expect(itemCode).toHaveValue("e2e-item-code");

  // Repeating the flow with a new value proves the acknowledged baseline
  // moved on: the second save persists the new draft, never the first. The
  // first save leaves the Work masters tab active, so navigate back first.
  await page.getByText("1. Overview", { exact: true }).click();
  await expect(itemCode).toBeVisible();
  await enterText(itemCode, "e2e-item-code-v2");
  await fluentButton(page.locator("#case-6-overview-panel"), "Save section draft").click();
  await expect(page.getByText("Section draft saved to the catalog session.")).toBeVisible();
  await fluentButton(page, "Save").first().click();
  await expect(reviewDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(reviewDialog, "Save").click();
  await expect(page.getByText("No committed changes", { exact: true })).toBeVisible();
  await expect(itemCode).toHaveValue("e2e-item-code-v2");
});

test("case 6 clears section draft markers but keeps catalog changes pending", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  const draftToggle = page.locator(".pg-catalog-draft-mode fluent-checkbox");
  const itemCode = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).locator("input").first();
  const itemCodeRow = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).first();
  const overviewPanel = page.locator("#case-6-overview-panel");

  await enterText(itemCode, "e2e-marked-code");
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" }).first()).toBeVisible();
  await expect(itemCodeRow).toHaveClass(/pg-changed/);
  await expect(overviewPanel.locator(".pg-card-head .pg-has-changes-badge")).toHaveText("Edited");
  await expect(draftToggle).toHaveAttribute("disabled", "");

  await fluentButton(overviewPanel, "Save section draft").click();
  await expect(page.getByText("Section draft saved to the catalog session.")).toBeVisible();
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(0);
  await expect(itemCodeRow).not.toHaveClass(/pg-changed/);
  await expect(overviewPanel.locator(".pg-card-head .pg-has-changes-badge")).toHaveText("Current");
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");
  await expect(draftToggle).toHaveAttribute("disabled", "");

  await fluentButton(page, "Save").first().click();
  const reviewDialog = page.locator(".pg-catalog-review-dialog");
  await expect(reviewDialog.locator("dialog[open]")).toBeVisible();
  await expect(reviewDialog).toContainText("e2e-marked-code");
  await fluentButton(reviewDialog, "Save").click();
  await expect(page.getByText("No committed changes", { exact: true })).toBeVisible();
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(0);
  await expect(draftToggle).not.toHaveAttribute("disabled", "");
});

test("case 6 highlights work-master fields and badges in pass-through mode", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  const draftToggle = page.locator(".pg-catalog-draft-mode fluent-checkbox");
  await draftToggle.click();

  await page.getByText("5. Work masters", { exact: true }).click();
  const masterPanel = page.locator("#case-6-master-cut-panel");
  const equipmentRow = masterPanel.locator(".pg-edit-fields .pg-row").filter({ hasText: "Equipment" });
  await enterText(equipmentRow.locator("input").first(), "e2e-equipment");

  await expect(equipmentRow).toHaveClass(/pg-changed/);
  await expect(masterPanel.locator(".pg-card-head .pg-has-changes-badge")).toHaveText("Edited");
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(2);
  await expect(draftToggle).toHaveAttribute("disabled", "");
  await expect(page.locator(".pg-catalog-draft-mode .pg-hint").last()).toContainText(
    "catalog and all section drafts have no pending changes",
  );
});

test("case 6 only marks added workflow rows until an existing row moves", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("4. Workflow", { exact: true }).click();
  const rows = page.locator("#case6-workflow .pg-roster-row");
  const initialCount = await rows.count();
  const template = page.locator(".pg-catalog-template").filter({ hasText: "Visual inspection" });

  await template.locator('fluent-button[title="Add step"]').click();
  await expect(rows).toHaveCount(initialCount + 1);
  const addedRow = rows.filter({ hasText: "Visual inspection" });
  await expect(addedRow).toHaveClass(/pg-added/);
  for (let index = 0; index < initialCount; index++) {
    await addedRow.locator('fluent-button[title="Move up"]').click();
  }
  await expect(rows.first()).toContainText("Visual inspection");
  for (let index = 0; index < initialCount; index++) {
    await expect(rows.nth(index)).not.toHaveClass(/pg-moved/);
  }

  await rows.filter({ hasText: "Cutting" }).locator('fluent-button[title="Move down"]').click();
  await expect(rows.filter({ hasText: "Cutting" })).toHaveClass(/pg-moved/);
  await expect(rows.filter({ hasText: "Assembly" })).toHaveClass(/pg-moved/);
  await expect(addedRow).toHaveClass(/pg-added/);
});

test("case 6 synchronizes work-master tabs after removing a workflow step", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("Enable section drafts", { exact: true }).click();
  await page.getByText("4. Workflow", { exact: true }).click();
  const rows = page.locator("#case6-workflow .pg-roster-row");
  const assembly = rows.filter({ hasText: "Assembly" });

  await assembly.locator('fluent-button[title="Remove step"]').click();
  await expect(rows).toHaveCount(2);
  await expect(rows.filter({ hasText: "Cutting" })).not.toHaveClass(/pg-moved/);
  await expect(rows.filter({ hasText: "Final inspection" })).not.toHaveClass(/pg-moved/);

  await page.getByText("5. Work masters", { exact: true }).click();
  await expect(page.getByRole("heading", { name: "Cutting master" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Assembly" })).toHaveCount(0);
  await expect(page.getByRole("tab", { name: "Final inspection" })).toBeVisible();

  await fluentButton(page, "Undo").first().click();
  await page.getByText("4. Workflow", { exact: true }).click();
  await expect(rows.filter({ hasText: "Assembly" })).toHaveCount(1);
  await page.getByText("5. Work masters", { exact: true }).click();
  await expect(page.getByRole("tab", { name: "Assembly" })).toBeVisible();

  await fluentButton(page, "Redo").first().click();
  await page.getByText("4. Workflow", { exact: true }).click();
  await expect(rows.filter({ hasText: "Assembly" })).toHaveCount(0);
  await page.getByText("5. Work masters", { exact: true }).click();
  await expect(page.getByRole("tab", { name: "Assembly" })).toHaveCount(0);
});

test("case 6 keeps work masters unchanged while workflow edits are a draft", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("4. Workflow", { exact: true }).click();
  const rows = page.locator("#case6-workflow .pg-roster-row");

  await rows.filter({ hasText: "Assembly" }).locator('fluent-button[title="Remove step"]').click();
  await expect(rows.filter({ hasText: "Assembly" })).toHaveCount(0);
  await page.locator(".pg-catalog-template")
    .filter({ hasText: "Visual inspection" })
    .locator('fluent-button[title="Add step"]')
    .click();
  await expect(rows.filter({ hasText: "Visual inspection" })).toHaveCount(1);

  await page.getByText("5. Work masters", { exact: true }).click();
  await expect(page.getByRole("tab", { name: "Assembly" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Visual inspection" })).toHaveCount(0);

  await page.getByText("4. Workflow", { exact: true }).click();
  await fluentButton(page.locator("#case-6-workflow-panel"), "Discard section draft").click();
  await expect(rows.filter({ hasText: "Assembly" })).toHaveCount(1);
  await expect(rows.filter({ hasText: "Visual inspection" })).toHaveCount(0);
  await page.getByText("5. Work masters", { exact: true }).click();
  await expect(page.getByRole("tab", { name: "Assembly" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Visual inspection" })).toHaveCount(0);
});

test("case 6 saves and resets a work-master draft", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("5. Work masters", { exact: true }).click();
  const masterPanel = page.locator("#case-6-master-cut-panel");
  const workCenterRow = masterPanel.locator(".pg-edit-fields .pg-row").filter({ hasText: "WorkCenter" });
  const workCenter = workCenterRow.locator("input").first();

  await enterText(workCenter, "e2e-work-center");
  await expect(workCenterRow).toHaveClass(/pg-changed/);
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" }).first()).toBeVisible();
  await fluentButton(masterPanel, "Save section draft").click();
  await expect(workCenterRow).not.toHaveClass(/pg-changed/);
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(0);
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");

  await fluentButton(page, "Reset").first().click();
  await expect(workCenter).toHaveValue("WC-12");
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("No committed changes");
});

test("case 6 passes through a workflow insertion and creates its work master", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("Enable section drafts", { exact: true }).click();
  const itemCode = page
    .locator("#case-6-overview-panel .pg-edit-fields .pg-row")
    .filter({ hasText: "ItemCode" })
    .locator("input")
    .first();
  await enterText(itemCode, "e2e-reset-before-workflow");
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");
  await fluentButton(page, "Reset").first().click();
  await expect(itemCode).toHaveValue("EQ-2048");
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("No committed changes");

  await page.getByText("4. Workflow", { exact: true }).click();
  const template = page.locator(".pg-catalog-template").filter({ hasText: "Visual inspection" });

  const addStartedAt = Date.now();
  await template.locator('fluent-button[title="Add step"]').click();
  const addedRow = page.locator("#case6-workflow .pg-roster-row").filter({ hasText: "Visual inspection" });
  await expect(addedRow).toHaveCount(1);
  expect(Date.now() - addStartedAt).toBeLessThan(5_000);
  await expect(addedRow).toHaveClass(/pg-added/);
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");

  await page.getByText("5. Work masters", { exact: true }).click();
  await page.getByRole("tab", { name: "Visual inspection" }).click();
  await expect(page.getByRole("heading", { name: "Visual inspection master" })).toBeVisible();
  const stepId = await page
    .getByRole("heading", { name: "Visual inspection master" })
    .locator("xpath=following-sibling::p/code")
    .textContent();
  const masterPanel = page.locator(`#case-6-master-${stepId}-panel`);
  await expect(
    masterPanel.locator(".pg-edit-fields .pg-row").filter({ hasText: "WorkCenter" }).locator("input").first(),
  ).toHaveValue("QC-04");
  await expect(
    masterPanel.locator(".pg-edit-fields .pg-row").filter({ hasText: "Equipment" }).locator("input").first(),
  ).toHaveValue("Inspection Station");
  await expect(page.locator("#blazor-error-ui")).toBeHidden();

  await fluentButton(page, "Save").first().click();
  const reviewDialog = page.locator(".pg-catalog-review-dialog");
  await expect(reviewDialog.locator("dialog[open]")).toBeVisible();
  await fluentButton(reviewDialog, "Save").click();
  await expect(page.getByText("No committed changes", { exact: true })).toBeVisible();
  await page.getByText("4. Workflow", { exact: true }).click();
  await expect(addedRow).not.toHaveClass(/pg-added/);
});

test("case 6 catalog undo and redo refresh pass-through markers", async ({ page }) => {
  await openCase(page, "case-6", "Enterprise master editing with child sessions");
  await page.getByText("Enable section drafts", { exact: true }).click();
  const itemCode = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).locator("input").first();
  const itemCodeRow = page.locator(".pg-edit-fields .pg-row").filter({ hasText: "ItemCode" }).first();

  await enterText(itemCode, "e2e-undo-code");
  await expect(itemCodeRow).toHaveClass(/pg-changed/);
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(1);
  await expect(fluentButton(page, "Undo").first()).toBeVisible();
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");
  await fluentButton(page, "Undo").first().click();
  await expect(itemCode).toHaveValue("EQ-2048");
  await expect(itemCodeRow).not.toHaveClass(/pg-changed/);
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("No committed changes");
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(0);
  await fluentButton(page, "Redo").first().click();
  await expect(itemCode).toHaveValue("e2e-undo-code");
  await expect(itemCodeRow).toHaveClass(/pg-changed/);
  await expect(page.locator(".pg-catalog-header .pg-has-changes-badge")).toHaveText("Changes pending");
  await expect(page.locator(".pg-catalog-tab-badge").filter({ hasText: "Edited" })).toHaveCount(1);
});
