"""Read-only checks against the actual demo through its VM-local Caddy listener."""
import base64
import json
import re
from pathlib import Path
import subprocess
import urllib.request

BASE = "http://127.0.0.1"
HEADERS = {"Host": "45.79.238.119"}
root = Path("/opt/factoryops/current")
settings = dict(line.split("=", 1) for line in (root / ".env.cloud").read_text().splitlines()
                if "=" in line and not line.lstrip().startswith("#"))
if settings.get("SITE_ADDRESS", "").startswith("https://"):
    values = dict(line.split(": ", 1) for line in Path("/opt/factoryops/demo-access.txt").read_text().splitlines())
    assert values["URL"] == settings["SITE_ADDRESS"]
    BASE = values["URL"]
    HEADERS["Authorization"] = "Basic " + base64.b64encode((values["Username"] + ":" + values["Password"]).encode()).decode()
routes = ["/healthz", "/Dashboard", "/WorkOrders", "/WorkOrders/New", "/Products",
          "/Products/Create", "/Products/Import", "/Products/CsvTemplate", "/Customers",
          "/Customers/CsvTemplate", "/Inventory", "/Inventory/BulkCount", "/Inventory/ImportCsv",
          "/Inventory/StockCsvTemplate", "/Inventory/ExportStockCsv", "/Inventory/Calendar",
          "/Inventory/Replenishments", "/Inventory/Schedule", "/Companies", "/Companies/Create",
          "/Settings", "/Knowledge", "/Products/Lookup?q=DEMO", "/Customers/Search?q=04412"]
for route in routes:
    with urllib.request.urlopen(urllib.request.Request(BASE + route, headers=HEADERS), timeout=30) as response:
        body = response.read()
        assert response.status == 200 and body, route
        if route == "/Dashboard":
            control = re.search(r'<select\b[^>]*id="stockProductSelect"[^>]*>(.*?)</select>', body.decode(), re.S)
            product_id = int(re.search(r'<option\b[^>]*value="(\d+)"', control.group(1)).group(1))
        print("PASS actual demo " + route + ": HTTP 200", flush=True)

for months in (1, 3):
    route = "/Dashboard/Projection?productId=" + str(product_id) + "&months=" + str(months)
    with urllib.request.urlopen(urllib.request.Request(BASE + route, headers=HEADERS), timeout=30) as response:
        data = json.load(response)
        assert "onHand" in data and "days" in data
        print("PASS actual demo: " + str(months) + "-month dashboard projection", flush=True)

inner = r'''
import hashlib,json,os,urllib.request
headers={'X-FactoryOps-Key':os.environ['FACTORYOPS_AI_KEY'],'X-FactoryOps-Company':'fti-demo'}
def get(url):
    return urllib.request.urlopen(urllib.request.Request(url,headers=headers),timeout=30)
with get(os.environ['CATALOG_API_URL']) as response:
    catalog=json.load(response)
    assert catalog['companyKey']=='fti-demo'
    print('PASS actual demo: private SQL callback, '+str(len(catalog['products']))+' products',flush=True)
with get('http://localhost:8000/v1/documents') as response:
    documents=json.load(response)['documents']
    print('PASS actual demo: knowledge listing, '+str(len(documents))+' documents',flush=True)
for document in documents:
    with get('http://localhost:8000/v1/documents/'+document['id']+'/original') as response:
        original=response.read()
        assert original
        assert hashlib.sha256(original).hexdigest()==document['sha256']
print('PASS actual demo: all knowledge original-file checksums match',flush=True)
request=urllib.request.Request('http://localhost:8000/v1/retrieve',data=json.dumps({'query':'Fast Tread'}).encode(),headers=dict(headers,**{'Content-Type':'application/json'}))
with urllib.request.urlopen(request,timeout=60) as response:
    data=json.load(response)
    assert data['requires_review'] is True and data['citations']
    print('PASS actual demo: retrieval, '+str(len(data['citations']))+' citations; requires review',flush=True)
'''
dc = ["docker", "compose", "-p", "factoryops-demo", "--env-file", ".env.cloud",
      "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml"]
subprocess.run(dc + ["exec", "-T", "ai", "python", "-"], cwd=root, input=inner, text=True, check=True)
subprocess.run(dc + ["exec", "-T", "web", "sh", "-c", 'if [ -n "$OPENAI_API_KEY" ]; then echo "Cloud OpenAI key: configured, API access not tested"; else echo "Cloud OpenAI key: empty"; fi'],
               cwd=root, stdin=subprocess.DEVNULL, check=True)
