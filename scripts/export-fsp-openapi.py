#!/usr/bin/env python3
from __future__ import annotations

import argparse
import importlib
import json
import os
from pathlib import Path
import sys
import tomllib
from typing import Any

from fastapi import FastAPI


FSP_VERSION = "3.1.0"
FSP_SOURCE_COMMIT = "8bb0474558c274666d6625a763a8ca9f02481c83"

PUBLIC_PATHS = {"/favicon.ico", "/healthz", "/metrics", "/readyz"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export deterministic FSP OpenAPI contracts from an exact source checkout."
    )
    parser.add_argument(
        "--fsp-source",
        type=Path,
        required=True,
        help="Path to the Fabric Shortcut Proxy source checkout.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("contracts/fsp/3.1.0"),
        help="Destination directory for generated contracts.",
    )
    return parser.parse_args()


def verify_source(source: Path) -> None:
    lite = tomllib.loads((source / "pyproject.toml").read_text(encoding="utf-8"))
    enterprise = tomllib.loads(
        (source / "enterprise" / "pyproject.toml").read_text(encoding="utf-8")
    )
    versions = {
        lite["project"]["version"],
        enterprise["project"]["version"],
    }
    if versions != {FSP_VERSION}:
        raise ValueError(
            f"Expected Lite and Enterprise {FSP_VERSION}; found {sorted(versions)}"
        )


def load_apps(source: Path) -> dict[str, FastAPI]:
    sys.path.insert(0, str(source))
    sys.path.insert(0, str(source / "src"))

    os.environ.setdefault("ENABLE_ADMIN_UI", "0")
    os.environ.setdefault("ENABLE_CONFIG_BUILDER", "0")
    os.environ.setdefault("ENABLE_MONITOR", "0")

    lite = importlib.import_module("fabric_shortcut_proxy.main").app
    manager = importlib.import_module("enterprise.control.manager_app").app
    catalog = importlib.import_module("enterprise.control.catalog_app").app
    config_router = importlib.import_module(
        "fabric_shortcut_proxy.configbuilder.router"
    ).router

    config_builder = FastAPI(
        title="Fabric Shortcut Proxy: Config Builder",
        version=FSP_VERSION,
    )
    config_builder.include_router(config_router)

    return {
        "lite": lite,
        "manager": manager,
        "catalog": catalog,
        "config-builder": config_builder,
    }


def security_schemes() -> dict[str, Any]:
    return {
        "agentBearer": {
            "type": "http",
            "scheme": "bearer",
            "description": "Microsoft Entra access token accepted on Agent control routes.",
        },
        "agentToken": {
            "type": "apiKey",
            "in": "header",
            "name": "X-Agent-Token",
            "description": "Shared Agent token accepted when token authentication is configured.",
        },
        "operatorBasic": {
            "type": "http",
            "scheme": "basic",
            "description": "Manager operator credentials when standalone authentication is configured.",
        },
        "operatorBearer": {
            "type": "http",
            "scheme": "bearer",
            "description": "Microsoft Entra operator access token.",
        },
        "sigV4": {
            "type": "apiKey",
            "in": "header",
            "name": "Authorization",
            "description": "AWS Signature Version 4 authorization for the S3 data plane.",
        },
    }


def problem_details_schema() -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": True,
        "required": ["type", "title", "status", "code", "correlationId"],
        "properties": {
            "type": {"type": "string", "format": "uri-reference"},
            "title": {"type": "string"},
            "status": {"type": "integer", "minimum": 400, "maximum": 599},
            "detail": {"type": "string"},
            "instance": {"type": "string", "format": "uri-reference"},
            "code": {"type": "string", "pattern": "^[a-z][a-z0-9_]*$"},
            "correlationId": {"type": "string", "minLength": 1},
        },
    }


def supported_schemas() -> dict[str, Any]:
    object_values = {"type": "object", "additionalProperties": True}
    return {
        "AgentList": {
            "type": "object",
            "additionalProperties": False,
            "required": ["agents", "dead"],
            "properties": {
                "agents": {"type": "array", "items": object_values},
                "dead": {"type": "array", "items": object_values},
            },
        },
        "ConfigurationSnapshot": {
            "type": "object",
            "additionalProperties": False,
            "required": ["settings", "config_file"],
            "properties": {
                "settings": object_values,
                "config_file": {"type": "string"},
            },
        },
        "ConfigurationUpdate": {
            "type": "object",
            "additionalProperties": False,
            "required": ["settings"],
            "properties": {"settings": object_values},
        },
        "ConfigurationUpdateResult": {
            "type": "object",
            "additionalProperties": True,
            "required": ["ok", "changed", "restart_required"],
            "properties": {
                "ok": {"type": "boolean"},
                "changed": {"type": "array", "items": {"type": "string"}},
                "restart_required": {"type": "boolean"},
            },
        },
        "Health": {
            "type": "object",
            "additionalProperties": True,
            "required": ["status"],
            "properties": {"status": {"type": "string"}},
        },
        "QueueActionResult": {
            "type": "object",
            "additionalProperties": True,
            "required": ["ok"],
            "properties": {
                "ok": {"type": "boolean"},
                "request_id": {"type": "string"},
                "task_id": {"type": "string"},
            },
        },
        "SourceValidationResult": {
            "type": "object",
            "additionalProperties": True,
            "required": ["ok", "source_id"],
            "properties": {
                "ok": {"const": True},
                "source_id": {"type": "string"},
                "server_version": {"type": ["string", "null"]},
                "objects": {"type": "array", "items": object_values},
                "capabilities": object_values,
                "warnings": {"type": "array", "items": {"type": "string"}},
            },
        },
    }


def schema_response(name: str, description: str = "Successful response.") -> dict[str, Any]:
    return {
        "description": description,
        "content": {
            "application/json": {
                "schema": {"$ref": f"#/components/schemas/{name}"}
            }
        },
    }


def revision_parameter() -> dict[str, Any]:
    return {
        "name": "If-Match",
        "in": "header",
        "required": True,
        "description": "ETag returned by the last successful configuration read or write.",
        "schema": {
            "type": "string",
            "pattern": "^(?:W/)?\\\"[^\\\"]+\\\"$",
        },
    }


def revision_header() -> dict[str, Any]:
    return {
        "description": "Revision of the configuration after this operation.",
        "schema": {
            "type": "string",
            "pattern": "^(?:W/)?\\\"[^\\\"]+\\\"$",
        },
    }


def add_revision_contract(operation: dict[str, Any]) -> None:
    operation.setdefault("parameters", []).append(revision_parameter())
    operation["responses"]["409"] = {
        "$ref": "#/components/responses/StaleRevision"
    }
    success = operation["responses"].get("200") or operation["responses"].get("201")
    if success is not None:
        success.setdefault("headers", {})["ETag"] = revision_header()


def apply_supported_contracts(document: str, schema: dict[str, Any]) -> None:
    schemas = schema["components"]["schemas"]
    schemas.update(supported_schemas())
    paths = schema["paths"]

    if document == "lite":
        paths["/healthz"]["get"]["responses"]["200"] = schema_response("Health")
        paths["/readyz"]["get"]["responses"]["200"] = schema_response("Health")
        return

    if document == "manager":
        paths["/healthz"]["get"]["responses"]["200"] = schema_response("Health")
        paths["/readyz"]["get"]["responses"]["200"] = schema_response("Health")
        paths["/agents"]["get"]["responses"]["200"] = schema_response("AgentList")
        paths["/control/work-queue"]["get"]["responses"]["200"] = {
            "description": "Current durable work queue and scheduler status.",
            "content": {"application/json": {"schema": {"type": "object"}}},
        }
        for path in (
            "/control/work-queue/requests/{request_id}/cancel",
            "/control/work-queue/tasks/{task_id}/retry",
        ):
            operation = paths[path]["post"]
            operation["responses"]["200"] = schema_response("QueueActionResult")
            operation["responses"]["409"] = {
                "$ref": "#/components/responses/ProblemDetails"
            }
            operation["responses"]["503"] = {
                "$ref": "#/components/responses/ProblemDetails"
            }
        return

    if document == "catalog":
        for path_item in paths.values():
            for operation in path_item.values():
                if not isinstance(operation, dict) or "responses" not in operation:
                    continue
                success_code = "201" if "201" in operation["responses"] else "200"
                operation["responses"][success_code] = {
                    "description": "Catalog operation result.",
                    "content": {
                        "application/json": {"schema": {"type": "object"}}
                    },
                }
        return

    if document != "config-builder":
        return

    paths["/_config/api/current"]["get"]["responses"]["200"] = schema_response(
        "ConfigurationSnapshot"
    )
    paths["/_config/api/current"]["get"]["responses"]["200"]["headers"] = {
        "ETag": revision_header()
    }
    for path in ("/_config/api/save", "/_config/api/apply"):
        operation = paths[path]["post"]
        operation["requestBody"] = {
            "required": True,
            "content": {
                "application/json": {
                    "schema": {"$ref": "#/components/schemas/ConfigurationUpdate"}
                }
            },
        }
        operation["responses"]["200"] = schema_response("ConfigurationUpdateResult")
        operation["responses"]["400"] = {
            "$ref": "#/components/responses/ProblemDetails"
        }
        add_revision_contract(operation)

    validate = paths["/_config/api/sources/{source_id}/validate"]["post"]
    validate["responses"]["200"] = schema_response("SourceValidationResult")
    validate["responses"]["400"] = {
        "$ref": "#/components/responses/ProblemDetails"
    }

    backup = paths["/_config/api/backup"]["post"]
    backup["requestBody"] = {
        "required": True,
        "content": {
            "application/json": {
                "schema": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["password"],
                    "properties": {
                        "password": {"type": "string", "format": "password"}
                    },
                }
            }
        },
    }
    backup["responses"]["200"] = {
        "description": "Password-encrypted FSP backup archive.",
        "headers": {
            "Content-Disposition": {"schema": {"type": "string"}},
            "X-FSP-Backup-Summary": {"schema": {"type": "string"}},
        },
        "content": {
            "application/vnd.fsp.backup": {
                "schema": {"type": "string", "format": "binary"}
            }
        },
    }
    backup["responses"]["400"] = {
        "$ref": "#/components/responses/ProblemDetails"
    }

    restore = paths["/_config/api/restore"]["post"]
    restore["responses"]["200"] = {
        "description": "Restore result and restored asset counts.",
        "content": {"application/json": {"schema": {"type": "object"}}},
    }
    restore["responses"]["400"] = {
        "$ref": "#/components/responses/ProblemDetails"
    }


def operation_security(document: str, path: str) -> list[dict[str, list[str]]]:
    if path in PUBLIC_PATHS or path in {"/", "/_config", "/_config/"}:
        return []
    if document == "manager" and path.startswith("/control/"):
        return [{"agentBearer": []}, {"agentToken": []}]
    if document == "lite" and not path.startswith("/_"):
        return [{"sigV4": []}]
    return [{"operatorBearer": []}, {"operatorBasic": []}]


def annotate(document: str, schema: dict[str, Any]) -> dict[str, Any]:
    schema["info"]["x-fsp-release"] = FSP_VERSION
    schema["info"]["x-fsp-source-commit"] = FSP_SOURCE_COMMIT
    schema["x-fsp-authentication-boundaries"] = {
        "operator": "Operator routes require configured Basic or Microsoft Entra authentication.",
        "agent": "Manager control routes require an Agent token or Microsoft Entra Agent identity.",
        "dataPlane": "Lite S3 routes require AWS Signature Version 4.",
        "public": "Health, readiness, metrics, and static entry routes are anonymous.",
    }
    schema["x-fsp-contract-overlays"] = {
        "revisionAndProblemDetails": {
            "status": "required-upstream",
            "issue": "https://github.com/Andreas-bersgtedt/Fabric-Shortcut-Proxy/issues/176",
            "description": "Required Orchestrator compatibility contract not implemented by the immutable 3.1.0 release.",
        }
    }

    components = schema.setdefault("components", {})
    components.setdefault("securitySchemes", {}).update(security_schemes())
    components.setdefault("schemas", {})["ProblemDetails"] = problem_details_schema()
    components.setdefault("responses", {})["ProblemDetails"] = {
        "description": "A stable FSP operator API error.",
        "headers": {
            "X-Correlation-ID": {
                "description": "Correlation identifier also returned in the response body.",
                "schema": {"type": "string", "minLength": 1},
            }
        },
        "content": {
            "application/problem+json": {
                "schema": {"$ref": "#/components/schemas/ProblemDetails"}
            }
        },
    }
    components["responses"]["StaleRevision"] = {
        "description": "The supplied configuration revision is stale.",
        "headers": {
            "X-Correlation-ID": {
                "description": "Correlation identifier also returned in the response body.",
                "schema": {"type": "string", "minLength": 1},
            }
        },
        "content": {
            "application/problem+json": {
                "schema": {
                    "allOf": [
                        {"$ref": "#/components/schemas/ProblemDetails"},
                        {
                            "type": "object",
                            "properties": {"code": {"const": "stale_revision"}},
                        },
                    ]
                }
            }
        },
    }
    apply_supported_contracts(document, schema)

    for path, path_item in schema["paths"].items():
        security = operation_security(document, path)
        for method, operation in path_item.items():
            if method.lower() not in {
                "delete",
                "get",
                "head",
                "options",
                "patch",
                "post",
                "put",
                "trace",
            }:
                continue
            operation["security"] = security
            operation["x-fsp-auth-boundary"] = (
                "public"
                if security == []
                else next(iter(security[0])).removesuffix("Bearer").removesuffix("Basic").removesuffix("Token")
            )
            if security:
                operation["responses"].setdefault(
                    "401", {"$ref": "#/components/responses/ProblemDetails"}
                )
                operation["responses"].setdefault(
                    "403", {"$ref": "#/components/responses/ProblemDetails"}
                )
                operation["responses"].setdefault(
                    "default", {"$ref": "#/components/responses/ProblemDetails"}
                )

    return schema


def export(source: Path, output: Path) -> None:
    verify_source(source)
    output.mkdir(parents=True, exist_ok=True)
    for name, app in load_apps(source).items():
        schema = annotate(name, app.openapi())
        destination = output / f"openapi-{name}.json"
        destination.write_text(
            json.dumps(schema, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        print(f"wrote {destination}")


def main() -> None:
    args = parse_args()
    export(args.fsp_source.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
