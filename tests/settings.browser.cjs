const assert = require("node:assert/strict");
const path = require("node:path");
const { chromium } = require("../artifacts/browser-tools/node_modules/playwright");

(async () => {
    const base = process.env.FACTORYOPS_TEST_BASE_URL;
    assert.ok(base, "Run through tests/order_editing.py --settings --browser");
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
        const page = await context.newPage();
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        async function selectCompany(name) {
            await page.goto(base + "/Companies");
            await page.locator(".card").filter({ has: page.locator("h5", { hasText: name }) })
                .getByRole("button", { name: "Use This Company", exact: true }).click();
            await page.waitForLoadState("networkidle");
        }
        await page.goto(base + "/WorkOrders");
        await page.getByRole("link", { name: "Settings", exact: true }).click();
        await page.getByRole("heading", { name: "AI & Workflow Settings", exact: true }).waitFor();
        assert.match(await page.locator("#openai-key-status").innerText(), /Configured \(not tested\)/);
        assert.ok(!(await page.content()).includes("test-key-not-a-real-openai-credential"));
        await page.getByLabel("Order extraction and reply model").fill("browser-order-model");
        await page.getByLabel("Phone transcription model").fill("browser-speech-model");
        await page.getByLabel("Knowledge embedding model").fill("browser-embedding-model");
        await page.getByLabel("Order intake policy").selectOption("CreateAiDraft");
        await page.getByLabel("Phone conversation records").uncheck();
        await page.getByLabel("Email orders and attachments").check();
        await page.getByLabel("Order inbox address").fill("orders@example.com");
        await page.getByLabel("Preferred reply channel").selectOption("Both");
        await page.getByLabel("Reply language").selectOption("zh-TW");
        await page.getByLabel("Reply sender address").fill("sales@example.com");
        await page.getByLabel("Missing-information reply instructions").fill("請確認缺少的交貨日期與數量。");
        await page.getByLabel("Reply signature").fill("FTI demo team");
        await page.getByRole("button", { name: "Save Settings", exact: true }).click();
        await page.getByRole("status").waitFor();
        await page.reload();
        assert.equal(await page.getByLabel("Order extraction and reply model").inputValue(), "browser-order-model");
        assert.equal(await page.getByLabel("Order intake policy").inputValue(), "CreateAiDraft");
        assert.equal(await page.getByLabel("Phone conversation records").isChecked(), false);
        assert.equal(await page.getByLabel("Missing-information reply instructions").inputValue(), "請確認缺少的交貨日期與數量。");

        const staleTab = await page.context().newPage();
        await staleTab.goto(base + "/Settings");
        await selectCompany("Empty Company");
        await page.goto(base + "/Settings");
        assert.equal(await page.getByLabel("Order extraction and reply model").inputValue(), "");
        assert.equal(await page.getByLabel("Order intake policy").inputValue(), "ReviewBeforeImport");
        await staleTab.getByLabel("Reply signature").fill("Wrong company write");
        await staleTab.getByRole("button", { name: "Save Settings", exact: true }).click();
        await staleTab.getByText(/active company changed in another tab/).waitFor();
        await page.reload();
        assert.equal(await page.getByLabel("Reply signature").inputValue(), "Blank company only");

        await selectCompany("FTI Group Demo");
        await page.goto(base + "/Settings");
        await staleTab.goto(base + "/Settings");
        await page.getByLabel("Reply signature").fill("Updated team signature");
        await page.getByRole("button", { name: "Save Settings", exact: true }).click();
        await page.getByRole("status").waitFor();
        await staleTab.getByLabel("Reply signature").fill("Stale overwrite");
        await staleTab.getByRole("button", { name: "Save Settings", exact: true }).click();
        await staleTab.getByText(/Settings changed in another tab/).waitFor();
        await page.reload();
        assert.equal(await page.getByLabel("Reply signature").inputValue(), "Updated team signature");
        await page.screenshot({ path: path.join(__dirname, "../artifacts/ai-settings.png"), fullPage: true });
        await page.setViewportSize({ width: 390, height: 844 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth));
        assert.deepEqual(errors, []);
        console.log("Verified: settings save/reload, source toggles, company isolation, stale tabs and mobile layout.");
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exit(1); });
