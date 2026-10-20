const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/browser-tools/node_modules/playwright');
(async () => {
    const base = process.env.FACTORYOPS_TEST_BASE_URL;
    assert.ok(base);
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ locale: 'zh-TW', viewport: { width: 1440, height: 1000 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        const sku = `SUPPLIER-${Date.now()}`;
        await page.goto(base + '/Products/Create');
        await page.locator('#Sku').fill(sku); await page.locator('#Name').fill('Multiple supplier product'); await page.locator('#UnitOfMeasure').fill('each');
        await Promise.all([page.waitForURL(base + '/Products'), page.getByRole('button', { name: 'Create Product', exact: true }).click()]);
        const productId = await page.locator('tr[data-product-id]').filter({ hasText: sku }).getAttribute('data-product-id');
        await page.goto(`${base}/Inventory/Configure/${productId}`); await page.locator('#CountedQuantity').fill('6');
        await Promise.all([page.waitForURL(`${base}/Inventory/Details/${productId}`), page.getByRole('button', { name: 'Save Stock Settings', exact: true }).click()]);
        await page.goto(`${base}/Inventory/Schedule?productId=${productId}`);
        await page.locator('#manageProductSuppliers').click();
        for (const suffix of ['Alpha', 'Beta']) {
            await page.getByRole('link', { name: 'Add New Supplier for Product', exact: true }).click();
            await page.locator('#Name').fill(sku + ' ' + suffix);
            await page.locator('#Email').fill(suffix.toLowerCase() + '@example.com');
            if (suffix === 'Beta') await page.locator('#MakeDefault').uncheck();
            await Promise.all([page.waitForURL(`${base}/Suppliers/Product/${productId}`), page.getByRole('button', { name: 'Create Supplier', exact: true }).click()]);
        }
        assert.equal(await page.locator('tr[data-product-supplier]').count(), 2);
        const betaRow = page.locator('tr[data-product-supplier]').filter({ hasText: sku + ' Beta' });
        const beta = await betaRow.getAttribute('data-product-supplier');
        await Promise.all([page.waitForURL(`${base}/Suppliers/Product/${productId}`), betaRow.getByRole('button', { name: 'Make Default', exact: true }).click()]);
        assert.equal(await betaRow.getAttribute('data-default'), 'true');
        await betaRow.getByRole('link', { name: 'Edit Supplier', exact: true }).click();
        await page.locator('#Name').fill(sku + ' Beta Updated');
        await Promise.all([page.waitForURL(`${base}/Suppliers/Product/${productId}`), page.getByRole('button', { name: 'Save Supplier', exact: true }).click()]);
        await page.getByRole('link', { name: 'Schedule Replenishment', exact: true }).click();
        assert.equal(await page.locator('#SupplierId').inputValue(), beta);
        assert.equal(await page.locator('#manualSupplierField').isVisible(), false);
        assert.match(await page.locator('#SupplierId option:checked').innerText(), /Beta Updated \(Default\)/);
        const projection = await (await page.request.get(`${base}/Dashboard/Projection?productId=${productId}`)).json();
        const date = days => new Date(Date.parse(projection.startDate + 'T12:00:00Z') + days * 86400000).toISOString().slice(0, 10);
        await page.locator('#Quantity').fill('5'); await page.locator('#ExpectedDate').fill(date(3));
        await Promise.all([page.waitForURL(base + '/Inventory/Replenishments'), page.getByRole('button', { name: 'Schedule Replenishment', exact: true }).click()]);
        assert.match(await page.locator('tr[data-replenishment-id]').filter({ hasText: sku }).innerText(), /Beta Updated/);
        await page.goto(base + '/WorkOrders/New');
        await page.locator('#customerSearch').fill('Inventory Test Customer');
        await page.locator('#searchResults button').filter({ hasText: 'Inventory Test Customer' }).click();
        await page.locator('#DeliveryType').selectOption('Company');
        await page.locator('select[data-field="ProductId"]').selectOption(productId);
        await page.locator('input[data-field="Quantity"]').fill('8'); await page.locator('#DueDate').fill(date(10));
        await Promise.all([page.waitForURL(base + '/WorkOrders'), page.locator('#saveOrderButton').click()]);
        await page.goto(`${base}/Dashboard?productId=${productId}`);
        await page.locator('#stockProductButton').click();
        const risk = page.locator(`#stock-product-option-${productId}`);
        await risk.waitFor({ state: 'visible' });
        assert.match(await risk.innerText(), /Depends on incoming stock/);
        assert.equal(await risk.evaluate(el => getComputedStyle(el).color), 'rgb(176, 42, 55)');
        await page.screenshot({ path: 'artifacts/dashboard-risk-dropdown.png', fullPage: true });
        await risk.click();
        assert.equal(await page.locator('#stockProductButton').getAttribute('aria-expanded'), 'false');
        await page.locator('#stockProductButton').press('ArrowDown');
        await page.locator('#stockProductButton').press('Escape');
        assert.equal(await page.locator('#stockProductButton').getAttribute('aria-expanded'), 'false');
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#stockProductButton').click();
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
        await page.screenshot({ path: 'artifacts/dashboard-risk-dropdown-mobile.png', fullPage: true });
        for (const route of ['/Suppliers', `/Suppliers/Product/${productId}`, `/Inventory/Schedule?productId=${productId}`]) {
            await page.goto(base + route);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, route);
            assert.doesNotMatch(await page.locator('main').innerText(), /[\u3400-\u9fff]/);
        }
        await page.goto(base + '/Inventory/Schedule');
        await page.locator('#ProductId').selectOption(productId);
        await page.waitForFunction(id => document.getElementById('SupplierId').value === id, beta);
        await page.locator('#SupplierId').selectOption('');
        assert.equal(await page.locator('#manualSupplierField').isVisible(), true);
        assert.deepEqual(errors, []);
        console.log('PASS: multiple suppliers, editable defaults, replenishment auto-selection, red risk dropdown, keyboard and English mobile UI.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
