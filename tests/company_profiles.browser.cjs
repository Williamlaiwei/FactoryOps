const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('../artifacts/browser-tools/node_modules/playwright');

(async () => {
    const base = process.env.FACTORYOPS_TEST_BASE_URL;
    assert.ok(base, 'Run python tests/order_editing.py --companies --browser');
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const context = await browser.newContext({ locale: 'en-AU', viewport: { width: 1440, height: 1000 } });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(base + '/Companies');
        const originalCount = await page.locator('.company-profile').count();
        const staleTab = await context.newPage();
        await staleTab.goto(base + '/Products/Create');
        await page.getByRole('link', { name: '+ Create Company', exact: true }).click();
        await page.getByLabel('Company name', { exact: true }).fill('Browser Manufacturing Ltd');
        await page.getByLabel('Website (optional)', { exact: true }).fill('https://example.com');
        await page.getByRole('button', { name: 'Create Company', exact: true }).click();
        await page.getByRole('status').waitFor();
        assert.equal(await page.locator('.company-profile').count(), originalCount + 1);
        const card = page.locator('.company-profile').filter({ hasText: 'Browser Manufacturing Ltd' });
        assert.match(await card.innerText(), /Active company/);
        assert.equal(await page.locator('.navbar-brand').innerText(), 'Browser Manufacturing Ltd');
        const newKey = await card.getAttribute('data-company-key');
        assert.match(newKey, /^company-[a-f0-9]{32}$/);

        await page.reload();
        assert.equal(await page.locator('.company-profile').count(), originalCount + 1);
        await page.goto(base + '/Products');
        assert.match(await page.locator('main').innerText(), /0 product\(s\)/);
        await page.goto(base + '/Customers');
        assert.equal(await page.locator('.customer-item').count(), 0);
        await page.goto(base + '/WorkOrders');
        assert.match(await page.locator('main').innerText(), /No orders yet/);
        await page.goto(base + '/Settings');
        assert.equal(await page.locator('input[name="__CompanyKey"]').inputValue(), newKey);
        assert.equal(await page.getByLabel('Order intake policy').inputValue(), 'ReviewBeforeImport');
        assert.equal(await page.getByLabel('Order extraction and reply model').inputValue(), '');
        assert.equal(await page.locator('#ReplyLanguage option[value="zh-TW"]').innerText(), 'Traditional Chinese');

        // System text is English; user-entered names and document contents remain untouched.
        for (const route of ['/Companies', '/Companies/Create', '/Customers', '/Products', '/Products/Create', '/Products/Import', '/WorkOrders', '/WorkOrders/New', '/Settings']) {
            await page.goto(base + route);
            const labels = await page.locator('h1,h2,h3,h4,h5,label,button,a,summary,option').allTextContents();
            assert.ok(!labels.some(text => /[\u3400-\u9fff]/.test(text)), 'Non-English interface text on ' + route);
        }

        await staleTab.locator('#Sku').fill('STALE-COMPANY');
        await staleTab.locator('#Name').fill('Must not save in new company');
        await staleTab.locator('#UnitOfMeasure').fill('each');
        await staleTab.getByRole('button', { name: 'Create Product', exact: true }).click();
        await staleTab.getByText(/active company changed in another tab/).waitFor();

        await page.goto(base + '/Companies');
        await page.screenshot({ path: path.join(__dirname, '../artifacts/company-profiles.png'), fullPage: true });
        const demo = page.locator('.company-profile').filter({ has: page.locator('h5', { hasText: 'FTI Group Demo' }) });
        await demo.getByRole('button', { name: 'Use This Company', exact: true }).click();
        await page.goto(base + '/Products');
        assert.match(await page.locator('main').innerText(), /DEMO-BD-C075/);
        assert.deepEqual(errors, []);
        console.log('PASS: Edge create company, automatic switch, empty data, default settings, English UI and stale-tab protection.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
