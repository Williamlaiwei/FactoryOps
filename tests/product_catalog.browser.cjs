// Run in the isolated database: python tests/order_editing.py --catalog --browser
const assert = require("node:assert/strict");
const path = require("node:path");
const { chromium } = require("../artifacts/browser-tools/node_modules/playwright");

(async () => {
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    try {
        const base = process.env.FACTORYOPS_TEST_BASE_URL;
        assert.ok(base, "Run through the Python test runner");
        const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        const sku = `WEB-${Date.now()}`;
        const alias = `PanelAlias${Date.now()}`;
        await page.goto(base + "/Products/Create");
        await page.locator("#Sku").fill(sku);
        await page.locator("#Name").fill("Browser panel");
        await page.locator("#Category").fill("Building supplies");
        await page.locator("#UnitOfMeasure").fill("sheet");
        await page.locator("#Dimensions").fill("2400 x 1200 mm");
        await page.locator("#Specifications").fill("Coated steel");
        await page.locator("#Packaging").fill("Pack of 10");
        await page.locator("#Aliases").fill(alias);
        await Promise.all([page.waitForURL(base + "/Products"), page.getByRole("button", { name: "Create Product", exact: true }).click()]);
        const productRow = page.locator("tr[data-product-id]").filter({ hasText: sku });
        const productId = await productRow.getAttribute("data-product-id");
        assert.ok(productId);
        await page.locator("#Q").fill(alias);
        await Promise.all([page.waitForURL(url => url.searchParams.get("q") === alias), page.getByRole("button", { name: "Search", exact: true }).click()]);
        assert.equal(await page.locator("tr[data-product-id]").count(), 1);

        await page.goto(base + "/WorkOrders/New");
        await page.locator("#customerSearch").fill("Catalog Test Customer");
        await page.locator("#searchResults button").filter({ hasText: "Catalog Test Customer" }).click();
        await page.locator('#ConstructionSiteId option[value="601"]').waitFor({ state: "attached" });
        await page.locator("#ConstructionSiteId").selectOption("601");
        await page.locator("#productFilter").fill(alias);
        const visibleOptions = await page.locator('select[data-field="ProductId"] option[data-search]').evaluateAll(
            options => options.filter(option => !option.hidden).map(option => option.value));
        assert.deepEqual(visibleOptions, [productId]);
        await page.locator('select[data-field="ProductId"]').selectOption(productId);
        await page.locator('input[data-field="Quantity"]').fill("7");
        await page.locator("#DueDate").fill(new Date(Date.now() + 15 * 86400000).toISOString().slice(0, 10));
        await Promise.all([page.waitForURL(base + "/WorkOrders"), page.locator("#saveOrderButton").click()]);
        const message = await page.locator('[role="status"]').first().innerText();
        const orderId = message.match(/Order WO-(\d+) created/)[1];

        await page.goto(base + `/Products/Edit/${productId}`);
        await page.locator("#Name").fill("Renamed browser panel");
        await page.locator("#Sku").fill(sku + "-NEW");
        await page.locator("#UnitOfMeasure").fill("pack");
        await page.locator("#IsActive").uncheck();
        await Promise.all([page.waitForURL(base + "/Products"), page.getByRole("button", { name: "Save Product", exact: true }).click()]);
        await page.goto(base + `/WorkOrders/Edit/${orderId}`);
        assert.equal(await page.locator('select[data-field="ProductId"]').inputValue(), productId);
        assert.match(await page.locator('select[data-field="ProductId"] option:checked').innerText(), /inactive, existing line only/);
        assert.match(await page.locator("#orderItemsContainer").innerText(), /Recorded product: Browser panel/);
        await page.locator('input[data-field="Quantity"]').fill("8");
        await Promise.all([page.waitForURL(base + "/WorkOrders"), page.locator("#saveOrderButton").click()]);
        await page.locator(`button[data-bs-target="#orderDetails-${orderId}"]`).click();
        await page.locator(`#orderDetails-${orderId}.show`).waitFor();
        const detail = await page.locator(`#orderDetails-${orderId}`).innerText();
        assert.match(detail, /Browser panel/);
        assert.ok(detail.includes(`SKU: ${sku})`));
        assert.match(detail, /Quantity: 8 sheet/);

        const csvSku = sku + "-CSV";
        const file = { name: "products.csv", mimeType: "text/csv", buffer: Buffer.from(
            `Sku,Name,UnitOfMeasure,Aliases\n${csvSku},CSV browser product,box,CSV alias\n`, "utf8") };
        await page.goto(base + "/Products/Import");
        await page.locator("#File").setInputFiles(file);
        await Promise.all([page.waitForURL(base + "/Products"), page.getByRole("button", { name: "Import Products", exact: true }).click()]);
        const csvRow = page.locator("tr[data-product-id]").filter({ hasText: csvSku });
        const csvProductId = await csvRow.getAttribute("data-product-id");
        assert.ok(csvProductId);
        await page.screenshot({ path: path.join(__dirname, "../artifacts/product-catalog.png"), fullPage: true });
        await page.goto(base + "/Products/Import");
        await page.locator("#File").setInputFiles(file);
        await page.getByRole("button", { name: "Import Products", exact: true }).click();
        await page.getByText("Row 2: This SKU already exists.", { exact: false }).waitFor();
        await page.goto(base + `/Products/Delete/${csvProductId}`);
        await Promise.all([page.waitForURL(base + "/Products"), page.getByRole("button", { name: "Delete Product", exact: true }).click()]);
        assert.equal(await page.locator(`tr[data-product-id="${csvProductId}"]`).count(), 0);
        await page.goto(base + "/WorkOrders/New");
        assert.equal(await page.locator(`select[data-field="ProductId"] option[value="${productId}"]`).count(), 0);
        assert.deepEqual(errors, []);
        console.log("PASS: Edge catalog CRUD, alias filter, CSV import/errors and historical order snapshots.");
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
