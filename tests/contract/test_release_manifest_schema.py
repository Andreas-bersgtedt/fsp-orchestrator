import copy
import json
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker


ROOT = Path(__file__).parents[2]
SCHEMA_PATH = ROOT / "contracts" / "fsp" / "release-manifest.schema.json"
MANIFEST_PATH = ROOT / "contracts" / "fsp" / "3.1.0" / "release-manifest.json"


class ReleaseManifestSchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        cls.manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        cls.validator = Draft202012Validator(
            cls.schema,
            format_checker=FormatChecker(),
        )

    def assert_invalid(self, manifest: dict) -> None:
        self.assertNotEqual(list(self.validator.iter_errors(manifest)), [])

    def test_schema_is_valid_draft_2020_12(self) -> None:
        Draft202012Validator.check_schema(self.schema)

    def test_3_1_0_manifest_is_valid(self) -> None:
        self.validator.validate(self.manifest)

    def test_digest_is_required(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        del manifest["helmChart"]["digest"]

        self.assert_invalid(manifest)

    def test_malformed_digest_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["images"][0]["digest"] = "sha256:not-a-digest"

        self.assert_invalid(manifest)

    def test_signature_is_required(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["signatures"] = []

        self.assert_invalid(manifest)

    def test_malformed_signature_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["signatures"][0]["value"] = "not valid base64!"

        self.assert_invalid(manifest)


if __name__ == "__main__":
    unittest.main()
