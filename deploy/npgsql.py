"""Print an Npgsql connection string for a postgresql:// URI (as Neon shows them).

Usage: python3 npgsql.py postgresql://user:password@host/db?sslmode=require
"""
import sys
from urllib.parse import parse_qsl, unquote, urlsplit

SSL_MODES = {"disable": "Disable", "allow": "Allow", "prefer": "Prefer", "require": "Require",
             "verify-ca": "VerifyCA", "verify-full": "VerifyFull"}


def quote(value: str) -> str:
    # Npgsql reads values with ; = or quotes when they are double-quoted, with "" for ".
    return '"' + value.replace('"', '""') + '"' if any(c in value for c in ';="\'') else value


def main() -> None:
    url = urlsplit(sys.argv[1])
    if url.scheme not in ("postgresql", "postgres") or not url.hostname or not url.username:
        sys.exit("expected postgresql://user:password@host/database")
    query = dict(parse_qsl(url.query))
    parts = {
        "Host": url.hostname,
        "Port": str(url.port or 5432),
        "Database": unquote(url.path.lstrip("/")) or "postgres",
        "Username": unquote(url.username),
        "Password": unquote(url.password or ""),
        # Neon requires TLS; default to it when the URI does not say.
        "SSL Mode": SSL_MODES.get(query.get("sslmode", "require"), "Require"),
    }
    print(";".join(f"{key}={quote(value)}" for key, value in parts.items()))


if __name__ == "__main__":
    main()
