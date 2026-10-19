const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/browser-tools/node_modules/playwright');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const base = process.env.FACTORYOPS_TEST_BASE_URL;
        assert.ok(base, 'Run through the isolated Python runner');
        const page = await browser.newPage({ locale: 'zh-TW', viewport: { width: 1440, height: 1000 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        const suffix = Date.now();
        async function product(sku, quantity) {
            await page.goto(base + '/Products/Create');
            await page.locator('#Sku').fill(sku); await page.locator('#Name').fill(sku); await page.locator('#UnitOfMeasure').fill('each');
            await Promise.all([page.waitForURL(base + '/Products'), page.getByRole('button', { name: 'Create Product', exact: true }).click()]);
            const id = await page.locator('tr[data-product-id]').filter({ hasText: sku }).getAttribute('data-product-id');
            await page.goto(`${base}/Inventory/Configure/${id}`); await page.locator('#CountedQuantity').fill(String(quantity));
            await Promise.all([page.waitForURL(`${base}/Inventory/Details/${id}`), page.getByRole('button', { name: 'Save Stock Settings', exact: true }).click()]);
            return id;
        }
        const skuA = `CHART-A-${suffix}`, skuB = `CHART-B-${suffix}`;
        const a = await product(skuA, 3), b = await product(skuB, 20);
        const initial = await (await page.request.get(`${base}/Dashboard/Projection?productId=${a}`)).json();
        const date = days => new Date(Date.parse(initial.startDate + 'T12:00:00Z') + days * 86400000).toISOString().slice(0, 10);
        async function orderForm(id, quantity, due) {
            await page.goto(base + '/WorkOrders/New');
            await page.locator('#customerSearch').fill('Inventory Test Customer');
            await page.locator('#searchResults button').filter({ hasText: 'Inventory Test Customer' }).click();
            await page.locator('#DeliveryType').selectOption('Company');
            await page.locator('select[data-field="ProductId"]').selectOption(id);
            await page.locator('input[data-field="Quantity"]').fill(String(quantity)); await page.locator('#DueDate').fill(due);
        }
        await orderForm(a, 8, date(10));
        await page.locator('#saveOrderButton').click();
        await page.getByText('would be short 5', { exact: false }).waitFor();
        assert.equal(await page.locator('input[data-field="Quantity"]').inputValue(), '8');

        await page.goto(`${base}/Inventory/Schedule?productId=${a}`);
        await page.locator('#Quantity').fill('10'); await page.locator('#ExpectedDate').fill(date(5));
        await Promise.all([page.waitForURL(base + '/Inventory/Replenishments'), page.getByRole('button', { name: 'Schedule Replenishment', exact: true }).click()]);
        await orderForm(a, 8, date(10));
        await Promise.all([page.waitForURL(base + '/WorkOrders'), page.locator('#saveOrderButton').click()]);
        const orderId = await page.locator('tr.order-row').filter({ hasText: skuA }).getAttribute('data-order-id');
        let order = page.locator(`tr[data-order-id="${orderId}"]`);
        assert.match(await order.innerText(), /Reserved/);

        await page.goto(`${base}/Dashboard?productId=${a}`);
        assert.match(await page.locator('#projectionOnHand').innerText(), /^3 each/);
        assert.match(await page.locator('#projectionIncoming').innerText(), /^10 each/);
        assert.match(await page.locator('#projectionOutgoing').innerText(), /^8 each/);
        for (const [series, color] of [['actual', '#1765d1'], ['withIncoming', '#16803c'], ['available', '#d32f2f']])
            assert.equal(await page.locator(`polyline[data-series="${series}"]`).getAttribute('stroke'), color);
        assert.equal(await page.locator('#projectionRows tr').nth(10).locator('td').nth(3).textContent(), '5');
        await page.locator('#stockProjectionChart').focus(); await page.keyboard.press('ArrowRight');
        assert.match(await page.locator('#projectionTooltip').innerText(), new RegExp(date(1)));
        await page.locator('#projectionMonths').selectOption('3');
        await page.waitForFunction(() => document.querySelectorAll('#projectionRows tr').length > 85);
        await page.locator('#stockProductSelect').selectOption(b);
        await page.waitForFunction(() => document.getElementById('projectionOnHand').textContent.startsWith('20 each'));
        assert.equal(await page.locator('#projectionRows tr').nth(10).locator('td').nth(3).textContent(), '20');
        await page.locator('#stockProductSelect').selectOption(a);
        await page.waitForFunction(() => document.getElementById('projectionOnHand').textContent.startsWith('3 each'));
        await page.route('**/Dashboard/Projection?**', route => route.abort());
        await page.locator('#stockProductSelect').selectOption(b);
        await page.locator('#projectionError').waitFor({ state: 'visible' });
        assert.equal(await page.locator('polyline[data-series]').count(), 0);
        await page.unroute('**/Dashboard/Projection?**');
        await page.locator('#stockProductSelect').selectOption(a);
        await page.waitForFunction(() => document.getElementById('projectionOnHand').textContent.startsWith('3 each'));
        await page.screenshot({ path: 'artifacts/stock-holds-dashboard.png', fullPage: true });

        await page.goto(base + '/WorkOrders'); order = page.locator(`tr[data-order-id="${orderId}"]`);
        await order.locator('select[name="status"]').selectOption('Completed');
        await Promise.all([page.waitForURL(base + '/WorkOrders'), order.getByRole('button', { name: 'Update', exact: true }).click()]);
        await Promise.all([page.waitForURL(base + '/WorkOrders'), order.getByRole('button', { name: 'Ship Order', exact: true }).click()]);
        assert.match(await page.locator('[role="status"]').innerText(), /Not enough actual stock/);
        await page.goto(base + '/Inventory/Replenishments');
        const receipt = page.locator('tr[data-replenishment-id]').filter({ hasText: skuA });
        await Promise.all([page.waitForURL(base + '/Inventory/Replenishments'), receipt.getByRole('button', { name: 'Receive', exact: true }).click()]);
        await page.goto(base + '/WorkOrders');
        await Promise.all([page.waitForURL(base + '/WorkOrders'), order.getByRole('button', { name: 'Ship Order', exact: true }).click()]);
        assert.match(await order.innerText(), /Shipped/);
        await page.goto(`${base}/Inventory/Details/${a}`);
        assert.equal(await page.locator('#onHand').innerText(), '5');
        await page.goto(`${base}/Dashboard?productId=${a}&months=3`);
        assert.match(await page.locator('#projectionOutgoing').innerText(), /^0 each/);

        await page.setViewportSize({ width: 390, height: 844 });
        for (const route of [`/Dashboard?productId=${a}`, '/Inventory', '/Inventory/Calendar', '/Inventory/Replenishments', `/Inventory/Details/${a}`, `/Inventory/Configure/${a}`, '/Inventory/Schedule']) {
            const response = await page.goto(base + route); assert.equal(response.status(), 200);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, route + ' overflow');
            assert.doesNotMatch(await page.locator('main').innerText(), /[\u3400-\u9fff]/);
        }
        await page.goto(`${base}/Dashboard?productId=${a}&months=3`);
        await page.screenshot({ path: 'artifacts/stock-holds-dashboard-mobile.png', fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS: Edge dated order rejection/hold, incoming hold, explicit receipt/dispatch, product switching, 1/3-month chart colors, keyboard inspection and seven English mobile pages.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
