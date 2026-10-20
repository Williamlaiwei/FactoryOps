"""Prepare HTTPS and a shared demo password; keep plaintext credentials on the VM."""
import argparse
import getpass
import ipaddress
import os
from pathlib import Path
import re
import secrets
import subprocess
import tempfile


def write_private(path, value):
    descriptor, temporary = tempfile.mkstemp(prefix=".demo-setting-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as output:
            output.write(value)
        os.chmod(temporary, 0o600)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", required=True, help="Public IP address or DNS name, without scheme/port")
    parser.add_argument("--user", default="demo")
    parser.add_argument("--env-file", type=Path, default=Path(".env.cloud"))
    parser.add_argument("--credentials-file", type=Path, default=Path("/opt/factoryops/demo-access.txt"))
    parser.add_argument("--generate", action="store_true", help="Generate a password instead of prompting")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,32}", args.user):
        parser.error("Use 1-32 letters, numbers, underscores or dashes for the username.")
    try:
        address = ipaddress.ip_address(args.site)
        if not address.is_global or address.version != 4:
            parser.error("Use a public IPv4 address or a DNS name.")
    except ValueError:
        if not re.fullmatch(r"(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?", args.site) or "." not in args.site:
            parser.error("Use a DNS name or public IPv4 address without scheme, path or port.")
    target = args.env_file.resolve(strict=True)
    credentials = args.credentials_file.resolve()
    if target == credentials or not credentials.parent.is_dir():
        parser.error("Choose a separate credential file in an existing private server directory.")
    password = secrets.token_urlsafe(24) if args.generate else getpass.getpass("Demo password (hidden, at least 12 characters): ")
    if len(password) < 12 or len(password.encode()) > 72 or any(ch in password for ch in "\r\n\0"):
        parser.error("Password must be 12-72 UTF-8 bytes without line breaks.")
    command = ["docker", "compose", "-p", "factoryops-demo", "--env-file", str(target),
               "-f", "compose.cloud.yaml", "-f", "compose.vm.yaml", "exec", "-T", "proxy", "caddy", "hash-password"]
    result = subprocess.run(command, cwd=target.parent, input=password + "\n", text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    hashed = re.search(r"\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}", result.stdout)
    if result.returncode or not hashed:
        parser.error("Password hashing failed. Existing configuration was preserved.")
    text = target.read_text(encoding="utf-8-sig")
    settings = {"SITE_ADDRESS": "https://" + args.site, "REQUIRE_HTTPS": "true",
                "DEMO_TLS_NAME": args.site,
                "CADDY_CONFIG_FILE": "./deploy/Caddyfile.protected", "DEMO_AUTH_USER": args.user,
                "DEMO_AUTH_HASH": "'" + hashed.group() + "'"}
    for key, value in settings.items():
        pattern = r"^[ \t]*" + key + r"[ \t]*=.*$"
        if re.search(pattern, text, re.MULTILINE):
            text = re.sub(pattern, lambda _: key + "=" + value, text, flags=re.MULTILINE)
        else:
            text = text.rstrip() + "\n" + key + "=" + value + "\n"
    write_private(credentials, "URL: https://" + args.site + "\nUsername: " + args.user + "\nPassword: " + password + "\n")
    write_private(target, text)
    print("HTTPS/demo-access settings prepared. Password value hidden.")
    print("Private credentials file:", credentials)
    print("Open inbound TCP 80/443, then recreate web and proxy to activate. Certificate issuance must still succeed.")


if __name__ == "__main__":
    main()
