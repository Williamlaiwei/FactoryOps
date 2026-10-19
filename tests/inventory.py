"""Dated inbound/outbound holds, physical dispatch and dashboard integration tests."""
import concurrent.futures
import datetime as dt
import http.cookiejar
import json
import re
import threading
import unittest
import urllib.request
import uuid
import __main__ as runner


class InventoryTests(unittest.TestCase):
    request = runner.OrderEditingTests.request
    get_form = runner.OrderEditingTests.get_form
    post = runner.OrderEditingTests.post

    @classmethod
    def setUpClass(cls):
        runner.sql('''INSERT INTO "Customers" ("Id","CompanyName","ContactName","Phone","Email","BillingAddress","Notes")
            VALUES (701,'Inventory Test Customer','','','','1 Inventory Road','');''')

    def setUp(self):
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.sku = "HOLD-" + uuid.uuid4().hex[:12].upper()
        self.post("/Products/Create", {"Sku": self.sku, "Name": "Hold test product", "UnitOfMeasure": "each", "IsActive": "true"})
        self.product_id = runner.rows("Products", f'"Sku"=\'{self.sku}\'')[0]["Id"]
        self.today = dt.date.fromisoformat(self.projection()["startDate"])

    def date(self, days):
        return (self.today + dt.timedelta(days=days)).isoformat()

    def projection(self, months=1, product_id=None):
        status, body, _ = self.request(f"/Dashboard/Projection?productId={product_id or self.product_id}&months={months}")
        self.assertEqual(status, 200, body[:500])
        return json.loads(body)

    def stock(self, product_id=None):
        return json.loads(runner.sql(f'SELECT row_to_json(t) FROM "InventoryStocks" t WHERE "ProductId"={product_id or self.product_id};') or "null")

    def configure(self, count=None, fields=None, product_id=None, reason="Test count"):
        product_id = product_id or self.product_id
        fields = fields or self.get_form(f"/Inventory/Configure/{product_id}")[0]
        return self.post(f"/Inventory/Configure/{product_id}", dict(fields, CountedQuantity="" if count is None else count,
                         ReorderPoint=0, LeadTimeDays=2, Reason=reason))

    def holds(self, order_id=None):
        where = f'"ProductId"={self.product_id} AND "Status"=\'Held\''
        if order_id: where += f' AND "WorkOrderId"={order_id}'
        return json.loads(runner.sql(f'SELECT coalesce(json_agg(t),\'[]\') FROM (SELECT * FROM "StockReservations" WHERE {where}) t;'))

    def payload(self, quantity, days=10, **extra):
        values = {"CustomerId": 701, "DeliveryType": "Company", "DueDate": self.date(days),
                  "Items[0].ProductId": self.product_id, "Items[0].Quantity": quantity}
        values.update(extra)
        return values

    def order(self, quantity, days=10, extra=None):
        _, body, url = self.post("/WorkOrders/CreateOrder", dict(self.payload(quantity, days), **(extra or {})), "/WorkOrders/New")
        self.assertTrue(url.endswith("/WorkOrders"), body[:800])
        return int(runner.sql('SELECT max("Id") FROM "WorkOrders";'))

    def action(self, order_id, action, version=None):
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order_id}")
        return self.post(f"/WorkOrders/{action}", {"id": order_id, "version": version or fields["Version"]}, "/WorkOrders")

    def status(self, order_id, status):
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order_id}")
        return self.post("/WorkOrders/UpdateStatus", {"id": order_id, "version": fields["Version"], "status": status}, "/WorkOrders")

    def edit(self, order_id, quantity, days=10, extra=None):
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order_id}")
        return self.post(f"/WorkOrders/Edit/{order_id}", dict(fields, **self.payload(quantity, days), **(extra or {})))

    def schedule(self, quantity, days):
        fields, _ = self.get_form("/Inventory/Schedule")
        _, body, url = self.post("/Inventory/Schedule", dict(fields, ProductId=self.product_id, Quantity=quantity, ExpectedDate=self.date(days)))
        self.assertTrue(url.endswith("/Inventory/Replenishments"), body[:700])
        return runner.rows("Replenishments", f'"ProductId"={self.product_id}')[-1]

    def receipt(self, receipt_id, action="Receive", version=None):
        version = version or runner.sql(f'SELECT xmin::text FROM "Replenishments" WHERE "Id"={receipt_id};')
        return self.post(f"/Inventory/{action}", {"id": receipt_id, "version": version}, "/Inventory/Schedule")

    def test_confirm_creates_hold_without_deducting_actual(self):
        self.configure(10)
        order = self.order(6)
        self.assertEqual(self.stock()["OnHand"], 10)
        self.assertEqual(sum(x["Quantity"] for x in self.holds(order)), 6)
        self.assertEqual(self.projection()["days"][-1]["available"], 4)
        self.assertEqual(runner.rows("WorkOrders", f'"Id"={order}')[0]["FulfillmentStatus"], "Reserved")

    def test_insufficient_order_is_rejected_and_late_arrival_cannot_cover_it(self):
        self.configure(5)
        self.schedule(10, 20)
        before = runner.rows("WorkOrders")
        _, body, _ = self.post("/WorkOrders/CreateOrder", self.payload(8, 10), "/WorkOrders/New")
        self.assertIn("would be short 3", body)
        self.assertEqual(runner.rows("WorkOrders"), before)
        self.assertEqual(self.holds(), [])
        self.schedule(3, 5)
        self.order(8, 10)
        self.assertEqual(self.stock()["OnHand"], 5)

    def test_existing_future_holds_are_protected_from_new_earlier_orders(self):
        self.configure(10)
        self.order(10, 20)
        _, body, _ = self.post("/WorkOrders/CreateOrder", self.payload(1, 5), "/WorkOrders/New")
        self.assertIn("after existing holds", body)
        self.assertIn(self.date(20), body)
        self.assertEqual(sum(x["Quantity"] for x in self.holds()), 10)

    def test_incoming_hold_receive_transfers_to_actual_without_double_count(self):
        self.configure(0)
        receipt = self.schedule(10, 5)
        self.order(10, 10)
        before = self.projection()
        self.assertEqual((before["onHand"], before["incomingHeld"], before["outboundHeld"]), (0, 10, 10))
        version = runner.sql(f'SELECT xmin::text FROM "Replenishments" WHERE "Id"={receipt["Id"]};')
        self.receipt(receipt["Id"], version=version)
        self.receipt(receipt["Id"], version=version)
        after = self.projection()
        self.assertEqual((after["onHand"], after["incomingHeld"], after["outboundHeld"]), (10, 0, 10))
        self.assertEqual(after["days"][-1]["available"], before["days"][-1]["available"])
        self.assertEqual(len(runner.rows("StockMovements", f'"ProductId"={self.product_id} AND "Kind"=\'Receipt\'')), 1)

    def test_ship_requires_actual_stock_then_releases_hold_and_deducts_once(self):
        self.configure(0)
        receipt = self.schedule(10, 5)
        order = self.order(10, 10)
        _, body, _ = self.action(order, "Ship")
        self.assertIn("Not enough actual stock", body)
        self.status(order, "Completed")
        self.assertEqual(self.stock()["OnHand"], 0)
        self.assertEqual(len(self.holds(order)), 1)
        self.receipt(receipt["Id"])
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order}")
        self.action(order, "Ship", fields["Version"])
        self.action(order, "Ship", fields["Version"])
        self.assertEqual(self.stock()["OnHand"], 0)
        self.assertEqual(self.holds(order), [])
        self.assertEqual(runner.rows("WorkOrders", f'"Id"={order}')[0]["FulfillmentStatus"], "Shipped")
        self.status(order, "Pending")
        self.assertEqual(self.stock()["OnHand"], 0)
        self.assertEqual(len(runner.rows("StockMovements", f'"WorkOrderId"={order}')), 1)

    def test_early_shipment_cannot_take_stock_held_for_an_earlier_delivery(self):
        self.configure(10)
        self.schedule(10, 10)
        earlier = self.order(8, 5)
        later = self.order(8, 20)
        _, body, _ = self.action(later, "Ship")
        self.assertIn("after existing holds", body)
        self.assertEqual(self.stock()["OnHand"], 10)
        self.action(earlier, "Ship")
        self.assertEqual(self.stock()["OnHand"], 2)
        self.assertEqual(len(self.holds(later)), 1)

    def test_edit_replaces_holds_cancel_releases_and_reconfirm_rechecks_capacity(self):
        self.configure(10)
        order = self.order(6)
        self.edit(order, 4, 15)
        self.assertEqual((self.holds(order)[0]["Quantity"], self.holds(order)[0]["NeedDate"]), (4, self.date(15)))
        self.action(order, "Cancel")
        self.assertEqual(self.holds(order), [])
        self.assertEqual(self.stock()["OnHand"], 10)
        self.order(10)
        _, body, _ = self.action(order, "Confirm")
        self.assertIn("would be short", body)
        self.assertEqual(self.holds(order), [])

    def test_unknown_stock_and_invalid_units_cannot_be_confirmed(self):
        _, body, _ = self.post("/WorkOrders/CreateOrder", self.payload(1), "/WorkOrders/New")
        self.assertIn("Stock is not tracked", body)
        self.configure(10)
        order = self.order(1)
        runner.sql(f'UPDATE "WorkOrderItems" SET "UnitOfMeasureSnapshot"=\'box\' WHERE "WorkOrderId"={order};')
        _, body, _ = self.edit(order, 1)
        self.assertIn("different ordering unit", body)
        _, body, url = self.edit(order, 1, extra={"ConfirmCurrentUnits": "true"})
        self.assertTrue(url.endswith("/WorkOrders"), body[:600])
        self.assertEqual(self.holds(order)[0]["UnitOfMeasure"], "each")

    def test_stock_loss_and_delayed_or_cancelled_arrivals_preserve_existing_holds(self):
        self.configure(2)
        receipt = self.schedule(8, 5)
        order = self.order(10)
        self.receipt(receipt["Id"], "Cancel")
        self.assertEqual(sum(x["Quantity"] for x in self.holds(order)), 10)
        self.assertEqual(min(x["available"] for x in self.projection()["days"]), -8)
        _, body, _ = self.post("/WorkOrders/CreateOrder", self.payload(1), "/WorkOrders/New")
        self.assertIn("would be short", body)
        # Metadata changes keep the same existing commitment; decreasing it can reduce a pre-existing shortage.
        self.edit(order, 9)
        self.assertEqual(self.holds(order)[0]["Quantity"], 9)
        self.configure(1)
        self.assertEqual(self.stock()["OnHand"], 1)
        self.assertEqual(self.holds(order)[0]["Quantity"], 9)

    def test_overdue_incoming_is_excluded_and_same_day_incoming_is_allowed(self):
        self.configure(0)
        receipt = self.schedule(5, 5)
        runner.sql(f'UPDATE "Replenishments" SET "ExpectedDate"=\'{self.date(-1)}\' WHERE "Id"={receipt["Id"]};')
        _, body, _ = self.post("/WorkOrders/CreateOrder", self.payload(5, 5), "/WorkOrders/New")
        self.assertIn("would be short", body)
        self.assertEqual(self.projection()["incomingHeld"], 0)
        self.schedule(5, 5)
        self.order(5, 5)
        self.assertEqual(self.projection()["days"][5]["available"], 0)

    def test_creation_replay_and_stale_order_edit_do_not_duplicate_holds(self):
        self.configure(20)
        fields, _ = self.get_form("/WorkOrders/New")
        self.post("/WorkOrders/CreateOrder", dict(self.payload(5), CreationToken=fields["CreationToken"]), "/WorkOrders/New")
        order = int(runner.sql('SELECT max("Id") FROM "WorkOrders";'))
        self.post("/WorkOrders/CreateOrder", dict(self.payload(5), CreationToken=fields["CreationToken"]), "/WorkOrders/New")
        self.assertEqual(sum(x["Quantity"] for x in self.holds()), 5)
        stale, _ = self.get_form(f"/WorkOrders/Edit/{order}")
        self.edit(order, 6)
        self.post(f"/WorkOrders/Edit/{order}", dict(stale, **self.payload(8)))
        self.assertEqual(self.holds(order)[0]["Quantity"], 6)

    def test_concurrent_confirmations_cannot_overbook(self):
        self.configure(10)
        ready = threading.Barrier(2)
        def confirm(_):
            helper = InventoryTests()
            helper.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
            fields, _ = helper.get_form("/WorkOrders/New")
            payload = self.payload(7)
            payload.update({key: fields[key] for key in ("__CompanyKey", "__RequestVerificationToken", "CreationToken")})
            ready.wait(timeout=15)
            return helper.request("/WorkOrders/CreateOrder", payload)[2]
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            urls = list(pool.map(confirm, range(2)))
        self.assertEqual(sum(url.endswith("/WorkOrders") for url in urls), 1)
        self.assertEqual(sum(x["Quantity"] for x in self.holds()), 7)
        self.assertEqual(self.stock()["OnHand"], 10)

    def test_ship_and_hold_failures_roll_back_entire_transaction(self):
        self.configure(10)
        before = runner.rows("WorkOrders")
        runner.sql('''CREATE FUNCTION hold_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Injected hold failure'; END $$;
            CREATE TRIGGER hold_fail BEFORE INSERT ON "StockReservations" FOR EACH ROW EXECUTE FUNCTION hold_fail();''')
        try:
            self.post("/WorkOrders/CreateOrder", self.payload(5), "/WorkOrders/New")
            self.assertEqual(runner.rows("WorkOrders"), before)
            self.assertEqual(self.holds(), [])
        finally: runner.sql('DROP TRIGGER hold_fail ON "StockReservations"; DROP FUNCTION hold_fail();')
        order = self.order(5)
        runner.sql('''CREATE FUNCTION ship_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Kind"='Shipment' THEN RAISE EXCEPTION 'Injected shipment failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER ship_fail BEFORE INSERT ON "StockMovements" FOR EACH ROW EXECUTE FUNCTION ship_fail();''')
        try:
            self.action(order, "Ship")
            self.assertEqual(self.stock()["OnHand"], 10)
            self.assertEqual(self.holds(order)[0]["Quantity"], 5)
            self.assertEqual(runner.rows("WorkOrders", f'"Id"={order}')[0]["FulfillmentStatus"], "Reserved")
        finally: runner.sql('DROP TRIGGER ship_fail ON "StockMovements"; DROP FUNCTION ship_fail();')

    def test_historical_mixed_tracking_metadata_never_issues_stock_again(self):
        self.configure(8)
        other_sku = "HISTORY-" + uuid.uuid4().hex[:10].upper()
        self.post("/Products/Create", {"Sku": other_sku, "Name": "Historical B", "UnitOfMeasure": "each", "IsActive": "true"})
        other = runner.rows("Products", f'"Sku"=\'{other_sku}\'')[0]["Id"]
        order = int(runner.sql(f'''INSERT INTO "WorkOrders" ("CompanyKey","Title","ProductType","Quantity","DueDate","Status","CustomerId","DeliveryType","DeliveryAddressSnapshot","DeliveryInstructions","FulfillmentStatus")
            VALUES ('fti-demo','Old completed','Hold test product',5,'{self.date(10)}','Completed',701,'Company','1 Inventory Road','','Historical') RETURNING "Id";''').splitlines()[0])
        runner.sql(f'''INSERT INTO "WorkOrderItems" ("WorkOrderId","ProductId","ProductType","Quantity","ProductSkuSnapshot","UnitOfMeasureSnapshot") VALUES
            ({order},{self.product_id},'Hold test product',2,'{self.sku}','each'),({order},{other},'Historical B',3,'{other_sku}','each');
            INSERT INTO "StockMovements" ("CompanyKey","ProductId","Quantity","Kind","Reason","CreatedAt","WorkOrderId")
            VALUES ('fti-demo',{self.product_id},-2,'Order issue','Historical issue',now(),{order});''')
        self.configure(5, product_id=other)
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order}")
        values = dict(fields, **self.payload(2), **{"Items[1].ProductId": other, "DeliveryInstructions": "Metadata only"})
        _, body, url = self.post(f"/WorkOrders/Edit/{order}", values)
        self.assertTrue(url.endswith("/WorkOrders"), body[:800])
        self.assertEqual((self.stock()["OnHand"], self.stock(other)["OnHand"]), (8, 5))
        self.status(order, "Pending")
        self.assertEqual(self.stock(other)["OnHand"], 5)

    def test_shipped_lines_are_fixed_but_notes_can_be_updated(self):
        self.configure(10)
        order = self.order(5)
        self.action(order, "Ship")
        _, body, _ = self.edit(order, 6)
        self.assertIn("product lines and delivery dates are fixed", body)
        _, body, url = self.edit(order, 5, extra={"DeliveryInstructions": "Updated note"})
        self.assertTrue(url.endswith("/WorkOrders"), body[:500])
        self.assertEqual(self.stock()["OnHand"], 5)

    def test_projection_one_and_three_calendar_months_and_numeric_series(self):
        self.configure(10)
        self.schedule(10, 5)
        self.order(6, 10)
        one, three = self.projection(1), self.projection(3)
        self.assertGreaterEqual(len(one["days"]), 29)
        self.assertLessEqual(len(one["days"]), 32)
        self.assertGreater(len(three["days"]), 85)
        self.assertEqual(one["days"][0]["actual"], 10)
        self.assertEqual(one["days"][5]["withIncoming"], 20)
        self.assertEqual(one["days"][10]["available"], 14)
        self.assertTrue(all(x["actual"] == 10 for x in three["days"]))
        self.assertEqual(self.request(f"/Dashboard/Projection?productId={self.product_id}&months=2")[0], 400)
        self.assertEqual(self.request("/Dashboard/Projection?productId=999999&months=1")[0], 404)

    def test_untracked_projection_is_unknown_not_fake_zero_and_forms_are_protected(self):
        projection = self.projection()
        self.assertIsNone(projection["onHand"])
        self.assertEqual(projection["days"], [])
        self.assertEqual(self.request("/WorkOrders/Ship", {"id": 1, "version": 1})[0], 400)
        self.assertEqual(self.request(f"/Inventory/Configure/{self.product_id}", {"CountedQuantity": 100})[0], 400)

    def test_company_isolation_for_holds_dispatch_projection_and_stale_forms(self):
        self.configure(10)
        order = self.order(5)
        fields, _ = self.get_form(f"/WorkOrders/Edit/{order}")
        self.post("/Companies/Select", {"key": "blank-company"}, "/Companies")
        self.assertEqual(self.request(f"/Dashboard/Projection?productId={self.product_id}")[0], 404)
        self.assertEqual(self.post("/WorkOrders/Ship", {"id": order, "version": fields["Version"]}, "/WorkOrders/New")[0], 404)
        _, _, url = self.post("/WorkOrders/Ship", {"id": order, "version": fields["Version"], "__CompanyKey": "fti-demo"}, "/WorkOrders/New")
        self.assertTrue(url.endswith("/Companies"))
        self.assertEqual(self.stock()["OnHand"], 10)
        self.assertEqual(self.holds(order)[0]["Quantity"], 5)

    def test_upgrade_preserves_valid_existing_commitments_and_never_issues_history(self):
        script = (runner.ROOT / "FactoryOps.Web/Data/Sql/008-stock-reservations.sql").read_text(encoding="utf-8")
        runner.sql('''CREATE SCHEMA holds_upgrade;
            CREATE TABLE holds_upgrade."SchemaUpdates" ("Name" text PRIMARY KEY);
            CREATE TABLE holds_upgrade."CompanyProfiles" ("Key" varchar(64) PRIMARY KEY);
            INSERT INTO holds_upgrade."CompanyProfiles" VALUES ('fti-demo');
            CREATE TABLE holds_upgrade."Products" ("Id" integer PRIMARY KEY,"CompanyKey" varchar(64),"UnitOfMeasure" varchar(32));
            INSERT INTO holds_upgrade."Products" VALUES (1,'fti-demo','each');
            CREATE TABLE holds_upgrade."WorkOrders" ("Id" integer PRIMARY KEY,"CompanyKey" varchar(64),"Status" text,"DueDate" date);
            INSERT INTO holds_upgrade."WorkOrders" VALUES (1,'fti-demo','Completed',CURRENT_DATE),(2,'fti-demo','Pending',CURRENT_DATE),(3,'fti-demo','Pending',CURRENT_DATE);
            CREATE TABLE holds_upgrade."WorkOrderItems" ("WorkOrderId" integer,"ProductId" integer,"Quantity" integer,"UnitOfMeasureSnapshot" text);
            INSERT INTO holds_upgrade."WorkOrderItems" VALUES (1,1,3,'each'),(2,1,4,'each'),(3,1,5,'');''')
        try:
            runner.sql('BEGIN;SET LOCAL search_path=holds_upgrade;' + script + script + 'COMMIT;')
            self.assertEqual(runner.sql('SELECT "FulfillmentStatus" FROM holds_upgrade."WorkOrders" WHERE "Id"=1;'), "Historical")
            self.assertEqual(runner.sql('SELECT "FulfillmentStatus" FROM holds_upgrade."WorkOrders" WHERE "Id"=3;'), "Unconfirmed")
            self.assertEqual(runner.sql('SELECT sum("Quantity") FROM holds_upgrade."StockReservations";'), "4")
        finally: runner.sql('DROP SCHEMA holds_upgrade CASCADE;')
