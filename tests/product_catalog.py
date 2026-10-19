"""Phase 2 regression cases. Run: python tests/order_editing.py --catalog"""
import datetime as dt
import http.cookiejar
import json
import re
import unittest
import urllib.error
import urllib.parse
import urllib.request
import uuid

# The runner is __main__; use its active temporary database and helpers.
import __main__ as runner


class ProductCatalogTests(unittest.TestCase):
    request = runner.OrderEditingTests.request
    get_form = runner.OrderEditingTests.get_form
    post = runner.OrderEditingTests.post

    @classmethod
    def setUpClass(cls):
        runner.sql("""
            INSERT INTO "Customers" ("Id", "CompanyName", "ContactName", "Phone", "Email", "BillingAddress", "Notes")
            VALUES (501, 'Catalog Test Customer', '', '', '', '10 Test Road', '');
            INSERT INTO "ConstructionSites" ("Id", "CustomerId", "SiteName", "Address", "Suburb", "State", "Postcode", "ContactName", "ContactPhone", "Status")
            VALUES (601, 501, 'Catalog Test Site', '1 Test Street', 'Sydney', 'NSW', '2000', '', '', 'Active');
        """)
        cls.customer_before = runner.rows("Customers")
        cls.sites_before = runner.rows("ConstructionSites")

    @classmethod
    def tearDownClass(cls):
        assert runner.rows("Customers") == cls.customer_before
        assert runner.rows("ConstructionSites") == cls.sites_before

    def setUp(self):
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.sku = "TEST-" + uuid.uuid4().hex[:12].upper()
        self.values = dict(Sku=self.sku, Name="Galvanised panel", Description="Panel description",
                           Category="Panels", UnitOfMeasure="sheet", Dimensions="2400 x 1200 x 19 mm",
                           Specifications="Z275 coating", Packaging="Pack of 10", IsActive="true",
                           Aliases="GP|Galv Panel|gp")
        status, body, url = self.post("/Products/Create", self.values)
        self.assertEqual(status, 200)
        self.assertTrue(url.endswith("/Products"), body[:500])
        self.product = runner.rows("Products", f'"Sku" = \'{self.sku}\'')[0]
        self.product_id = self.product["Id"]

    def edit_product(self, **changes):
        fields, _ = self.get_form(f"/Products/Edit/{self.product_id}")
        values = dict(self.values, Version=fields["Version"])
        values.update(changes)
        return self.post(f"/Products/Edit/{self.product_id}", values)

    def create_order(self, product_id=None):
        runner.stock_fixture(product_id or self.product_id)
        values = {"CustomerId": 501, "ConstructionSiteId": 601, "DeliveryType": "Site",
                  "DueDate": (dt.date.today() + dt.timedelta(days=15)).isoformat(),
                  "Items[0].ProductId": product_id or self.product_id, "Items[0].Quantity": 12}
        _, body, url = self.post("/WorkOrders/CreateOrder", values, "/WorkOrders/New")
        self.assertTrue(url.endswith("/WorkOrders"), body[:500])
        order_id = int(runner.sql('SELECT max("Id") FROM "WorkOrders";'))
        item = runner.rows("WorkOrderItems", f'"WorkOrderId" = {order_id}')[0]
        return order_id, item

    def edit_order_values(self, order_id, item):
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order_id}")
        return {"Version": fields["Version"], "CustomerId": 501, "ConstructionSiteId": 601,
                "DeliveryType": "Site", "DueDate": (dt.date.today() + dt.timedelta(days=15)).isoformat(),
                "Items[0].Id": item["Id"], "Items[0].ProductId": item["ProductId"], "Items[0].Quantity": 19}

    def import_csv(self, content, filename="products.csv"):
        fields, _ = self.get_form("/Products/Import")
        boundary = "catalog-" + uuid.uuid4().hex
        raw = content.encode("utf-8") if isinstance(content, str) else content
        data = (f'--{boundary}\r\nContent-Disposition: form-data; name="__RequestVerificationToken"\r\n\r\n'
                f'{fields["__RequestVerificationToken"]}\r\n--{boundary}\r\n'
                f'Content-Disposition: form-data; name="File"; filename="{filename}"\r\n'
                'Content-Type: text/csv\r\n\r\n').encode() + raw + f'\r\n--{boundary}--\r\n'.encode()
        request = urllib.request.Request(runner.BASE + "/Products/Import", data=data,
                                        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
        try:
            response = self.client.open(request, timeout=60)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.read().decode(), response.geturl()

    def test_full_product_crud_and_alias_reconciliation(self):
        self.assertEqual(self.product["UnitOfMeasure"], "sheet")
        aliases = runner.rows("ProductAliases", f'"ProductId" = {self.product_id}')
        self.assertEqual(len(aliases), 2)
        gp_id = next(x["Id"] for x in aliases if x["NormalizedValue"] == "GP")
        _, body, url = self.edit_product(Name="Updated panel", Aliases="GP|New alias", Packaging="Pallet")
        self.assertTrue(url.endswith("/Products"), body[:500])
        current = runner.rows("Products", f'"Id" = {self.product_id}')[0]
        self.assertEqual(current["Name"], "Updated panel")
        self.assertEqual(current["Packaging"], "Pallet")
        aliases = runner.rows("ProductAliases", f'"ProductId" = {self.product_id}')
        self.assertEqual({x["Value"] for x in aliases}, {"GP", "New alias"})
        self.assertEqual(next(x["Id"] for x in aliases if x["NormalizedValue"] == "GP"), gp_id)
        fields, _ = self.get_form(f"/Products/Delete/{self.product_id}")
        self.post(f"/Products/Delete/{self.product_id}", {"version": fields["version"]})
        self.assertEqual(runner.rows("Products", f'"Id" = {self.product_id}'), [])
        self.assertEqual(runner.rows("ProductAliases", f'"ProductId" = {self.product_id}'), [])

    def test_required_fields_unique_normalized_sku_and_lengths(self):
        before = len(runner.rows("Products"))
        for changes, expected in [({"Sku": self.sku.lower()}, "SKU already exists"),
                                  ({"Name": " "}, "Name field is required"),
                                  ({"Sku": "bad code!"}, "SKU must contain"),
                                  ({"UnitOfMeasure": ""}, "UnitOfMeasure field is required"),
                                  ({"Aliases": "x" * 201}, "no longer than 200"),
                                  ({"Dimensions": "x" * 501}, "maximum length")]:
            with self.subTest(changes=changes):
                values = dict(self.values, Sku="NEW-" + uuid.uuid4().hex[:12])
                values.update(changes)
                status, body, _ = self.post("/Products/Create", values)
                self.assertEqual(status, 200)
                self.assertIn(expected, body)
                self.assertEqual(len(runner.rows("Products")), before)
        sku = " space-" + uuid.uuid4().hex[:8] + " "
        _, _, url = self.post("/Products/Create", dict(self.values, Sku=sku))
        self.assertTrue(url.endswith("/Products"))
        self.assertEqual(len(runner.rows("Products", f'"Sku" = \'{sku.strip().upper()}\'')), 1)

    def test_lookup_sku_precedence_alias_ambiguity_and_inactive(self):
        key = "shared-" + uuid.uuid4().hex[:12]
        self.edit_product(Aliases=key)
        second_sku = "OTHER-" + uuid.uuid4().hex[:10].upper()
        self.post("/Products/Create", dict(self.values, Sku=second_sku, Aliases=key + "|" + self.sku))
        _, body, _ = self.request("/Products/Lookup?q=" + urllib.parse.quote(key.upper()))
        self.assertEqual(len(json.loads(body)), 2)
        _, body, _ = self.request("/Products/Lookup?q=" + self.sku.lower())
        self.assertEqual([x["id"] for x in json.loads(body)], [self.product_id])
        self.edit_product(Aliases=key, IsActive="false")
        _, body, _ = self.request("/Products/Lookup?q=" + urllib.parse.quote(key))
        self.assertEqual([x["sku"] for x in json.loads(body)], [second_sku])

    def test_catalog_search_and_status_filters(self):
        unique_alias = "alias-" + uuid.uuid4().hex
        self.edit_product(Aliases=unique_alias, IsActive="false")
        _, body, _ = self.request("/Products?q=" + unique_alias)
        self.assertIn(self.sku, body)
        _, body, _ = self.request("/Products?status=active&q=" + unique_alias)
        self.assertNotIn(self.sku, body)
        _, body, _ = self.request("/Products?status=inactive&q=" + unique_alias)
        self.assertIn(self.sku, body)
        self.assertEqual(self.request("/Products/Edit/999999")[0], 404)
        self.assertEqual(self.request("/Products/Delete/999999")[0], 404)
        self.assertEqual(self.request("/Products/Create", self.values)[0], 400)

    def test_csv_valid_optional_and_minimal_columns(self):
        code = "CSV-" + uuid.uuid4().hex[:10].upper()
        data = ('Sku,Name,Description,Category,UnitOfMeasure,Dimensions,Specifications,Packaging,IsActive,Aliases\r\n'
                f'{code},"Panel, façade",Description,Cladding,sheet,2400 mm,Spec,Pack,false,FA|Facade\r\n')
        status, body, url = self.import_csv(data)
        self.assertEqual(status, 200)
        self.assertTrue(url.endswith("/Products"), body[:1000])
        product = runner.rows("Products", f'"Sku" = \'{code}\'')[0]
        self.assertEqual(product["Name"], "Panel, façade")
        self.assertFalse(product["IsActive"])
        self.assertEqual(len(runner.rows("ProductAliases", f'"ProductId" = {product["Id"]}')), 2)
        code2 = "MIN-" + uuid.uuid4().hex[:10].upper()
        _, body, url = self.import_csv(f' sku , NAME , unitofmeasure\n{code2.lower()},Minimal product,box\n')
        self.assertTrue(url.endswith("/Products"), body[:1000])
        self.assertTrue(runner.rows("Products", f'"Sku" = \'{code2}\'')[0]["IsActive"])

    def test_csv_errors_import_nothing(self):
        code = "BAD-" + uuid.uuid4().hex[:10].upper()
        before = runner.rows("Products")
        cases = [
            (f'Sku,Name,UnitOfMeasure\n{code},Valid,each\n{code.lower()},Duplicate,each\n', "duplicate SKU within this file"),
            (f'Sku,Name,UnitOfMeasure\n{code},Valid,each\n{self.sku},Existing,each\n', "SKU already exists"),
            (f'Sku,Name,UnitOfMeasure\n{code},Valid,each\n{code}X,,each\n', "Row 3"),
            (f'Sku,Name,UnitOfMeasure,IsActive\n{code},Valid,each,maybe\n', "IsActive must be true or false"),
            ('Sku,Name\nABC,Missing unit\n', "Missing required CSV column: UnitOfMeasure"),
            ('Sku,Name,UnitOfMeasure\n', "at least one product row"),
            ('Sku,Name,Name,UnitOfMeasure\nA,B,C,D\n', "column names must be unique"),
            ('Sku,Name,UnitOfMeasure\nA,Wrong columns\n', "could not be read"),
            (b'Sku,Name,UnitOfMeasure\nA,\xff,each\n', "could not be read"),
        ]
        for content, expected in cases:
            with self.subTest(expected=expected):
                status, body, _ = self.import_csv(content)
                self.assertEqual(status, 200)
                self.assertIn(expected, body)
                self.assertEqual(runner.rows("Products"), before)
        self.assertIn("nonempty CSV file", self.import_csv("", "bad.txt")[1])

    def test_rename_preserves_snapshots_and_stock_unit_is_locked(self):
        order_id, item = self.create_order()
        self.assertEqual(item["ProductSkuSnapshot"], self.sku)
        self.assertEqual(item["UnitOfMeasureSnapshot"], "sheet")
        _, body, _ = self.edit_product(UnitOfMeasure="pack")
        self.assertIn("inventory history", body)
        self.edit_product(Name="Renamed panel", Sku="RENAMED-" + uuid.uuid4().hex[:8])
        self.assertEqual(runner.rows("WorkOrderItems", f'"Id" = {item["Id"]}')[0], item)
        values = self.edit_order_values(order_id, item)
        _, body, url = self.post(f"/WorkOrders/Edit/{order_id}", values)
        self.assertTrue(url.endswith("/WorkOrders"), body[:1000])
        current = runner.rows("WorkOrderItems", f'"Id" = {item["Id"]}')[0]
        self.assertEqual(current["ProductType"], item["ProductType"])
        self.assertEqual(current["ProductSkuSnapshot"], item["ProductSkuSnapshot"])
        self.assertEqual(current["UnitOfMeasureSnapshot"], "sheet")
        self.assertEqual(current["Quantity"], 19)
        order = runner.rows("WorkOrders", f'"Id" = {order_id}')[0]
        self.assertEqual(order["ProductType"], item["ProductType"])

    def test_inactive_rejected_on_new_lines_but_existing_retained(self):
        order_id, item = self.create_order()
        self.edit_product(IsActive="false")
        fields, body = self.get_form("/WorkOrders/New")
        self.assertNotIn(f'value="{self.product_id}" data-search=', body)
        values = self.edit_order_values(order_id, item)
        _, body, url = self.post(f"/WorkOrders/Edit/{order_id}", values)
        self.assertTrue(url.endswith("/WorkOrders"), body[:1000])
        values = self.edit_order_values(order_id, item)
        values.update({"Items[1].ProductId": self.product_id, "Items[1].Quantity": 1})
        _, body, _ = self.post(f"/WorkOrders/Edit/{order_id}", values)
        self.assertIn("inactive product may only be kept", body)
        self.assertEqual(len(runner.rows("WorkOrderItems", f'"WorkOrderId" = {order_id}')), 1)
        values = {"CustomerId": 501, "DeliveryType": "Company", "DueDate": (dt.date.today() + dt.timedelta(days=10)).isoformat(),
                  "Items[0].ProductId": self.product_id, "Items[0].Quantity": 1}
        _, body, _ = self.post("/WorkOrders/CreateOrder", values, "/WorkOrders/New")
        self.assertIn("select a valid product", body)
        values["Items[0].ProductId"] = 999999
        _, body, _ = self.post("/WorkOrders/CreateOrder", values, "/WorkOrders/New")
        self.assertIn("select a valid product", body)

    def test_delete_referenced_product_is_blocked(self):
        self.create_order()
        fields, body = self.get_form(f"/Products/Delete/{self.product_id}")
        self.assertIn("cannot be deleted", body)
        version = runner.sql(f'SELECT xmin::text FROM "Products" WHERE "Id" = {self.product_id};')
        self.post(f"/Products/Delete/{self.product_id}", {"version": version}, "/Products/Create")
        self.assertEqual(len(runner.rows("Products", f'"Id" = {self.product_id}')), 1)

    def test_product_stale_edit_rejected(self):
        fields, _ = self.get_form(f"/Products/Edit/{self.product_id}")
        stale = dict(self.values, Version=fields["Version"], Name="Stale name", Aliases="Stale alias")
        self.edit_product(Name="Current name")
        _, body, _ = self.post(f"/Products/Edit/{self.product_id}", stale)
        self.assertIn("product has changed", body)
        self.assertEqual(runner.rows("Products", f'"Id" = {self.product_id}')[0]["Name"], "Current name")

    def test_replacing_product_takes_new_snapshots(self):
        order_id, item = self.create_order()
        other_sku = "REPLACE-" + uuid.uuid4().hex[:8].upper()
        self.post("/Products/Create", dict(self.values, Sku=other_sku, Name="Replacement", UnitOfMeasure="metre"))
        other = runner.rows("Products", f'"Sku" = \'{other_sku}\'')[0]
        runner.stock_fixture(other["Id"])
        values = self.edit_order_values(order_id, item)
        values["Items[0].ProductId"] = other["Id"]
        self.post(f"/WorkOrders/Edit/{order_id}", values)
        updated = runner.rows("WorkOrderItems", f'"Id" = {item["Id"]}')[0]
        self.assertEqual(updated["ProductId"], other["Id"])
        self.assertEqual(updated["ProductSkuSnapshot"], other_sku)
        self.assertEqual(updated["UnitOfMeasureSnapshot"], "metre")

    def test_additive_upgrade_and_repeat_preserve_legacy_records(self):
        script = (runner.ROOT / "FactoryOps.Web/Data/Sql/002-product-catalog.sql").read_text(encoding="utf-8")
        runner.sql('CREATE SCHEMA phase1_upgrade; '
                   'CREATE TABLE phase1_upgrade."WorkOrders" ("Id" integer PRIMARY KEY, "ProductType" text); '
                   'CREATE TABLE phase1_upgrade."WorkOrderItems" ("Id" integer PRIMARY KEY, "ProductType" text, "Quantity" integer); '
                   'INSERT INTO phase1_upgrade."WorkOrders" VALUES (44, \'Existing custom product\'); '
                   'INSERT INTO phase1_upgrade."WorkOrderItems" VALUES (55, \'Existing custom product\', 17);')
        apply = 'BEGIN; SET LOCAL search_path TO phase1_upgrade; ' + script + ' COMMIT;'
        runner.sql(apply)
        record = json.loads(runner.sql('SELECT row_to_json(t) FROM phase1_upgrade."WorkOrderItems" t;'))
        self.assertEqual(record["Id"], 55)
        self.assertEqual(record["Quantity"], 17)
        self.assertEqual(record["ProductType"], "Existing custom product")
        self.assertIsNotNone(record["ProductId"])
        self.assertEqual(record["ProductSkuSnapshot"], "")
        self.assertEqual(record["UnitOfMeasureSnapshot"], "")
        runner.sql('UPDATE phase1_upgrade."Products" SET "IsActive" = false;')
        runner.sql(apply)
        self.assertEqual(runner.sql('SELECT count(*) FROM phase1_upgrade."Products";'), "4")
        self.assertEqual(runner.sql('SELECT count(*) FROM phase1_upgrade."Products" WHERE "IsActive";'), "0")
        self.assertEqual(json.loads(runner.sql('SELECT row_to_json(t) FROM phase1_upgrade."WorkOrderItems" t;')), record)
