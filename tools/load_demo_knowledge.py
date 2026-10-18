"""Load sourced demo reference data into the FTI profile through the normal MVC upload boundary."""
import html
import http.cookiejar
from pathlib import Path
import re
import sys
import urllib.request
import uuid

root = Path(__file__).resolve().parents[1]
base = "http://127.0.0.1:8080"
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def upload(path, source):
    page = client.open(base + "/Knowledge", timeout=30).read().decode()
    match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page)
    if not match:
        raise RuntimeError("Knowledge upload form is unavailable")
    token = html.unescape(match.group(1))
    boundary = "demo-" + uuid.uuid4().hex
    fields = {"__RequestVerificationToken": token, "__CompanyKey": "fti-demo", "sourceUrl": source}
    data = b"".join((f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n').encode() for key, value in fields.items())
    data += (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()
    data += path.read_bytes() + f'\r\n--{boundary}--\r\n'.encode()
    request = urllib.request.Request(base + "/Knowledge/Upload", data=data, headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
    response = client.open(request, timeout=180).read().decode()
    if "Document saved and indexed." not in response and "already indexed for this company" not in response:
        raise RuntimeError("Demo reference upload failed; inspect the Knowledge page")
    print(f"Indexed demo reference: {path.name}")


upload(root / "demo/fti-group/knowledge.csv", "https://ftigroup.com.au/products/")
if "--with-brochure" in sys.argv:
    source = "https://ftigroup.com.au/wp-content/uploads/2021/07/Product-Brochure-2021_REV4_spreads.pdf"
    with urllib.request.urlopen(source, timeout=45) as response:
        content = response.read(20 * 1024 * 1024 + 1)
    if not content.startswith(b"%PDF-") or len(content) > 20 * 1024 * 1024:
        raise RuntimeError("The public brochure is not a supported PDF")
    path = root / "artifacts/fti-product-brochure-2021.pdf"
    path.parent.mkdir(exist_ok=True)
    path.write_bytes(content)
    upload(path, source)
    print("The 2021 brochure is historical reference material, not current availability or engineering approval.")
