import __main__ as runner
import http.cookiejar
import json
import unittest
import urllib.request


class CompanyProfileTests(unittest.TestCase):
    request = runner.OrderEditingTests.request
    get_form = runner.OrderEditingTests.get_form
    post = runner.OrderEditingTests.post

    def setUp(self):
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def select(self, key):
        return self.post("/Companies/Select", {"key": key}, "/Companies")

    def test_a_empty_slot_and_demo_sources(self):
        self.assertEqual(len(runner.rows("Products", '"CompanyKey" = \'fti-demo\' AND "IsDemo"')), 13)
        self.assertEqual(runner.rows("Products", '"CompanyKey" = \'blank-company\''), [])
        self.select("blank-company")
        _, body, _ = self.request("/Products")
        self.assertNotIn("DEMO-BD-C075", body)
        self.assertIn("0 product(s)", body)
        _, body, _ = self.request("/WorkOrders")
        self.assertIn("No orders yet", body)

    def test_b_company_scoped_skus_and_lookup(self):
        self.select("blank-company")
        _, body, url = self.post("/Products/Create", {"Sku": "DEMO-BD-C075", "Name": "Another company product", "UnitOfMeasure": "box", "IsActive": "true"})
        self.assertTrue(url.endswith("/Products"), body[:500])
        _, body, _ = self.request("/Products/Lookup?q=DEMO-BD-C075")
        self.assertEqual(json.loads(body)[0]["name"], "Another company product")
        self.select("fti-demo")
        _, body, _ = self.request("/Products/Lookup?q=DEMO-BD-C075")
        self.assertEqual(json.loads(body)[0]["name"], "BlueDeck Composite 0.75 mm")

    def test_c_cross_company_ids_are_rejected(self):
        product = runner.rows("Products", '"CompanyKey" = \'fti-demo\' AND "Sku" = \'DEMO-BD-C075\'')[0]
        self.select("blank-company")
        self.assertEqual(self.request(f'/Products/Edit/{product["Id"]}')[0], 404)
        self.assertEqual(self.request(f'/Products/Delete/{product["Id"]}')[0], 404)
        _, body, _ = self.post("/WorkOrders/CreateOrder", {"CustomerId": 999999, "DeliveryType": "Company", "DueDate": "2099-01-01", "Items[0].ProductId": product["Id"], "Items[0].Quantity": 1}, "/WorkOrders/New")
        self.assertIn("select a valid product", body)

    def test_d_stale_company_form_is_not_saved(self):
        self.get_form("/Products/Create")
        self.select("blank-company")
        before = runner.rows("Products")
        _, body, url = self.post("/Products/Create", {"__CompanyKey": "fti-demo", "Sku": "STALE-FORM", "Name": "Wrong company", "UnitOfMeasure": "each", "IsActive": "true"})
        self.assertTrue(url.endswith("/Companies"))
        self.assertIn("active company changed", body)
        self.assertEqual(runner.rows("Products"), before)

    def test_e_repeat_schema_keeps_empty_profile_free_of_demo(self):
        before = runner.rows("Products")
        scripts = sorted((runner.ROOT / "FactoryOps.Web/Data/Sql").glob("*.sql"))
        runner.sql("BEGIN;" + "\n".join(p.read_text(encoding="utf-8") for p in scripts) + "COMMIT;")
        self.assertEqual(runner.rows("Products"), before)
        self.assertFalse(any(p["IsDemo"] for p in runner.rows("Products", '"CompanyKey" = \'blank-company\'')))
