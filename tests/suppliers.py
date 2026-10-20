"""Supplier/default and visible stock-risk tests, plus the inventory regression suite."""
import json
import re
import uuid
import __main__ as runner
from inventory import InventoryTests


class SupplierTests(InventoryTests):
    def supplier(self, suffix, product_id=None, default=False):
        path = "/Suppliers/Create" + (f"?productId={product_id}" if product_id else "")
        fields, _ = self.get_form(path)
        values = dict(fields, Name=self.sku + suffix, IsActive="true", MakeDefault=str(default).lower())
        if product_id: values["ProductId"] = product_id
        _, body, url = self.post("/Suppliers/Create", values)
        self.assertNotIn("validation-summary-errors", body)
        supplier = runner.rows("Suppliers", f'"Name"=\'{self.sku + suffix}\'')[0]
        return supplier, values

    def links(self, product_id=None):
        return json.loads(
            runner.sql(f'SELECT coalesce(json_agg(t),\'[]\') FROM "ProductSuppliers" t WHERE "ProductId"={product_id or self.product_id};'))

    def add_other(self, sku):
        sku = sku.upper()
        self.post("/Products/Create", {"Sku": sku, "Name": "Other supplier product", "UnitOfMeasure": "each", "IsActive": "true"})
        return runner.rows("Products", f'"Sku"=\'{sku}\'')[0]["Id"]

    def default(self, supplier_id):
        version = runner.sql(f'SELECT xmin::text FROM "ProductSuppliers" WHERE "ProductId"={self.product_id} AND "SupplierId"={supplier_id};')
        return self.post("/Suppliers/SetDefault", {"productId": self.product_id, "supplierId": supplier_id, "version": version}, f"/Suppliers/Product/{self.product_id}")

    def managed_schedule(self, supplier_id, product_id=None):
        fields, _ = self.get_form("/Inventory/Schedule")
        return self.post("/Inventory/Schedule", dict(fields, ProductId=product_id or self.product_id,
            SupplierId=supplier_id, Supplier="Untrusted posted name", Quantity=5, ExpectedDate=self.date(3)))

    def test_supplier_many_per_product_single_default_and_independent_products(self):
        a, values = self.supplier("-A", self.product_id, True)
        b, _ = self.supplier("-B", self.product_id)
        self.assertEqual(len(self.links()), 2)
        self.default(b["Id"])
        self.assertEqual([x["SupplierId"] for x in self.links() if x["IsDefault"]], [b["Id"]])
        other = self.add_other("OTHER-" + uuid.uuid4().hex[:10])
        self.post("/Suppliers/Assign", {"productId": other, "supplierId": a["Id"], "makeDefault": "true"}, "/Suppliers/Create")
        self.assertEqual([x["SupplierId"] for x in self.links(other) if x["IsDefault"]], [a["Id"]])
        self.post("/Suppliers/Create", values)
        self.post("/Suppliers/Assign", {"productId": self.product_id, "supplierId": b["Id"]}, "/Suppliers/Create")
        self.assertEqual(len(self.links()), 2)

    def test_supplier_schedule_prefill_snapshot_alternative_and_default_change(self):
        self.configure(6)
        a, _ = self.supplier("-A", self.product_id, True)
        b, _ = self.supplier("-B", self.product_id)
        _, body = self.get_form(f"/Inventory/Schedule?productId={self.product_id}")
        self.assertRegex(body, f'<option[^>]*selected="selected"[^>]*value="{a["Id"]}"|<option[^>]*value="{a["Id"]}"[^>]*selected="selected"')
        self.managed_schedule(a["Id"])
        receipt = runner.rows("Replenishments", f'"ProductId"={self.product_id}')[-1]
        self.assertEqual(receipt["SupplierId"], a["Id"]); self.assertEqual(receipt["Supplier"], a["Name"])
        self.default(b["Id"])
        self.assertEqual(runner.rows("Replenishments", f'"Id"={receipt["Id"]}')[0], receipt)
        self.managed_schedule(b["Id"])
        self.assertEqual(runner.rows("Replenishments", f'"ProductId"={self.product_id}')[-1]["SupplierId"], b["Id"])

    def test_supplier_rename_and_deactivation_preserve_recorded_schedule(self):
        self.configure(6)
        a, _ = self.supplier("-A", self.product_id, True)
        self.managed_schedule(a["Id"])
        receipt = runner.rows("Replenishments", f'"ProductId"={self.product_id}')[-1]
        fields, _ = self.get_form(f'/Suppliers/Edit/{a["Id"]}')
        self.post(f'/Suppliers/Edit/{a["Id"]}', dict(fields, Name=a["Name"] + " renamed", IsActive="false"))
        self.assertFalse(any(x["IsDefault"] for x in self.links()))
        choices = json.loads(self.request(f"/Suppliers/ForProduct?productId={self.product_id}")[1])
        self.assertEqual(choices["suppliers"], [])
        fields, _ = self.get_form(f'/Inventory/EditReplenishment/{receipt["Id"]}')
        self.post(f'/Inventory/EditReplenishment/{receipt["Id"]}', dict(fields, ProductId=self.product_id,
            SupplierId=a["Id"], Quantity=7, ExpectedDate=self.date(4), Notes="Updated date"))
        current = runner.rows("Replenishments", f'"Id"={receipt["Id"]}')[0]
        self.assertEqual(current["Supplier"], a["Name"]); self.assertEqual(current["Quantity"], 7)
        count = len(runner.rows("Replenishments"))
        _, body, _ = self.managed_schedule(a["Id"])
        self.assertIn("active supplier linked to this product", body)
        self.assertEqual(len(runner.rows("Replenishments")), count)

    def test_supplier_wrong_product_and_company_are_rejected(self):
        self.configure(6)
        a, _ = self.supplier("-A", self.product_id, True)
        other = self.add_other("OTHER-" + uuid.uuid4().hex[:10])
        runner.stock_fixture(other)
        _, body, _ = self.managed_schedule(a["Id"], other)
        self.assertIn("active supplier linked to this product", body)
        before = runner.rows("Suppliers")
        self.post("/Companies/Select", {"key": "blank-company"}, "/Companies")
        self.assertEqual(self.request(f'/Suppliers/Edit/{a["Id"]}')[0], 404)
        self.assertEqual(self.request(f"/Suppliers/ForProduct?productId={self.product_id}")[0], 404)
        self.assertEqual(self.post("/Suppliers/Assign", {"productId": self.product_id, "supplierId": a["Id"]}, "/Suppliers/Create")[0], 404)
        self.assertEqual(runner.rows("Suppliers"), before)

    def test_supplier_stale_edit_duplicate_names_and_csrf(self):
        a, _ = self.supplier("-A")
        fields, _ = self.get_form(f'/Suppliers/Edit/{a["Id"]}')
        values = dict(fields, Name=a["Name"] + " updated", IsActive="true")
        self.post(f'/Suppliers/Edit/{a["Id"]}', values)
        _, body, _ = self.post(f'/Suppliers/Edit/{a["Id"]}', dict(values, Name="Stale overwrite"))
        self.assertIn("supplier changed", body)
        self.assertEqual(runner.rows("Suppliers", f'"Id"={a["Id"]}')[0]["Name"], values["Name"])
        fields, _ = self.get_form("/Suppliers/Create")
        _, body, _ = self.post("/Suppliers/Create", dict(fields, Name=values["Name"].lower(), IsActive="true"))
        self.assertIn("already exists", body)
        self.assertEqual(self.request("/Suppliers/Create", {"Name": "No token"})[0], 400)

    def test_supplier_default_change_is_atomic_on_failure_and_stale_link(self):
        a, _ = self.supplier("-A", self.product_id, True)
        b, _ = self.supplier("-B", self.product_id)
        before = self.links()
        runner.sql('''CREATE FUNCTION fail_supplier_default() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."IsDefault" THEN RAISE EXCEPTION 'Injected default failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_supplier_default BEFORE UPDATE ON "ProductSuppliers" FOR EACH ROW EXECUTE FUNCTION fail_supplier_default();''')
        try: self.default(b["Id"])
        finally: runner.sql('DROP TRIGGER fail_supplier_default ON "ProductSuppliers"; DROP FUNCTION fail_supplier_default();')
        self.assertEqual(self.links(), before)
        stale = runner.sql(f'SELECT xmin::text FROM "ProductSuppliers" WHERE "ProductId"={self.product_id} AND "SupplierId"={a["Id"]};')
        self.default(b["Id"])
        self.post("/Suppliers/SetDefault", {"productId": self.product_id, "supplierId": a["Id"], "version": stale}, "/Suppliers/Create")
        self.assertEqual([x["SupplierId"] for x in self.links() if x["IsDefault"]], [b["Id"]])

    def test_supplier_legacy_names_and_repeat_upgrade_are_preserved(self):
        self.configure(6)
        fields, _ = self.get_form("/Inventory/Schedule")
        self.post("/Inventory/Schedule", dict(fields, ProductId=self.product_id, Supplier="Legacy manual supplier", Quantity=5, ExpectedDate=self.date(3)))
        before = runner.rows("Replenishments", f'"ProductId"={self.product_id}')
        script = (runner.ROOT / "FactoryOps.Web/Data/Sql/009-product-suppliers.sql").read_text(encoding="utf-8")
        runner.sql("BEGIN;" + script + script + "COMMIT;")
        self.assertEqual(runner.rows("Replenishments", f'"ProductId"={self.product_id}'), before)
        self.assertIsNone(before[0]["SupplierId"])

    def test_supplier_creation_and_default_roll_back_together(self):
        a, _ = self.supplier("-A", self.product_id, True)
        before = self.links(); count = len(runner.rows("Suppliers"))
        runner.sql('''CREATE FUNCTION fail_supplier_link() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected supplier link failure'; END $$;
            CREATE TRIGGER fail_supplier_link BEFORE INSERT ON "ProductSuppliers" FOR EACH ROW EXECUTE FUNCTION fail_supplier_link();''')
        try:
            fields, _ = self.get_form(f"/Suppliers/Create?productId={self.product_id}")
            _, body, _ = self.post("/Suppliers/Create", dict(fields, Name=self.sku+"-B", IsActive="true", MakeDefault="true"))
            self.assertIn("No partial changes", body)
        finally: runner.sql('DROP TRIGGER fail_supplier_link ON "ProductSuppliers"; DROP FUNCTION fail_supplier_link();')
        self.assertEqual(len(runner.rows("Suppliers")), count); self.assertEqual(self.links(), before)

    def test_dashboard_risk_includes_dependency_and_native_fallback_labels(self):
        self.configure(6); self.schedule(5, 3); self.order(8, 10)
        projection = self.projection()
        self.assertTrue(projection["stockRisk"]); self.assertEqual(projection["stockAlert"], "Depends on incoming stock")
        body = self.request("/Dashboard")[1]
        option = re.search(fr'<option\b[^>]*value="{self.product_id}"[^>]*>(.*?)</option>', body, re.S)
        self.assertIn("Depends on incoming stock", option.group(0)); self.assertIn('data-risk="true"', option.group(0))
        self.assertIn('id="stockProductButton"', body)
        self.receipt(runner.rows("Replenishments", f'"ProductId"={self.product_id}')[-1]["Id"])
        self.assertFalse(self.projection()["stockRisk"])
