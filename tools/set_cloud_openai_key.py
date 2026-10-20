"""Set a server's OpenAI key interactively without printing it or changing other keys."""
import argparse
import getpass
import os
from pathlib import Path
import re
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, default=Path(".env.cloud"))
    args = parser.parse_args()
    target = args.env_file.resolve(strict=True)
    original = target.read_text(encoding="utf-8-sig")
    key = getpass.getpass("OpenAI API key (hidden): ").strip()
    if not re.fullmatch(r"sk-[A-Za-z0-9_-]+", key):
        parser.error("Expected an OpenAI secret key starting with sk-. No settings changed.")
    pattern = r"^[ \t]*OPENAI_API_KEY[ \t]*=.*$"
    if re.search(pattern, original, re.MULTILINE):
        updated = re.sub(pattern, lambda _: "OPENAI_API_KEY=" + key, original, flags=re.MULTILINE)
    else:
        updated = original.rstrip() + "\nOPENAI_API_KEY=" + key + "\n"
    descriptor, temporary = tempfile.mkstemp(prefix=".openai-setting-", dir=target.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as output:
            output.write(updated)
        os.chmod(temporary, 0o600)
        os.replace(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    print("OPENAI_API_KEY saved. Other settings were preserved; key value hidden.")
    print("Recreate the web container to apply it. This does not test OpenAI API access.")


if __name__ == "__main__":
    main()
