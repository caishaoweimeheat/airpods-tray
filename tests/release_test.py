"""Exercise release publishing with an in-memory API, without network or tokens."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from urllib.parse import parse_qs, urlsplit

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from publish_release import checked_path, load_bundle, publish


class FakeGitHub:
    def __init__(self):
        self.release = None
        self.ref = None
        self.calls = []
        self.corrupt_upload = False

    def request(self, method, path, payload=None, content_type=None, missing_ok=False):
        self.calls.append((method, path))
        if method == "GET":
            if path.startswith("/git/"):
                return copy.deepcopy(self.ref)
            if path.startswith("/releases?"):
                return [copy.deepcopy(self.release)] if self.release else []
            if self.release and not self.release["draft"]:
                return copy.deepcopy(self.release)
            return None
        if path == "/releases":
            self.release = {**payload, "id": 1, "assets": [], "html_url": "https://example.test/release"}
        elif "/assets?" in path:
            asset = {
                "name": parse_qs(urlsplit(path).query)["name"][0],
                "state": "uploaded", "size": len(payload),
                "digest": "sha256:" + hashlib.sha256(payload).hexdigest(),
            }
            if self.corrupt_upload:
                asset["digest"] = "sha256:" + "0" * 64
            self.release["assets"].append(asset)
            return copy.deepcopy(asset)
        else:
            self.release.update(payload)
        return copy.deepcopy(self.release)


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.manifest, self.notes, self.assets = load_bundle(ROOT)
        self.api = FakeGitHub()

    def run_publish(self):
        return publish(self.api, self.manifest, self.notes, self.assets)

    def test_verified_package_has_runtime_and_checksum(self):
        self.assertEqual(len(self.assets), 2)
        checksum = self.assets["SHA256SUMS.txt"][0].decode("ascii")
        self.assertIn(self.manifest["sha256"], checksum)

    def test_create_draft_upload_both_assets_then_publish(self):
        release = self.run_publish()
        writes = [call for call in self.api.calls if call[0] != "GET"]
        self.assertEqual([method for method, _ in writes], ["POST", "POST", "POST", "PATCH"])
        self.assertTrue(release["prerelease"])
        self.assertFalse(release["draft"])
        self.assertEqual(set(a["name"] for a in release["assets"]), set(self.assets))

    def test_repeat_publish_does_not_change_existing_release(self):
        self.run_publish()
        self.api.calls.clear()
        self.run_publish()
        self.assertTrue(all(method == "GET" for method, _ in self.api.calls))

    def test_wrong_source_tag_blocks_all_writes(self):
        self.api.ref = {"object": {"type": "commit", "sha": "0" * 40}}
        with self.assertRaisesRegex(ValueError, "Existing tag"):
            self.run_publish()
        self.assertTrue(all(method == "GET" for method, _ in self.api.calls))

    def test_existing_asset_mismatch_blocks_all_writes(self):
        self.run_publish()
        self.api.release["assets"][0]["digest"] = "sha256:" + "0" * 64
        self.api.calls.clear()
        with self.assertRaisesRegex(ValueError, "asset differs"):
            self.run_publish()
        self.assertTrue(all(method == "GET" for method, _ in self.api.calls))

    def test_failed_upload_keeps_release_as_draft(self):
        self.api.corrupt_upload = True
        with self.assertRaisesRegex(ValueError, "asset differs"):
            self.run_publish()
        self.assertTrue(self.api.release["draft"])
        self.assertFalse(any(method == "PATCH" for method, _ in self.api.calls))

    def test_draft_retry_only_uploads_missing_asset(self):
        self.run_publish()
        self.api.release["draft"] = True
        self.api.release["assets"].pop()
        self.api.calls.clear()
        self.run_publish()
        writes = [method for method, _ in self.api.calls if method != "GET"]
        self.assertEqual(writes, ["POST", "PATCH"])

    def test_local_package_tampering_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = {**self.manifest, "asset": "bad.zip"}
            (root / "release-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            (root / "bad.zip").write_bytes(b"not the tested package")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                load_bundle(root)

    def test_paths_cannot_escape_repository(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            checked_path(ROOT, "../outside.zip")


if __name__ == "__main__":
    unittest.main()
