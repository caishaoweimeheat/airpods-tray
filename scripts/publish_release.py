"""Publish an already-tested ZIP; never rebuild or replace published assets."""
import hashlib
import io
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen
import zipfile


def checked_path(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError("Release file is outside the repository")
    return path


def load_bundle(root):
    manifest = json.loads((root / "release-manifest.json").read_text(encoding="utf-8"))
    for key, pattern in {
        "repository": r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+",
        "tag": r"v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?",
        "target_commit": r"[0-9a-f]{40}",
        "sha256": r"[0-9a-f]{64}",
    }.items():
        if not re.fullmatch(pattern, manifest[key]):
            raise ValueError("Invalid manifest field: " + key)
    if not isinstance(manifest["prerelease"], bool):
        raise ValueError("prerelease must be a boolean")
    asset = checked_path(root, manifest["asset"])
    data = asset.read_bytes()
    if hashlib.sha256(data).hexdigest() != manifest["sha256"]:
        raise ValueError("Package SHA-256 differs from the tested package")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        expected = {"AirPods4Tray.exe", "README.txt", "LICENSE"}
        if len(archive.infolist()) != 3 or set(archive.namelist()) != expected:
            raise ValueError("Unexpected package contents")
        if archive.testzip() is not None:
            raise ValueError("Package CRC verification failed")
    notes = checked_path(root, manifest["notes"]).read_text(encoding="utf-8").strip()
    if not notes:
        raise ValueError("Release notes are empty")
    checksums = (manifest["sha256"] + "  " + asset.name + "\n").encode("ascii")
    return manifest, notes, {
        asset.name: (data, "application/zip"),
        "SHA256SUMS.txt": (checksums, "text/plain"),
    }


class GitHub:
    def __init__(self, repository, token):
        self.repository = repository
        self.token = token

    def request(self, method, path, payload=None, content_type=None, missing_ok=False):
        host = "uploads.github.com" if content_type else "api.github.com"
        url = "https://" + host + "/repos/" + self.repository + path
        headers = {
            "Authorization": "Bearer " + self.token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "AirPodsTray-release",
        }
        data = payload if content_type else (
            json.dumps(payload).encode("utf-8") if payload is not None else None
        )
        if data is not None:
            headers["Content-Type"] = content_type or "application/json"
        try:
            with urlopen(Request(url, data=data, headers=headers, method=method), timeout=60) as response:
                return json.load(response)
        except HTTPError as error:
            if missing_ok and error.code == 404:
                return None
            raise RuntimeError("GitHub returned HTTP %s for %s %s" %
                               (error.code, method, path)) from None


def verify_asset(remote, name, data):
    expected_digest = "sha256:" + hashlib.sha256(data).hexdigest()
    if (remote.get("name") != name or remote.get("state") != "uploaded"
            or remote.get("size") != len(data) or remote.get("digest") != expected_digest):
        raise ValueError("Release asset differs from the verified file: " + name)


def find_release(api, tag):
    release = api.request("GET", "/releases/tags/" + quote(tag, safe=""), missing_ok=True)
    if release is not None:
        return release
    # The tag endpoint may omit drafts. Listing with write access includes them.
    matches = []
    page = 1
    while True:
        releases = api.request("GET", "/releases?per_page=100&page=" + str(page))
        matches.extend(item for item in releases if item["tag_name"] == tag)
        if len(releases) < 100:
            break
        page += 1
    if len(matches) > 1:
        raise ValueError("Multiple drafts use this tag; review them before publishing")
    return matches[0] if matches else None


def publish(api, manifest, notes, assets):
    tag = quote(manifest["tag"], safe="")
    ref = api.request("GET", "/git/ref/tags/" + tag, missing_ok=True)
    if ref is not None and (ref.get("object", {}).get("type") != "commit"
                            or ref.get("object", {}).get("sha") != manifest["target_commit"]):
        raise ValueError("Existing tag does not point to the tested source commit")
    release = find_release(api, manifest["tag"])
    metadata = {
        "tag_name": manifest["tag"],
        "target_commitish": manifest["target_commit"],
        "name": manifest["title"],
        "body": notes,
        "prerelease": manifest["prerelease"],
    }
    if release is None:
        release = api.request("POST", "/releases", {
            **metadata, "draft": True, "make_latest": "false"
        })
    if any(release.get(key) != value for key, value in metadata.items()):
        raise ValueError("Existing release metadata differs; refusing to overwrite it")
    release_id = str(int(release["id"]))
    existing = {asset["name"]: asset for asset in release.get("assets", [])}
    if set(existing) - set(assets):
        raise ValueError("Existing release contains unexpected assets")
    # Validate all existing assets before uploading anything on a retry.
    for name, remote in existing.items():
        verify_asset(remote, name, assets[name][0])
    if not release["draft"]:
        if set(existing) != set(assets):
            raise ValueError("Published release is missing assets; refusing to modify it")
        return release
    for name, (data, mime) in assets.items():
        if name not in existing:
            remote = api.request("POST", "/releases/" + release_id + "/assets?name="
                                 + quote(name, safe=""), data, content_type=mime)
            verify_asset(remote, name, data)
    return api.request("PATCH", "/releases/" + release_id, {
        "draft": False, "prerelease": manifest["prerelease"], "make_latest": "false"
    })


def main():
    root = Path(__file__).resolve().parents[1]
    manifest, notes, assets = load_bundle(root)
    if os.environ.get("GITHUB_REPOSITORY") != manifest["repository"]:
        raise ValueError("Manifest repository does not match the workflow repository")
    token = os.environ.get("GH_TOKEN")
    if not token:
        raise ValueError("GH_TOKEN is required")
    release = publish(GitHub(manifest["repository"], token), manifest, notes, assets)
    if release.get("draft") is not False:
        raise ValueError("Release has not been published")
    print("Published: " + release["html_url"])


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
