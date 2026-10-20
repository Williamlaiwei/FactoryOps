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
            if (quantity !== null) {
                await page.goto(`${base}/Inventory/Configure/${id}`); await page.locator('#CountedQuantity').fill(String(quantity));
                await Promise.all([page.waitForURL(`${base}/Inventory/Details/${id}`), page.getByRole('button', { name: 'Save Stock Settings', exact: true }).click()]);
            }
            return id;
        }
        const skuA = `CHART-A-${suffix}`, skuB = `CHART-B-${suffix}`;
        const a = await product(skuA, null), b = await product(skuB, null);
        await page.goto(`${base}/Inventory/BulkCount?q=${suffix}&untrackedOnly=true`);
        assert.equal(await page.locator('tbody tr').count(), 2);
        assert.match(await page.locator(`tr[data-product-id="${a}"]`).innerText(), /Unknown/);
        await page.getByLabel(`Physical count for ${skuA}`, { exact: true }).fill('3');
        await page.getByLabel(`Physical count for ${skuB}`, { exact: true }).fill('20');
        assert.equal(await page.locator('.stock-selection:checked').count(), 2);
        await page.locator('#selectAllStock').uncheck();
        assert.equal(await page.locator('.stock-selection:checked').count(), 0);
        await page.locator('#selectAllStock').check();
        await page.screenshot({ path: 'artifacts/bulk-stock-count.png', fullPage: true });
        await Promise.all([page.waitForURL(base + '/Inventory'), page.getByRole('button', { name: 'Save Selected Products', exact: true }).click()]);
        assert.match(await page.locator('[role="status"]').innerText(), /Saved 2 product/);

        const csvSkuA = `04412-${suffix}`, csvSkuB = `CSV-${suffix}`;
        const csvA = await product(csvSkuA, null), csvB = await product(csvSkuB, null);
        await page.goto(base + '/Inventory/ImportCsv');
        assert.match(await page.locator('.file-picker-button').innerText(), /Choose File/);
        assert.match(await page.locator('.file-picker-name').innerText(), /No file chosen/);
        const example = await page.getByRole('link', { name: 'Download Stock CSV Example' }).getAttribute('href');
        const downloaded = await (await page.request.get(base + example)).body();
        assert.ok(downloaded.subarray(0, 3).equals(Buffer.from([0xef, 0xbb, 0xbf])));
        assert.equal(downloaded.subarray(3).toString().replace(/\r\n/g, '\n').trim(), (await page.locator('details pre').innerText()).replace(/\r\n/g, '\n').trim());
        await page.locator('#File').setInputFiles({ name: 'stock.csv', mimeType: 'text/csv', buffer: Buffer.from(
            `Sku,UnitOfMeasure,CountedQuantity,ReorderPoint,LeadTimeDays,Reason\r\n${csvSkuA},each,12,3,7,Opening count\r\n${csvSkuB},each,0,2,14,Opening count\r\n`) });
        await page.getByRole('button', { name: 'Preview Stock Import', exact: true }).click();
        await page.getByRole('heading', { name: 'Review Stock Import' }).waitFor();
        assert.equal(await page.locator('tbody tr').count(), 2);
        assert.equal((await (await page.request.get(`${base}/Dashboard/Projection?productId=${csvA}`)).json()).onHand, null);
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
        assert.doesNotMatch(await page.locator('main').innerText(), /[\u3400-\u9fff]/);
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.screenshot({ path: 'artifacts/stock-csv-preview.png', fullPage: true });
        await Promise.all([page.waitForURL(base + '/Inventory'), page.getByRole('button', { name: 'Confirm Stock Import', exact: true }).click()]);
        assert.match(await page.locator('[role="status"]').innerText(), /Imported stock counts and settings for 2 product/);
        assert.equal((await (await page.request.get(`${base}/Dashboard/Projection?productId=${csvA}`)).json()).onHand, 12);
        assert.equal((await (await page.request.get(`${base}/Dashboard/Projection?productId=${csvB}`)).json()).onHand, 0);
        await page.goto(base + '/Inventory/ImportCsv');
        const [stockDownload] = await Promise.all([
            page.waitForEvent('download'), page.getByRole('link', { name: 'Download Current Stock CSV', exact: true }).click()
        ]);
        assert.match(stockDownload.suggestedFilename(), /^stock-fti-demo-.*\.csv$/);
        const stockStream = await stockDownload.createReadStream();
        const stockChunks = []; for await (const chunk of stockStream) stockChunks.push(chunk);
        const stockBytes = Buffer.concat(stockChunks);
        assert.ok(stockBytes.subarray(0, 3).equals(Buffer.from([0xef, 0xbb, 0xbf])));
        assert.match(stockBytes.toString(), /StockSnapshot/);
        // Edit one row, retain every other row and snapshot, then upload the entire file.
        const updated = stockBytes.toString().split('\r\n').map(line => line.startsWith(csvSkuA + ',')
            ? line.replace(`${csvSkuA},each,12,3,7,,`, `${csvSkuA},each,11,3,7,Browser stocktake,`) : line).join('\r\n');
        await page.locator('#File').setInputFiles({ name: 'edited-stock.csv', mimeType: 'text/csv', buffer: Buffer.from(updated) });
        await page.getByRole('button', { name: 'Preview Stock Import', exact: true }).click();
        await page.getByRole('heading', { name: 'Review Stock Import' }).waitFor();
        assert.match(await page.locator(`tr[data-product-id="${csvA}"]`).innerText(), /Browser stocktake/);
        await Promise.all([page.waitForURL(base + '/Inventory'), page.getByRole('button', { name: 'Confirm Stock Import', exact: true }).click()]);
        assert.equal((await (await page.request.get(`${base}/Dashboard/Projection?productId=${csvA}`)).json()).onHand, 11);
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
        const warningSku = `WARN-${suffix}`, warningProduct = await product(warningSku, 0);
        await orderForm(warningProduct, 2, date(10));
        await page.locator('#saveOrderButton').click();
        await page.locator('#stockWarningDialog').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#stockWarningDialog').evaluate(el => el.matches(':modal')), true);
        assert.match(await page.locator('#stockWarningDialog').innerText(), /would be short 2/);
        await page.getByRole('button', { name: 'Go Back', exact: true }).click();
        assert.equal((await (await page.request.get(`${base}/Dashboard/Projection?productId=${warningProduct}`)).json()).outboundHeld, 0);
        await page.locator('input[data-field="Quantity"]').fill('3');
        await page.locator('#saveOrderButton').click();
        await page.locator('#stockWarningDialog').waitFor({ state: 'visible' });
        assert.match(await page.locator('#stockWarningDialog').innerText(), /would be short 3/);
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
        await page.screenshot({ path: 'artifacts/order-stock-warning.png', fullPage: true });
        await Promise.all([page.waitForURL(base + '/WorkOrders'), page.getByRole('button', { name: 'Continue Anyway', exact: true }).click()]);
        await page.setViewportSize({ width: 1440, height: 1000 });
        const warningProjection = await (await page.request.get(`${base}/Dashboard/Projection?productId=${warningProduct}`)).json();
        assert.equal(warningProjection.onHand, 0); assert.equal(warningProjection.outboundHeld, 3);
        assert.equal(warningProjection.days[10].available, -3);
        const warningOrder = page.locator('tr.order-row').filter({ hasText: warningSku });
        assert.match(await warningOrder.innerText(), /short 3/);
        await Promise.all([page.waitForURL(base + '/WorkOrders'), warningOrder.getByRole('button', { name: 'Ship Order', exact: true }).click()]);
        assert.match(await page.locator('[role="status"]').innerText(), /Not enough actual stock/);

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
        await page.locator('#stockProductSelect').selectOption(b, { force: true });
        await page.waitForFunction(() => document.getElementById('projectionOnHand').textContent.startsWith('20 each'));
        assert.equal(await page.locator('#projectionRows tr').nth(10).locator('td').nth(3).textContent(), '20');
        await page.locator('#stockProductSelect').selectOption(a, { force: true });
        await page.waitForFunction(() => document.getElementById('projectionOnHand').textContent.startsWith('3 each'));
        await page.route('**/Dashboard/Projection?**', route => route.abort());
        await page.locator('#stockProductSelect').selectOption(b, { force: true });
        await page.locator('#projectionError').waitFor({ state: 'visible' });
        assert.equal(await page.locator('polyline[data-series]').count(), 0);
        await page.unroute('**/Dashboard/Projection?**');
        await page.locator('#stockProductSelect').selectOption(a, { force: true });
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
        for (const route of [`/Dashboard?productId=${a}`, '/Inventory', `/Inventory/BulkCount?q=${suffix}`, '/Inventory/ImportCsv', '/Inventory/Calendar', '/Inventory/Replenishments', `/Inventory/Details/${a}`, `/Inventory/Configure/${a}`, '/Inventory/Schedule']) {
            const response = await page.goto(base + route); assert.equal(response.status(), 200);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, route + ' overflow');
            assert.doesNotMatch(await page.locator('main').innerText(), /[\u3400-\u9fff]/);
        }
        await page.goto(`${base}/Dashboard?productId=${a}&months=3`);
        await page.screenshot({ path: 'artifacts/stock-holds-dashboard-mobile.png', fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS: Edge warning dialog decline/accept, shortage holds and dispatch guard; CSV roundtrip, charts and English mobile pages.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
