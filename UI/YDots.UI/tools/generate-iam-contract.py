"""
Generates src/app/Shared/models/iam-contract.model.ts from the IAM API's OpenAPI document.

WHY THIS IS GENERATED RATHER THAN HAND-WRITTEN
----------------------------------------------
There are around 260 types in the contract. Transcribing them by hand guarantees that some
field ends up spelled slightly differently from what the server sends, and a mismatch of that
kind is silent: TypeScript is happy, the property is simply `undefined` at runtime, and the bug
surfaces as an empty cell on a screen weeks later. Generating removes the whole class of error.

Regenerate whenever the API contract changes:

    python tools/generate-iam-contract.py http://localhost:5017/swagger/v1/swagger.json

The output is committed, so a build never depends on the API being up.
"""
import io
import json
import os
import re
import sys
import urllib.request

DEFAULT_URL = "http://localhost:5017/swagger/v1/swagger.json"
OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "src", "app", "Shared", "models", "iam-contract.model.ts")

PRIMITIVES = {
    ("string", None): "string",
    ("string", "date-time"): "string",
    ("string", "date"): "string",
    ("string", "uuid"): "string",
    ("string", "byte"): "string",
    ("string", "binary"): "Blob",
    ("integer", "int32"): "number",
    ("integer", "int64"): "number",
    ("integer", None): "number",
    ("number", "double"): "number",
    ("number", "float"): "number",
    ("number", None): "number",
    ("boolean", None): "boolean",
}


def load(url):
    if url.startswith("http"):
        with urllib.request.urlopen(url) as response:
            return json.loads(response.read().decode("utf-8"))
    with io.open(url, encoding="utf-8") as handle:
        return json.load(handle)


def ref_name(ref):
    return ref.rsplit("/", 1)[-1]


def safe_name(name):
    """Swagger allows characters TypeScript does not; nested generics arrive as A_B."""
    return re.sub(r"[^A-Za-z0-9_]", "_", name)


def type_of(schema, schemas):
    """Maps one OpenAPI schema node onto a TypeScript type expression."""
    if schema is None:
        return "unknown"

    if "$ref" in schema:
        return safe_name(ref_name(schema["$ref"]))

    for key, joiner in (("oneOf", " | "), ("anyOf", " | "), ("allOf", " & ")):
        if key in schema:
            parts = [type_of(part, schemas) for part in schema[key]]
            parts = [p for p in parts if p != "unknown"] or ["unknown"]
            unique = list(dict.fromkeys(parts))
            return unique[0] if len(unique) == 1 else "(" + joiner.join(unique) + ")"

    kind = schema.get("type")
    fmt = schema.get("format")

    if kind == "array":
        return type_of(schema.get("items"), schemas) + "[]"

    if kind == "object" or (kind is None and "properties" in schema):
        extra = schema.get("additionalProperties")
        if extra not in (None, True, False):
            return "Record<string, %s>" % type_of(extra, schemas)
        if "properties" in schema:
            required = schema.get("required", [])
            inner = "; ".join(
                "%s%s: %s" % (prop, "" if prop in required else "?", type_of(sub, schemas))
                for prop, sub in schema["properties"].items())
            return "{ %s }" % inner
        return "Record<string, unknown>"

    if "enum" in schema and kind == "string":
        return " | ".join("'%s'" % value for value in schema["enum"])

    return PRIMITIVES.get((kind, fmt)) or PRIMITIVES.get((kind, None)) or "unknown"


def doc(text, indent=""):
    if not text:
        return ""
    lines = [line.rstrip() for line in str(text).strip().splitlines()]
    if len(lines) == 1:
        return "%s/** %s */" % (indent, lines[0])
    body = "\n".join(("%s * %s" % (indent, line)).rstrip() for line in lines)
    return "%s/**\n%s\n%s */" % (indent, body, indent)


def main():
    url = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_URL
    document = load(url)
    schemas = document.get("components", {}).get("schemas", {})

    out = [
        "/* eslint-disable */",
        "// ---------------------------------------------------------------------------",
        "// GENERATED FILE - DO NOT EDIT BY HAND.",
        "//",
        "// Produced from the IAM API's OpenAPI document by tools/generate-iam-contract.py.",
        "// Every interface here mirrors a server DTO exactly, field for field, so a rename on",
        "// the server becomes a compile error here instead of an empty cell on a screen.",
        "//",
        "// Regenerate with:  python tools/generate-iam-contract.py",
        "// ---------------------------------------------------------------------------",
        "",
    ]

    enum_names = sorted(name for name, node in schemas.items() if "enum" in node)

    out += [
        "// =========================================================================",
        "// Enumerations",
        "//",
        "// String unions rather than TypeScript enums: the API serialises enums as",
        "// camelCase names, so a union compares directly against what arrives on the",
        "// wire with no conversion step to get wrong.",
        "// =========================================================================",
        "",
    ]

    for name in enum_names:
        node = schemas[name]
        values = node["enum"]
        if all(isinstance(value, str) for value in values):
            union = " | ".join("'%s'" % value for value in values)
        else:
            union = " | ".join(str(value) for value in values)
        description = doc(node.get("description"))
        if description:
            out.append(description)
        out.append("export type %s = %s;" % (safe_name(name), union))
        out.append("")

    out += [
        "// =========================================================================",
        "// Request and response bodies",
        "// =========================================================================",
        "",
    ]

    for name in sorted(schemas):
        node = schemas[name]
        if "enum" in node:
            continue

        description = doc(node.get("description"))
        properties = node.get("properties")

        if not properties:
            if description:
                out.append(description)
            out.append("export type %s = %s;" % (safe_name(name), type_of(node, schemas)))
            out.append("")
            continue

        required = set(node.get("required", []))
        if description:
            out.append(description)
        out.append("export interface %s {" % safe_name(name))

        for prop, sub in properties.items():
            member_doc = doc(sub.get("description"), "  ")
            if member_doc:
                out.append(member_doc)
            optional = "" if prop in required else "?"
            ts = type_of(sub, schemas)
            if sub.get("nullable", False) and "null" not in ts:
                ts += " | null"
            out.append("  %s%s: %s;" % (prop, optional, ts))

        out.append("}")
        out.append("")

    target = os.path.normpath(OUT)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with io.open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(out).rstrip() + "\n")

    print("wrote %s (%d enums, %d interfaces)"
          % (target, len(enum_names), len(schemas) - len(enum_names)))


if __name__ == "__main__":
    main()
