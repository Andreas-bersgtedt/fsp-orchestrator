import json
import unittest
from pathlib import Path


ROOT = Path(__file__).parents[2]
CONTRACT_ROOT = ROOT / "contracts" / "fsp" / "3.1.0"
DOCUMENTS = ("lite", "manager", "catalog", "config-builder")


class FspOpenApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.documents = {
            name: json.loads(
                (CONTRACT_ROOT / f"openapi-{name}.json").read_text(encoding="utf-8")
            )
            for name in DOCUMENTS
        }

    def test_documents_identify_fsp_3_1_0(self) -> None:
        for name, document in self.documents.items():
            with self.subTest(document=name):
                self.assertEqual(document["info"]["version"], "3.1.0")
                self.assertEqual(document["info"]["x-fsp-release"], "3.1.0")

    def test_documents_name_every_authentication_boundary(self) -> None:
        for name, document in self.documents.items():
            with self.subTest(document=name):
                self.assertEqual(
                    set(document["x-fsp-authentication-boundaries"]),
                    {"operator", "agent", "dataPlane", "public"},
                )
                for path_item in document["paths"].values():
                    for operation in path_item.values():
                        if not isinstance(operation, dict) or "responses" not in operation:
                            continue
                        self.assertIn("security", operation)
                        self.assertIn("x-fsp-auth-boundary", operation)

    def test_documents_define_problem_details(self) -> None:
        for name, document in self.documents.items():
            with self.subTest(document=name):
                problem = document["components"]["schemas"]["ProblemDetails"]
                self.assertEqual(
                    set(problem["required"]),
                    {"type", "title", "status", "code", "correlationId"},
                )

    def test_upstream_runtime_gap_is_explicit(self) -> None:
        expected_issue = (
            "https://github.com/Andreas-bersgtedt/Fabric-Shortcut-Proxy/issues/176"
        )
        for name, document in self.documents.items():
            with self.subTest(document=name):
                overlay = document["x-fsp-contract-overlays"][
                    "revisionAndProblemDetails"
                ]
                self.assertEqual(overlay["status"], "required-upstream")
                self.assertEqual(overlay["issue"], expected_issue)

    def test_required_orchestrator_routes_are_present(self) -> None:
        expected = {
            "lite": {"/healthz", "/readyz"},
            "manager": {
                "/agents",
                "/control/work-queue",
                "/control/work-queue/requests/{request_id}/cancel",
                "/control/work-queue/tasks/{task_id}/retry",
                "/healthz",
                "/readyz",
            },
            "catalog": {"/catalog/datasets", "/catalog/datasets/{dataset_id}"},
            "config-builder": {
                "/_config/api/apply",
                "/_config/api/backup",
                "/_config/api/current",
                "/_config/api/restore",
                "/_config/api/save",
                "/_config/api/sources/{source_id}/validate",
            },
        }
        for name, paths in expected.items():
            with self.subTest(document=name):
                self.assertTrue(paths.issubset(self.documents[name]["paths"]))

    def test_required_routes_have_machine_readable_success_responses(self) -> None:
        required = {
            "lite": {
                ("/healthz", "get"),
                ("/readyz", "get"),
            },
            "manager": {
                ("/agents", "get"),
                ("/control/work-queue", "get"),
                ("/control/work-queue/requests/{request_id}/cancel", "post"),
                ("/control/work-queue/tasks/{task_id}/retry", "post"),
            },
            "catalog": {
                ("/catalog/datasets", "get"),
                ("/catalog/datasets", "post"),
                ("/catalog/datasets/{dataset_id}", "get"),
            },
            "config-builder": {
                ("/_config/api/apply", "post"),
                ("/_config/api/backup", "post"),
                ("/_config/api/current", "get"),
                ("/_config/api/restore", "post"),
                ("/_config/api/save", "post"),
                ("/_config/api/sources/{source_id}/validate", "post"),
            },
        }
        for name, operations in required.items():
            for path, method in operations:
                with self.subTest(document=name, operation=f"{method} {path}"):
                    responses = self.documents[name]["paths"][path][method]["responses"]
                    success = responses.get("200") or responses.get("201")
                    media_types = success.get("content", {})
                    self.assertTrue(media_types)
                    for media_type in media_types.values():
                        self.assertIn("schema", media_type)

    def test_required_untyped_json_requests_have_request_schemas(self) -> None:
        required = {
            ("/_config/api/apply", "post"),
            ("/_config/api/backup", "post"),
            ("/_config/api/save", "post"),
        }
        document = self.documents["config-builder"]
        for path, method in required:
            with self.subTest(operation=f"{method} {path}"):
                request_body = document["paths"][path][method]["requestBody"]
                self.assertIn("schema", request_body["content"]["application/json"])

    def test_configuration_writes_require_prior_revision(self) -> None:
        document = self.documents["config-builder"]
        for path in ("/_config/api/apply", "/_config/api/save"):
            with self.subTest(path=path):
                operation = document["paths"][path]["post"]
                if_match = next(
                    parameter
                    for parameter in operation["parameters"]
                    if parameter["name"] == "If-Match"
                )
                self.assertTrue(if_match["required"])
                self.assertEqual(
                    operation["responses"]["409"]["$ref"],
                    "#/components/responses/StaleRevision",
                )
                self.assertIn("ETag", operation["responses"]["200"]["headers"])

    def test_configuration_reads_return_revision(self) -> None:
        response = self.documents["config-builder"]["paths"][
            "/_config/api/current"
        ]["get"]["responses"]["200"]
        self.assertIn("ETag", response["headers"])

    def test_operator_errors_have_stable_problem_details(self) -> None:
        for name, document in self.documents.items():
            problem = document["components"]["schemas"]["ProblemDetails"]
            with self.subTest(document=name):
                self.assertTrue(
                    {"code", "correlationId"}.issubset(problem["required"])
                )
                response = document["components"]["responses"]["ProblemDetails"]
                self.assertIn("X-Correlation-ID", response["headers"])
                for path_item in document["paths"].values():
                    for operation in path_item.values():
                        if (
                            not isinstance(operation, dict)
                            or operation.get("security") == []
                        ):
                            continue
                        self.assertEqual(
                            operation["responses"]["default"]["$ref"],
                            "#/components/responses/ProblemDetails",
                        )

    def test_removing_required_response_field_breaks_contract_expectations(self) -> None:
        manager_schemas = self.documents["manager"]["components"]["schemas"]
        self.assertTrue(
            {"agents", "dead"}.issubset(manager_schemas["AgentList"]["required"])
        )
        config_schemas = self.documents["config-builder"]["components"]["schemas"]
        self.assertTrue(
            {"ok", "changed", "restart_required"}.issubset(
                config_schemas["ConfigurationUpdateResult"]["required"]
            )
        )


if __name__ == "__main__":
    unittest.main()
