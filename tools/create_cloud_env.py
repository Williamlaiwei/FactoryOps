"""Create cloud-only configuration with new random credentials; never print them."""
import argparse
from pathlib import Path
import re
import secrets

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--site", required=True, help="DNS name pointing to the cloud server")
parser.add_argument("--output", type=Path, default=Path(".env.cloud"))
parser.add_argument("--http", action="store_true", help="Use explicit HTTP for an IP-address demo")
args = parser.parse_args()
if not re.fullmatch(r"(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?", args.site): parser.error("Use a domain name without a scheme, path or port.")
with args.output.open("x", encoding="utf-8", newline="\n") as destination:
    address = "http://" + args.site if args.http else args.site
    destination.write(f"SITE_ADDRESS={address}\nREQUIRE_HTTPS={'false' if args.http else 'true'}\nRELEASE_TAG=demo\nPOSTGRES_PASSWORD={secrets.token_hex(32)}\nKNOWLEDGE_POSTGRES_PASSWORD={secrets.token_hex(32)}\nFACTORYOPS_AI_KEY={secrets.token_hex(32)}\nOPENAI_API_KEY=\n")
try: args.output.chmod(0o600)
except OSError: pass
print("Created", args.output, "with new random credentials. Existing files are never overwritten.")
