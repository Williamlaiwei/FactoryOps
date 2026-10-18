"""Settings persistence and boundary checks against the runner's temporary DB."""
import __main__ as runner
import http.cookiejar
import json
import urllib.request
import unittest


class SettingsTests(unittest.TestCase):
    request = runner.OrderEditingTests.request
    get_form = runner.OrderEditingTests.get_form

    def setUp(self):
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def select(self, key):
        fields, _ = self.get_form("/Companies")
        return self.request("/Companies/Select", dict(fields, key=key))

    def state(self):
        return json.loads(runner.sql('''SELECT json_agg(t ORDER BY t."CompanyKey") FROM
            (SELECT *, xmin::text AS version FROM "AiWorkflowSettings") t;'''))

    def values(self):
        fields, _ = self.get_form("/Settings")
        state = next(x for x in self.state() if x["CompanyKey"] == fields["__CompanyKey"])
        values = {key: value for key, value in state.items() if key not in ("CompanyKey", "version")}
        values.update({key: fields[key] for key in ("Version", "__CompanyKey", "__RequestVerificationToken")})
        return values

    def test_a_defaults_and_no_key_disclosure(self):
        fields, body = self.get_form("/Settings")
        self.assertEqual(len(self.state()), 2)
        self.assertTrue(all(x["IntakeMode"] == "ReviewBeforeImport" for x in self.state()))
        self.assertIn("Configured (not tested)", body)
        self.assertNotIn("test-key-not-a-real-openai-credential", body)
        self.assertIn("https://api.openai.com/v1", body)
        self.assertIn("still uses its local embedding index", body)
        self.assertEqual(fields["__CompanyKey"], "fti-demo")

    def test_b_save_reload_and_ignored_identity_overposting(self):
        values = self.values()
        values.update(OrderModel=" sample-order-model ", TranscriptionModel="sample-speech-model",
                      EmbeddingModel="sample-embedding-model", IntakeMode="CreateAiDraft",
                      UsePhoneRecords="false", UseEmailOrders="true", MailboxAddress="orders@example.com",
                      ReplyFromAddress="sales@example.com", ReplyChannel="Both", ReplyLanguage="zh-TW",
                      ReplySignature="測試簽名 <script>alert(1)</script>", MissingInfoInstructions="請確認交貨日期與數量",
                      CompanyKey="blank-company", Provider="Untrusted", AutoSend="true")
        status, body, url = self.request("/Settings", values)
        self.assertEqual(status, 200, body[:400])
        self.assertTrue(url.endswith("/Settings"))
        self.assertIn("Settings saved for this company", body)
        self.assertNotIn("<script>alert(1)</script>", body)
        state = next(x for x in self.state() if x["CompanyKey"] == "fti-demo")
        self.assertEqual(state["OrderModel"], "sample-order-model")
        self.assertEqual(state["ReplySignature"], values["ReplySignature"])
        self.assertFalse(state["UsePhoneRecords"])
        self.assertTrue(state["UseEmailOrders"])
        blank = next(x for x in self.state() if x["CompanyKey"] == "blank-company")
        self.assertEqual(blank["OrderModel"], "")
        self.assertEqual(state["IntakeMode"], "CreateAiDraft")
        self.assertEqual(int(runner.sql('SELECT count(*) FROM "WorkOrders";')), 0)

    def test_c_invalid_preferences_preserve_saved_settings(self):
        for changes in ({"IntakeMode": "AutoConfirm"}, {"IntakeMode": ""}, {"ReplyChannel": "Unknown"},
                        {"ReplyLanguage": "unknown"}, {"MailboxAddress": "not-an-email"},
                        {"ReplyFromAddress": "not-an-email"}, {"OrderModel": "x" * 101},
                        {"MissingInfoInstructions": "x" * 2001}):
            with self.subTest(changes=changes):
                before = self.state()
                values = self.values()
                values.update(changes)
                status, body, _ = self.request("/Settings", values)
                self.assertEqual(status, 200)
                self.assertIn("validation-summary-errors", body)
                self.assertEqual(self.state(), before)

    def test_d_concurrent_and_repeated_forms_cannot_overwrite(self):
        stale = self.values()
        fresh = self.values()
        fresh["OrderModel"] = "updated-in-other-tab"
        self.request("/Settings", fresh)
        saved = self.state()
        stale["OrderModel"] = "must-not-overwrite"
        _, body, _ = self.request("/Settings", stale)
        self.assertIn("Settings changed in another tab", body)
        self.assertEqual(self.state(), saved)
        _, body, _ = self.request("/Settings", fresh)
        self.assertIn("Settings changed in another tab", body)
        self.assertEqual(self.state(), saved)

    def test_e_company_switch_and_stale_tab(self):
        stale = self.values()
        before = self.state()
        self.select("blank-company")
        values = self.values()
        self.assertEqual(values["__CompanyKey"], "blank-company")
        self.assertEqual(values["OrderModel"], "")
        _, body, url = self.request("/Settings", stale)
        self.assertTrue(url.endswith("/Companies"))
        self.assertIn("active company changed", body)
        self.assertEqual(self.state(), before)
        values["ReplySignature"] = "Blank company only"
        self.request("/Settings", values)
        self.select("fti-demo")
        self.assertNotEqual(self.values()["ReplySignature"], "Blank company only")

    def test_f_csrf_missing_company_and_repeat_schema(self):
        before = self.state()
        values = self.values()
        without_token = dict(values)
        del without_token["__RequestVerificationToken"]
        self.assertEqual(self.request("/Settings", without_token)[0], 400)
        del values["__CompanyKey"]
        self.assertEqual(self.request("/Settings", values)[0], 400)
        self.assertEqual(self.state(), before)
        script = runner.ROOT / "FactoryOps.Web/Data/Sql/005-ai-workflow-settings.sql"
        runner.sql("BEGIN;" + script.read_text(encoding="utf-8") + "COMMIT;")
        self.assertEqual(self.state(), before)
