#!/usr/bin/env python3
"""One-time helper: performs the Spotify Authorization Code + PKCE flow
interactively (opens your browser, you log in as thebluesland) and prints
the resulting refresh token.

This is the "Mehmet's one-time, out-of-tool step" referenced in
docs/business-technical-specification.md section 18.4 and
tools/spotify-playlist-fetcher's SpotifyAuthClient docstring — the sync
tool itself only ever performs the refresh-token exchange, never this
interactive authorization step.

Usage:
    python3 scripts/spotify-get-refresh-token.py <CLIENT_ID>

Requires no third-party packages (stdlib only). Nothing is written to disk;
the refresh token is printed once, to your terminal, for you to copy
immediately into the SPOTIFY_REFRESH_TOKEN GitHub secret and nowhere else.
"""

import base64
import hashlib
import http.server
import json
import secrets
import sys
import threading
import urllib.parse
import urllib.request
import webbrowser
from typing import Optional

REDIRECT_URI = "http://127.0.0.1:8888/callback"
AUTHORIZE_URL = "https://accounts.spotify.com/authorize"
TOKEN_URL = "https://accounts.spotify.com/api/token"

# playlist-read-private is required by GET /playlists/{id}/items as of the
# Spotify February 2026 Web API changes (the old, unscoped /tracks endpoint
# was removed). The "Get Playlist" summary endpoint itself needs no scope;
# only the paginated items/track-artists fetch does.
SCOPES = "playlist-read-private"


def b64url(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode("ascii")


class _CallbackResult:
    code: Optional[str] = None
    state: Optional[str] = None
    error: Optional[str] = None


class _CallbackHandler(http.server.BaseHTTPRequestHandler):
    result: _CallbackResult
    expected_state: str

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != "/callback":
            self.send_response(404)
            self.end_headers()
            return

        params = urllib.parse.parse_qs(parsed.query)
        self.result.code = params.get("code", [None])[0]
        self.result.state = params.get("state", [None])[0]
        self.result.error = params.get("error", [None])[0]

        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        if self.result.error:
            self.wfile.write(f"Authorization failed: {self.result.error}. You can close this tab.".encode())
        else:
            self.wfile.write(b"Authorized. You can close this tab and return to the terminal.")

    def log_message(self, *_args):
        pass  # keep stdout clean; nothing here is sensitive anyway (only path/query, and code is single-use)


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: python3 scripts/spotify-get-refresh-token.py <CLIENT_ID>", file=sys.stderr)
        return 2
    client_id = sys.argv[1]

    code_verifier = b64url(secrets.token_bytes(64))
    code_challenge = b64url(hashlib.sha256(code_verifier.encode("ascii")).digest())
    state = b64url(secrets.token_bytes(16))

    result = _CallbackResult()
    handler = type("Handler", (_CallbackHandler,), {"result": result, "expected_state": state})
    server = http.server.HTTPServer(("127.0.0.1", 8888), handler)
    server_thread = threading.Thread(target=server.handle_request, daemon=True)
    server_thread.start()

    authorize_params = {
        "client_id": client_id,
        "response_type": "code",
        "redirect_uri": REDIRECT_URI,
        "code_challenge_method": "S256",
        "code_challenge": code_challenge,
        "state": state,
    }
    if SCOPES:
        authorize_params["scope"] = SCOPES
    authorize_url = f"{AUTHORIZE_URL}?{urllib.parse.urlencode(authorize_params)}"

    print(f"Redirect URI must be registered on the Spotify app exactly as: {REDIRECT_URI}")
    print("Opening your browser for you to log in as the thebluesland Spotify account...")
    webbrowser.open(authorize_url)

    server_thread.join(timeout=180)
    if result.code is None:
        print(f"No callback received (or Spotify returned an error: {result.error}). Aborting.", file=sys.stderr)
        return 1
    if result.state != state:
        print("State mismatch on callback — aborting for safety (possible CSRF).", file=sys.stderr)
        return 1

    token_request_body = urllib.parse.urlencode({
        "grant_type": "authorization_code",
        "code": result.code,
        "redirect_uri": REDIRECT_URI,
        "client_id": client_id,
        "code_verifier": code_verifier,
    }).encode("ascii")

    request = urllib.request.Request(
        TOKEN_URL,
        data=token_request_body,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    with urllib.request.urlopen(request) as response:
        payload = json.load(response)

    refresh_token = payload.get("refresh_token")
    if not refresh_token:
        print(f"Token response did not include a refresh_token: {payload}", file=sys.stderr)
        return 1

    print()
    print("Success. Copy this refresh token into the SPOTIFY_REFRESH_TOKEN GitHub secret now —")
    print("it is not saved anywhere by this script and will not be shown again:")
    print()
    print(refresh_token)
    print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
