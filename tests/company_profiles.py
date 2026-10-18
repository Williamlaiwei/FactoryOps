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

    def create_company(self, name="New Test Company"):
        fields, _ = self.get_form("/Companies/Create")
        values = dict(fields, Name=name, Website="https://example.com")
        result = self.request("/Companies/Create", values)
        return "company-" + fields["CreationToken"].replace("-", ""), values, result

    def profile_count(self):
        return int(runner.sql('SELECT count(*) FROM "CompanyProfiles";'))

    def test_f_create_empty_company_and_default_settings(self):
        before_products = runner.rows("Products")
        key, values, (status, body, url) = self.create_company()
        self.assertEqual(status, 200, body[:500])
        self.assertTrue(url.endswith("/Companies"))
        self.assertIn("created and selected", body)
        fields, body = self.get_form("/Settings")
        self.assertEqual(fields["__CompanyKey"], key)
        self.assertEqual(fields["OrderModel"], "")
        self.assertIn('<option value="ReviewBeforeImport" selected="selected">', body)
        for table in ("Products", "Customers", "ConstructionSites", "WorkOrders"):
            self.assertEqual(runner.rows(table, f'"CompanyKey" = \'{key}\''), [])
        self.assertEqual(runner.rows("Products"), before_products)
        self.assertEqual(int(runner.sql(f'SELECT count(*) FROM "AiWorkflowSettings" WHERE "CompanyKey" = \'{key}\';')), 1)

    def test_g_replayed_create_cannot_duplicate_or_overwrite(self):
        key, values, _ = self.create_company("Repeated Create Company")
        before = self.profile_count()
        self.select("fti-demo")
        values.update(Name="Must not overwrite", IsDemo="true", Key="fti-demo")
        _, body, _ = self.request("/Companies/Create", values)
        self.assertIn("No duplicate was added", body)
        self.assertEqual(self.profile_count(), before)
        profile = json.loads(runner.sql(f'SELECT row_to_json(t) FROM "CompanyProfiles" t WHERE "Key" = \'{key}\';'))
        self.assertEqual(profile["Name"], "Repeated Create Company")
        self.assertFalse(profile["IsDemo"])

    def test_h_create_validation_and_antiforgery(self):
        before = self.profile_count()
        for changes in ({"Name": ""}, {"Name": " "}, {"Name": "x" * 201},
                        {"Website": "http://example.com"}, {"Website": "invalid"},
                        {"CreationToken": ""}, {"CreationToken": "not-a-guid"},
                        {"CreationToken": "00000000-0000-0000-0000-000000000000"}):
            fields, _ = self.get_form("/Companies/Create")
            values = dict(fields, Name="Invalid Test", Website="https://example.com")
            values.update(changes)
            status, body, _ = self.request("/Companies/Create", values)
            self.assertEqual(status, 200)
            self.assertIn("validation-summary-errors", body)
            self.assertEqual(self.profile_count(), before)
        self.assertEqual(self.request("/Companies/Create", {"Name": "Missing token"})[0], 400)

    def test_i_creation_rolls_back_if_settings_fail(self):
        before = self.profile_count()
        runner.sql('''CREATE FUNCTION company_settings_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected settings write failure'; END $$;
            CREATE TRIGGER company_settings_failure BEFORE INSERT ON "AiWorkflowSettings"
            FOR EACH ROW EXECUTE FUNCTION company_settings_failure();''')
        try:
            _, _, (status, body, _) = self.create_company("Rollback Test Company")
            self.assertEqual(status, 200)
            self.assertIn("No company or settings were saved", body)
            self.assertEqual(self.profile_count(), before)
            fields, _ = self.get_form("/Settings")
            self.assertEqual(fields["__CompanyKey"], "fti-demo")
        finally:
            runner.sql('DROP TRIGGER company_settings_failure ON "AiWorkflowSettings"; DROP FUNCTION company_settings_failure();')

    def test_j_new_company_records_are_independent(self):
        old_products = runner.rows("Products", '"CompanyKey" = \'fti-demo\'')
        key, _, _ = self.create_company("Independent Company")
        self.post("/Products/Create", {"Sku": "DEMO-BD-C075", "Name": "Independent Product", "UnitOfMeasure": "box", "IsActive": "true"})
        self.post("/Customers/Create", {"CompanyName": "Independent Customer", "BillingAddress": "1 Example Road"}, "/Customers")
        self.assertEqual(len(runner.rows("Products", f'"CompanyKey" = \'{key}\'')), 1)
        self.assertEqual(len(runner.rows("Customers", f'"CompanyKey" = \'{key}\'')), 1)
        self.select("fti-demo")
        self.assertEqual(runner.rows("Products", '"CompanyKey" = \'fti-demo\''), old_products)
        _, body, _ = self.request("/Customers")
        self.assertNotIn("Independent Customer", body)
