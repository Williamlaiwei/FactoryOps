const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require("../artifacts/browser-tools/node_modules/playwright");

(async () => {
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    try {
        const base = process.env.FACTORYOPS_TEST_BASE_URL;
        assert.ok(base, "Run through tests/order_editing.py --knowledge --browser");
        const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        async function selectCompany(name) {
            await page.goto(base + "/Companies");
            const card = page.locator(".card").filter({ has: page.locator("h5", { hasText: name }) });
            await card.getByRole("button", { name: "Use This Company", exact: true }).click();
            await page.waitForLoadState("networkidle");
        }
        await selectCompany("Empty Company");
        await page.goto(base + "/Products");
        assert.match(await page.locator("main").innerText(), /0 product\(s\)/);
        await page.goto(base + "/Customers");
        assert.equal(await page.locator(".customer-card").count(), 0);
        await page.goto(base + "/WorkOrders");
        assert.match(await page.locator("main").innerText(), /No orders yet/);
        await page.goto(base + "/Knowledge");
        assert.equal(await page.locator(".knowledge-document").count(), 0);
        assert.equal(await page.locator('[role="alert"]').count(), 0);
        await selectCompany("FTI Group Demo");
        await page.goto(base + "/Knowledge");
        const raw = fs.readFileSync(path.join(__dirname, "../demo/fti-group/knowledge.csv"));
        const file = { name: "fti-demo-reference.csv", mimeType: "text/csv", buffer: raw };
        await page.locator("#knowledgeFile").setInputFiles(file);
        await page.locator("#sourceUrl").fill("https://ftigroup.com.au/products/");
        await page.getByRole("button", { name: "Upload and Index", exact: true }).click();
        const doc = page.locator(".knowledge-document");
        await doc.waitFor();
        assert.equal(await doc.count(), 1);
        const documentId = await doc.getAttribute("data-document-id");
        await page.locator("#knowledgeFile").setInputFiles(file);
        await page.getByRole("button", { name: "Upload and Index", exact: true }).click();
        await page.getByText("This document is already indexed for this company.", { exact: true }).waitFor();
        assert.equal(await doc.count(), 1);
        await page.locator("#knowledgeQuery").fill("BlueDeck 0.75");
        await page.getByRole("button", { name: "Search Knowledge", exact: true }).click();
        await page.locator(".knowledge-candidate").waitFor();
        assert.match(await page.locator(".knowledge-candidate").innerText(), /DEMO-BD-C075/);
        assert.match(await page.locator(".knowledge-candidate").innerText(), /confirm actual supplier details/);
        assert.ok(await page.locator(".knowledge-citation").count() > 0);
        assert.match(await page.locator(".knowledge-citation").first().innerText(), /Row [0-9]+/);
        await page.screenshot({ path: path.join(__dirname, "../artifacts/ai-knowledge.png"), fullPage: true });
        const original = await page.request.get(base + `/Knowledge/Original/${documentId}`);
        assert.equal(original.status(), 200);
        assert.deepEqual(await original.body(), raw);
        const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
        const result = await page.request.post(base + "/Knowledge/Retrieve", { form: {
            query: "BD075", __RequestVerificationToken: token, __CompanyKey: "fti-demo"
        }});
        assert.equal(result.status(), 200);
        const json = await result.json();
        assert.equal(json.company_key, "fti-demo");
        assert.equal(json.requires_review, true);
        assert.equal(json.candidates[0].sku, "DEMO-BD-C075");
        await selectCompany("Empty Company");
        await page.goto(base + "/Knowledge?q=BlueDeck%200.75");
        assert.equal(await page.locator(".knowledge-document").count(), 0);
        assert.equal(await page.locator(".knowledge-candidate").count(), 0);
        assert.equal(await page.locator(".knowledge-citation").count(), 0);
        assert.equal((await page.request.get(base + `/Knowledge/Original/${documentId}`)).status(), 404);
        await selectCompany("FTI Group Demo");
        await page.goto(base + "/Knowledge");
        await doc.getByRole("button", { name: "Re-index", exact: true }).click();
        await page.locator(`.knowledge-document[data-version="2"]`).waitFor();
        await doc.getByRole("button", { name: "Replace Document", exact: true }).click();
        await doc.locator('input[type="file"]').setInputFiles({
            name: "updated-reference.csv", mimeType: "text/csv", buffer: Buffer.from("Sku,Name\nDEMO-BD-PT330,BlueDeck PT330 decking\n")
        });
        await doc.getByRole("button", { name: "Replace and Re-index", exact: true }).click();
        await page.locator(`.knowledge-document[data-version="3"]`).waitFor();
        assert.match(await doc.innerText(), /updated-reference.csv/);
        await page.locator("#knowledgeQuery").fill("PT330");
        await page.getByRole("button", { name: "Search Knowledge", exact: true }).click();
        await page.locator(".knowledge-citation").waitFor();
        assert.match(await page.locator(".knowledge-citation").innerText(), /PT330/);
        page.once("dialog", dialog => dialog.accept());
        await doc.getByRole("button", { name: "Delete", exact: true }).click();
        await page.getByText("Document, stored original and index entries deleted.", { exact: true }).waitFor();
        assert.equal(await doc.count(), 0);
        assert.deepEqual(errors, []);
        console.log("PASS: Edge company switching, upload/dedup, citations, SQL candidates, cross-company isolation, reindex, replace and delete.");
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
