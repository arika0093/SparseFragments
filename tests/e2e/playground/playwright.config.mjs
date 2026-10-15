import { defineConfig } from "@playwright/test";

const baseURL = "http://127.0.0.1:4173/SparseTypes/";

// CI publishes, prepares, and verifies the site in earlier workflow steps and
// sets PLAYGROUND_PREBUILT=1, so the webServer only serves that prebuilt
// output and a startup failure points at serving itself. Local runs keep the
// one-command `npm run serve` chain that builds on demand.
const webServerCommand = process.env.PLAYGROUND_PREBUILT === "1" ? "npm run serve:site" : "npm run serve";

export default defineConfig({
  testDir: "./specs",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 2,
  reporter: [["list"], ["html", { open: "never" }]],
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  use: {
    baseURL,
    browserName: "chromium",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  webServer: {
    command: webServerCommand,
    cwd: import.meta.dirname,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
});
