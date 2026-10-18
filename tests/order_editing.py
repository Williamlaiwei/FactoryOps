"""HTTP + PostgreSQL regression suite. Requires Docker Compose and Python 3.10+.

Creates a temporary database and a separate web container, then removes only those
test resources. Never resets or writes application data in the development database.
"""
import copy
import datetime as dt
import html
import http.cookiejar
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
DATABASE = "factoryops_phase1_" + uuid.uuid4().hex
ENV = dict(os.environ, FACTORYOPS_TEST_DATABASE=DATABASE)
COMPOSE = ["docker", "compose", "-f", "compose.yaml", "-f", "compose.tests.yaml"]
BASE = "http://127.0.0.1:" + ENV.get("FACTORYOPS_TEST_PORT", "18080")


def command(args, **kwargs):
    result = subprocess.run(args, cwd=ROOT, env=ENV, text=True, **kwargs)
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {' '.join(args)}")
    return result


def sql(statement, database=DATABASE):
    result = command(
        ["docker", "compose", "exec", "-T", "db", "psql", "-U", "factoryops",
         "-d", database, "-v", "ON_ERROR_STOP=1", "-t", "-A"],
        input=statement, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return result.stdout.strip()


def rows(table, where="TRUE"):
    return json.loads(sql(
        f'SELECT coalesce(json_agg(t ORDER BY t."Id"), \'[]\'::json) '
        f'FROM (SELECT * FROM "{table}" WHERE {where}) t;'))


def development_fingerprint():
    return sql("""
        SELECT json_build_object(
          'customers', (SELECT md5(coalesce(string_agg(row_to_json(t)::text, '' ORDER BY t."Id"), '')) FROM "Customers" t),
          'sites', (SELECT md5(coalesce(string_agg(row_to_json(t)::text, '' ORDER BY t."Id"), '')) FROM "ConstructionSites" t),
          'orders', (SELECT md5(coalesce(string_agg(row_to_json(t)::text, '' ORDER BY t."Id"), '')) FROM "WorkOrders" t),
          'items', (SELECT md5(coalesce(string_agg(row_to_json(t)::text, '' ORDER BY t."Id"), '')) FROM "WorkOrderItems" t)
        );
    """, "factoryops")


class OrderEditingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        sql("""
            INSERT INTO "Customers" ("Id", "CompanyName", "ContactName", "Phone", "Email", "BillingAddress", "Notes") VALUES
              (101, 'Phase One Alpha', '', '', '', '10 Company Road, Sydney NSW 2000', ''),
              (102, 'Phase One Beta', '', '', '', '20 Company Road, Sydney NSW 2000', ''),
              (103, 'No Company Address', '', '', '', '', '');
            INSERT INTO "ConstructionSites" ("Id", "CustomerId", "SiteName", "Address", "Suburb", "State", "Postcode", "ContactName", "ContactPhone", "Status") VALUES
              (201, 101, 'Alpha Site', '1 Site Road', 'Sydney', 'NSW', '2000', '', '', 'Active'),
              (202, 102, 'Beta Site', '2 Site Road', 'Sydney', 'NSW', '2000', '', '', 'Active'),
              (203, 101, 'Inactive Site', '3 Site Road', 'Sydney', 'NSW', '2000', '', '', 'Completed'),
              (204, 101, 'Other Alpha Site', '4 Site Road', 'Sydney', 'NSW', '2000', '', '', 'Active'),
              (205, 101, 'Missing Address', '', 'Sydney', 'NSW', '2000', '', '', 'Active');
        """)
        cls.customers_before = rows("Customers")
        cls.sites_before = rows("ConstructionSites")

    @classmethod
    def tearDownClass(cls):
        assert rows("Customers") == cls.customers_before, "Customer records changed"
        assert rows("ConstructionSites") == cls.sites_before, "Site records changed"
        print("Verified: test customer/site records remain intact.", flush=True)

    def request(self, path, data=None):
        encoded = urllib.parse.urlencode(data).encode() if data is not None else None
        try:
            response = self.client.open(BASE + path, data=encoded, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.read().decode(), response.geturl()

    def get_form(self, path):
        status, body, _ = self.request(path)
        self.assertEqual(status, 200, body[:300])
        fields = dict(re.findall(r'<input\b[^>]*name="([^"]+)"[^>]*value="([^"]*)"', body))
        return {key: html.unescape(value) for key, value in fields.items()}, body

    def post(self, path, values, token_path=None):
        fields, _ = self.get_form(token_path or path)
        return self.request(path, dict(values, __RequestVerificationToken=fields["__RequestVerificationToken"]))

    def state(self):
        order = rows("WorkOrders", f'"Id" = {self.order_id}')[0]
        items = rows("WorkOrderItems", f'"WorkOrderId" = {self.order_id}')
        return order, items

    def editable(self):
        order, items = self.state()
        fields, _ = self.get_form(f"/WorkOrders/Edit/{self.order_id}")
        values = {
            "Version": fields["Version"], "CustomerId": order["CustomerId"],
            "ConstructionSiteId": order["ConstructionSiteId"] or "",
            "DeliveryType": order["DeliveryType"], "DueDate": order["DueDate"],
            "DeliveryInstructions": order["DeliveryInstructions"]}
        for index, item in enumerate(items):
            for key in ("Id", "ProductType", "Quantity"):
                values[f"Items[{index}].{key}"] = item[key]
        return values

    def setUp(self):
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        future = (dt.date.today() + dt.timedelta(days=10)).isoformat()
        payload = {"CustomerId": 101, "ConstructionSiteId": 201, "DeliveryType": "Site",
                   "DueDate": future, "DeliveryInstructions": "Original instructions"}
        for index, product in enumerate(("Fast Tread", "BlueDeck", "SureFit")):
            payload[f"Items[{index}].ProductType"] = product
            payload[f"Items[{index}].Quantity"] = (index + 1) * 10
        status, body, url = self.post("/WorkOrders/CreateOrder", payload, "/WorkOrders/New")
        self.assertEqual(status, 200, body[:300])
        self.assertTrue(url.endswith("/WorkOrders"), url)
        self.order_id = int(sql('SELECT max("Id") FROM "WorkOrders";'))
        self.assertEqual(len(self.state()[1]), 3)

    def assert_rejected(self, changes, expected):
        before = self.state()
        values = self.editable()
        values.update(changes)
        status, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertEqual(status, 200)
        self.assertIn(expected, body)
        self.assertEqual(self.state(), before)
        return body

    def test_complete_multi_item_edit_and_reload(self):
        before_order, before_items = self.state()
        values = self.editable()
        values["Items[0].Quantity"] = 75
        # Remove the middle line; renumber like the browser and add a fourth product.
        values["Items[1].Id"] = before_items[2]["Id"]
        values["Items[1].ProductType"] = "SureFit"
        values["Items[1].Quantity"] = 30
        values["Items[2].Id"] = ""
        values["Items[2].ProductType"] = "BlueDeck"
        values["Items[2].Quantity"] = 45
        values["DueDate"] = (dt.date.today() + dt.timedelta(days=20)).isoformat()
        values["DeliveryInstructions"] = "Use the north gate <call first>"
        count_before = int(sql('SELECT count(*) FROM "WorkOrders";'))
        status, body, url = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertEqual(status, 200)
        self.assertTrue(url.endswith("/WorkOrders"), url)
        order, items = self.state()
        self.assertEqual(order["Id"], before_order["Id"])
        self.assertEqual(order["Quantity"], 150)
        self.assertEqual(order["DueDate"], values["DueDate"])
        self.assertEqual(order["DeliveryInstructions"], values["DeliveryInstructions"])
        self.assertEqual([(x["ProductType"], x["Quantity"]) for x in items],
                         [("Fast Tread", 75), ("SureFit", 30), ("BlueDeck", 45)])
        self.assertEqual(items[0]["Id"], before_items[0]["Id"])
        self.assertNotIn(before_items[1]["Id"], [x["Id"] for x in items])
        self.assertEqual(int(sql('SELECT count(*) FROM "WorkOrders";')), count_before)
        fields, edit_body = self.get_form(f"/WorkOrders/Edit/{self.order_id}")
        self.assertEqual(fields["Items[0].Quantity"], "75")
        self.assertIn("&lt;call first&gt;", edit_body)
        self.assertIn("BlueDeck", body)
        self.assertIn('value="75"', edit_body)

    def test_change_product_and_delivery_destinations(self):
        values = self.editable()
        values.update({"Items[0].ProductType": "BlueDeck", "ConstructionSiteId": 204})
        self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertEqual(self.state()[0]["ConstructionSiteId"], 204)
        self.assertIn("4 Site Road", self.state()[0]["DeliveryAddressSnapshot"])
        self.assertEqual(self.state()[1][0]["ProductType"], "BlueDeck")
        values = self.editable()
        values.update({"DeliveryType": "Company", "ConstructionSiteId": 202})
        self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        order, _ = self.state()
        self.assertIsNone(order["ConstructionSiteId"])
        self.assertEqual(order["DeliveryAddressSnapshot"], "10 Company Road, Sydney NSW 2000")
        values = self.editable()
        values.update({"CustomerId": 102, "DeliveryType": "Site", "ConstructionSiteId": 202})
        self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertEqual(self.state()[0]["CustomerId"], 102)

    def test_reject_invalid_sites_and_customer(self):
        for site in (202, 203, 205, 999999, ""):
            with self.subTest(site=site):
                self.assert_rejected({"ConstructionSiteId": site}, "Select an active construction site belonging")
        self.assert_rejected({"CustomerId": 999999}, "Select an existing customer.")
        self.assert_rejected({"CustomerId": 103, "DeliveryType": "Company"}, "no company address")
        self.assert_rejected({"DeliveryType": "Elsewhere"}, "Choose Construction Site or Company Address.")

    def test_reject_invalid_items_dates_and_binding(self):
        for quantity in (0, -1, "not-a-number", "1.5", "2147483648"):
            with self.subTest(quantity=quantity):
                self.assert_rejected({"Items[0].Quantity": quantity}, "validation-summary-errors")
        self.assert_rejected({"Items[0].ProductType": "Invented SKU"}, "select a valid product")
        self.assert_rejected({"Items[0].ProductType": ""}, "select a valid product")
        self.assert_rejected({"Items[0].Quantity": 2147483647}, "total quantity is too large")
        self.assert_rejected({"DueDate": "invalid-date"}, "validation-summary-errors")
        self.assert_rejected({"DueDate": ""}, "valid requested delivery date")
        self.assert_rejected({"DueDate": "2001-01-01"}, "new delivery date cannot be in the past")

    def test_reject_zero_items(self):
        before = self.state()
        values = {key: value for key, value in self.editable().items() if not key.startswith("Items[")}
        _, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertIn("Add at least one valid order item.", body)
        self.assertEqual(self.state(), before)
        self.assertIn('id="addItemButton"', body)

    def test_reject_foreign_and_duplicate_item_ids(self):
        self.assert_rejected({"Items[0].Id": 999999}, "does not belong to this order")
        values = self.editable()
        self.assert_rejected({"Items[1].Id": values["Items[0].Id"]}, "submitted more than once")

    def test_duplicate_post_and_stale_edit_are_rejected(self):
        values = self.editable()
        values["Items[0].Quantity"] = 123
        self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        saved = self.state()
        _, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertIn("changed since you opened it", body)
        self.assertEqual(self.state(), saved)
        stale = copy.deepcopy(values)
        stale["Items[0].Quantity"] = 456
        _, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", stale)
        self.assertIn("Reload the order before saving", body)
        self.assertEqual(self.state(), saved)

    def test_status_change_invalidates_open_edit(self):
        values = self.editable()
        self.post("/WorkOrders/UpdateStatus", {"id": self.order_id, "status": "In Progress"}, "/WorkOrders")
        self.assertEqual(self.state()[0]["Status"], "In Progress")
        _, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertIn("changed since you opened it", body)
        values = self.editable()
        values["Items[0].Quantity"] = 11
        values["Status"] = "Completed"  # Overposting cannot change server-owned fields.
        values["DeliveryAddressSnapshot"] = "Forged address"
        self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertEqual(self.state()[0]["Status"], "In Progress")
        self.assertNotEqual(self.state()[0]["DeliveryAddressSnapshot"], "Forged address")

    def test_atomic_rollback_on_database_failure(self):
        before = self.state()
        # Inject a failure in this isolated test DB only, after validation succeeds.
        sql("""
          CREATE FUNCTION phase1_fail_item() RETURNS trigger LANGUAGE plpgsql AS $$
          BEGIN
            IF NEW."Quantity" = 777777 THEN RAISE EXCEPTION 'Injected test failure'; END IF;
            RETURN NEW;
          END $$;
          CREATE TRIGGER phase1_fail BEFORE INSERT OR UPDATE ON "WorkOrderItems"
          FOR EACH ROW EXECUTE FUNCTION phase1_fail_item();
        """)
        try:
            values = self.editable()
            values["DeliveryInstructions"] = "Must roll back"
            values["Items[0].Quantity"] = 777777
            values["Items[1].Id"] = ""  # Delete one old line and insert a new one.
            _, body, _ = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
            self.assertIn("No changes were saved.", body)
            self.assertEqual(self.state(), before)
        finally:
            sql('DROP TRIGGER phase1_fail ON "WorkOrderItems"; DROP FUNCTION phase1_fail_item();')

    def test_edit_legacy_order_with_past_date(self):
        sql(f'DELETE FROM "WorkOrderItems" WHERE "WorkOrderId" = {self.order_id}; '
            f'UPDATE "WorkOrders" SET "DueDate" = \'2001-01-01\', "ProductType" = \'SureFit\', '
            f'"Quantity" = 5 WHERE "Id" = {self.order_id};')
        fields, body = self.get_form(f"/WorkOrders/Edit/{self.order_id}")
        self.assertEqual(fields["Items[0].Quantity"], "5")
        values = self.editable()
        values.update({"Items[0].ProductType": "SureFit", "Items[0].Quantity": 8})
        _, _, url = self.post(f"/WorkOrders/Edit/{self.order_id}", values)
        self.assertTrue(url.endswith("/WorkOrders"))
        self.assertEqual(self.state()[0]["DueDate"], "2001-01-01")
        self.assertEqual(self.state()[1][0]["Quantity"], 8)

    def test_create_validation_redisplays_values(self):
        count = int(sql('SELECT count(*) FROM "WorkOrders";'))
        values = self.editable()
        values["Items[0].ProductType"] = "Unknown"
        values["DeliveryInstructions"] = "Keep these instructions"
        for key in list(values):
            if key.endswith(".Id"):
                del values[key]
        _, body, _ = self.post("/WorkOrders/CreateOrder", values, "/WorkOrders/New")
        self.assertIn("select a valid product", body)
        self.assertIn("Keep these instructions", body)
        self.assertIn("Phase One Alpha", body)
        self.assertEqual(int(sql('SELECT count(*) FROM "WorkOrders";')), count)

    def test_customer_search_sites_not_found_and_antiforgery(self):
        status, body, _ = self.request("/Customers/Search?q=Phase%20One")
        self.assertEqual(status, 200)
        self.assertEqual(len(json.loads(body)), 2)
        status, body, _ = self.request("/Customers/Sites?customerId=101")
        self.assertEqual(status, 200)
        self.assertTrue(all(site["id"] != 202 for site in json.loads(body)))
        self.assertEqual(self.request("/WorkOrders/Edit/999999")[0], 404)
        self.assertEqual(self.post("/WorkOrders/Edit/999999", {}, "/WorkOrders/New")[0], 404)
        self.assertEqual(self.request(f"/WorkOrders/Edit/{self.order_id}", self.editable())[0], 400)


def main():
    baseline = development_fingerprint()
    created = False
    started = False
    try:
        assert re.fullmatch(r"factoryops_phase1_[a-f0-9]{32}", DATABASE)
        sql(f'CREATE DATABASE "{DATABASE}";', "postgres")
        created = True
        print("Building a separate test web container...", flush=True)
        started = True
        command(COMPOSE + ["up", "-d", "--build", "--no-deps", "order-tests"])
        deadline = time.monotonic() + 60
        while True:
            try:
                with urllib.request.urlopen(BASE + "/WorkOrders", timeout=3) as response:
                    if response.status == 200:
                        break
            except (OSError, urllib.error.URLError):
                if time.monotonic() >= deadline:
                    command(COMPOSE + ["logs", "--tail", "80", "order-tests"])
                    raise RuntimeError("Test app did not become ready")
                time.sleep(1)
        result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(OrderEditingTests))
        if result.wasSuccessful() and "--browser" in sys.argv:
            ENV["FACTORYOPS_TEST_BASE_URL"] = BASE
            command(["node", "tests/order_editing.browser.cjs"])
        return 0 if result.wasSuccessful() else 1
    finally:
        if started:
            command(COMPOSE + ["rm", "--stop", "--force", "order-tests"], stdout=subprocess.PIPE)
        if created:
            sql(f'DROP DATABASE "{DATABASE}" WITH (FORCE);', "postgres")
        if development_fingerprint() != baseline:
            raise RuntimeError("Development data changed during testing; investigate concurrent writes")
        print("Verified: development data unchanged; temporary test resources removed.", flush=True)


if __name__ == "__main__":
    sys.exit(main())
