// Optional real-browser regression, run by: python tests/order_editing.py --browser
const assert = require("node:assert/strict");
const path = require("node:path");
const { chromium } = require("../artifacts/browser-tools/node_modules/playwright");

(async () => {
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        const base = process.env.FACTORYOPS_TEST_BASE_URL;
        assert.ok(base, "Use the Python runner to supply an isolated test environment");
        const future = days => new Date(Date.now() + days * 86400000).toISOString().slice(0, 10);
        await page.goto(base + "/WorkOrders/New");
        await page.locator("#customerSearch").fill("Phase One Alpha");
        await page.locator("#searchResults button").filter({ hasText: "Phase One Alpha" }).click();
        await page.locator('#ConstructionSiteId option[value="201"]').waitFor({ state: "attached" });
        await page.locator("#ConstructionSiteId").selectOption("201");
        const rows = page.locator("#orderItemsContainer .order-item");
        async function selectProduct(row, name) {
            const option = row.locator("option").filter({ hasText: `— ${name} (` });
            await row.locator("select").selectOption(await option.getAttribute("value"));
        }
        for (const [index, product] of ["Fast Tread", "BlueDeck", "SureFit"].entries()) {
            if (index) await page.locator("#addItemButton").click();
            await selectProduct(rows.nth(index), product);
            await rows.nth(index).locator('input[data-field="Quantity"]').fill(String((index + 1) * 10));
        }
        await page.locator("#DueDate").fill(future(10));
        await Promise.all([
            page.waitForURL(base + "/WorkOrders"),
            page.locator("#saveOrderButton").click()
        ]);
        const created = await page.locator('[role="status"]').first().innerText();
        const orderId = created.match(/Order WO-(\d+) created/)[1];
        const editPath = `/WorkOrders/Edit/${orderId}`;
        await page.locator(`a[href="${editPath}"]`).click();
        assert.equal(await rows.count(), 3);
        await rows.nth(0).locator('input[data-field="Quantity"]').fill("75");
        await page.locator("#addItemButton").click();
        await selectProduct(rows.nth(3), "BlueDeck");
        await rows.nth(3).locator('input[data-field="Quantity"]').fill("45");
        await rows.nth(1).locator(".remove-item-btn").click();
        assert.equal(await rows.count(), 3);
        assert.deepEqual(await rows.locator('input[data-field="Quantity"]').evaluateAll(
            inputs => inputs.map(input => input.name)),
            ["Items[0].Quantity", "Items[1].Quantity", "Items[2].Quantity"]);
        await page.locator("#DueDate").fill(future(20));
        await page.locator("#DeliveryInstructions").fill("Browser check: use north gate");
        await page.locator("#DeliveryType").selectOption("Company");
        assert.equal(await page.locator("#ConstructionSiteId").isDisabled(), true);
        // A second immediate submit is suppressed while the first request saves.
        await Promise.all([
            page.waitForURL(base + "/WorkOrders"),
            page.evaluate(() => {
                const form = document.getElementById("orderForm");
                form.requestSubmit();
                form.requestSubmit();
            })
        ]);
        assert.match(await page.locator('[role="status"]').first().innerText(), new RegExp(`Order WO-${orderId} updated`));
        const orderRow = page.locator(".order-row").filter({ has: page.locator(`a[href="${editPath}"]`) });
        assert.match(await orderRow.innerText(), /Fast Tread × 75/);
        await orderRow.locator('button[data-bs-toggle="collapse"]').click();
        await page.locator(`#orderDetails-${orderId}.show`).waitFor();
        assert.match(await page.locator(`#orderDetails-${orderId}`).innerText(), /Browser check: use north gate/);
        await page.goto(base + editPath);
        assert.deepEqual(await rows.locator('input[data-field="Quantity"]').evaluateAll(
            inputs => inputs.map(input => input.value)), ["75", "30", "45"]);
        assert.equal(await page.locator("#DeliveryType").inputValue(), "Company");
        assert.equal(await page.locator("#DueDate").inputValue(), future(20));
        await page.screenshot({ path: path.join(__dirname, "../artifacts/order-editing.png"), fullPage: true });
        // Removing every row must still allow a new row to be added and submitted.
        while (await rows.count()) await rows.first().locator(".remove-item-btn").click();
        await page.locator("#saveOrderButton").click();
        assert.match(await page.locator("#orderMessage").innerText(), /Add at least one valid order item/);
        await page.locator("#addItemButton").click();
        await selectProduct(rows.first(), "SureFit");
        await rows.first().locator('input[data-field="Quantity"]').fill("9");
        await Promise.all([page.waitForURL(base + "/WorkOrders"), page.locator("#saveOrderButton").click()]);
        await page.goto(base + editPath);
        assert.equal(await rows.count(), 1);
        assert.equal(await rows.first().locator('input[data-field="Quantity"]').inputValue(), "9");
        assert.deepEqual(errors, []);
        console.log("PASS: Edge create/edit/reload, row indexing, remove-all recovery and duplicate-submit checks.");
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
