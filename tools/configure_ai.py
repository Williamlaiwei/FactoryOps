"""Generate local service credentials without printing or replacing existing secrets."""
from pathlib import Path
import re
import secrets

path = Path(__file__).resolve().parents[1] / ".env"
text = path.read_text(encoding="utf-8-sig") if path.exists() else ""
changed = 0
for key in ("FACTORYOPS_AI_KEY", "KNOWLEDGE_POSTGRES_PASSWORD"):
    match = re.search(r"^" + key + r"=(.*)$", text, re.MULTILINE)
    if match and match.group(1).strip() and not match.group(1).strip().startswith("replace-"):
        continue
    line = key + "=" + secrets.token_urlsafe(48)
    if match:
        text = text[:match.start()] + line + text[match.end():]
    else:
        text = text.rstrip() + "\n" + line + "\n"
    changed += 1
if changed:
    path.write_text(text, encoding="utf-8")
print(f"Configured {changed} local service credential(s); values were not printed.")
