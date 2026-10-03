#!/usr/bin/env python3
"""Recover pointer-sized C typedefs erased by the upstream Windows x64 generator.

Example with Unity's Android NDK clang (which supplies its own C headers)::

    python Tools/extract-unity-native-types.py --clang <NDK>/bin/clang.exe

The checked-in metadata lets generate-unity.py run without clang. Re-extract after
updating FFmpeg headers or upstream generated bindings. --check compares without
writing. --target and repeatable --clang-arg allow another clang/sysroot.

Windows SDK interfaces are opaque pointer declarations in temporary headers. No
Windows SDK layout is inferred: this tool extracts typedef spelling only from the
real FFmpeg headers, including AVD3D11FrameDescriptor.index. Native layouts must be
verified separately against the actual compiler/SDK used to build FFmpeg.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Tools/unity-native-types.json"
NATIVE = r"(?:size_t|ptrdiff_t|intptr_t|uintptr_t)"
SOURCE_PATHS = [
    "FFmpeg.AutoGen/generated/ffmpeg.functions.facade.g.cs",
    "FFmpeg.AutoGen/generated/ffmpeg.functions.inline.g.cs",
    "FFmpeg.AutoGen/generated/Structs.g.cs",
    "FFmpeg.AutoGen/generated/Delegates.g.cs",
    "FFmpeg.AutoGen.CppSharpUnsafeGenerator/Program.cs",
]

WINDOWS_POINTER_TYPES = """#ifndef FFMPEG_METADATA_WINDOWS_OPAQUE_H
#define FFMPEG_METADATA_WINDOWS_OPAQUE_H
typedef unsigned int UINT;
typedef unsigned int DWORD;
typedef void *HANDLE;
typedef struct ID3D11Device ID3D11Device;
typedef struct ID3D11DeviceContext ID3D11DeviceContext;
typedef struct ID3D11VideoDevice ID3D11VideoDevice;
typedef struct ID3D11VideoContext ID3D11VideoContext;
typedef struct ID3D11Texture2D ID3D11Texture2D;
typedef struct ID3D11VideoDecoder ID3D11VideoDecoder;
typedef struct ID3D11VideoDecoderOutputView ID3D11VideoDecoderOutputView;
typedef struct D3D11_VIDEO_DECODER_CONFIG D3D11_VIDEO_DECODER_CONFIG;
typedef struct IDirect3DDeviceManager9 IDirect3DDeviceManager9;
typedef struct IDirect3DSurface9 IDirect3DSurface9;
typedef struct IDirectXVideoDecoder IDirectXVideoDecoder;
typedef struct DXVA2_ConfigPictureDecode DXVA2_ConfigPictureDecode;
typedef IDirect3DSurface9 *LPDIRECT3DSURFACE9;
#endif
"""


def read(path):
    return path.read_text(encoding="utf-8-sig")


def fingerprint(paths):
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(read(path).encode("utf-8")).hexdigest()
            for path in sorted(paths)}


def parameters(text):
    """Only names and positions matter; callback string MarshalAs contains commas."""
    text = re.sub(r"^\s*#.*$", "", text, flags=re.M)
    text = re.sub(r"\[[^\]]*\]", "", text)
    return re.findall(r"@(\w+)", text)


def generated_scopes():
    funcs, delegates = {}, {}
    for relative in SOURCE_PATHS[:2]:
        for match in re.finditer(r"public static \S+ (\w+)\(([^)\n]*)\)", read(ROOT / relative)):
            funcs[match[1]] = parameters(match[2])
    for match in re.finditer(r"public unsafe delegate \S+ (\w+)\s*\((.*?)\);",
                            read(ROOT / SOURCE_PATHS[3]), re.S):
        delegates[match[1]] = parameters(match[2])
    structs = {}
    for match in re.finditer(r"public unsafe partial struct (\w+)\s*\{(.*?)^\}",
                            read(ROOT / SOURCE_PATHS[2]), re.S | re.M):
        structs[match[1]] = set(re.findall(r"public \S+ @(\w+);", match[2]))
    return funcs, structs, delegates


def native_type(spelling, declaration=""):
    """Return a scalar/pointer/array spelling; never recurse into callback types."""
    spelling = re.sub(r"\b(?:const|volatile|restrict)\b", "", spelling)
    spelling = re.sub(r"\s+", "", spelling)
    match = re.fullmatch(rf"({NATIVE})(\**|\[\d+\])", spelling)
    if not match:
        return None
    # C adjusts function parameter arrays to pointers in the AST. Preserve their
    # source extent so the consumer can use the correct fixed-array wrapper.
    extent = re.search(r"\[\s*(\d+)\s*\]", declaration)
    if extent:
        return match[1] + "[" + extent[1] + "]"
    return spelling


def clang_ast(args):
    includes = re.findall(r'p\.Parse\("([^"]+)"\)', read(ROOT / SOURCE_PATHS[-1]))
    if not includes:
        raise ValueError("Upstream header entrypoints were not found")
    source = "\n".join(f'#include "{header}"' for header in includes) + "\n"
    with tempfile.TemporaryDirectory(prefix="ffmpeg-unity-types-") as temporary:
        folder = Path(temporary)
        for name in ["d3d11.h", "d3d9.h", "dxva2api.h"]:
            (folder / name).write_text(WINDOWS_POINTER_TYPES, encoding="utf-8")
        command = [args.clang]
        if args.target:
            command.append("--target=" + args.target)
        command += args.clang_arg + ["-I", str(folder), "-I", str(ROOT / "FFmpeg/include"), "-x", "c", "-"]
        preprocessed = subprocess.run(command + ["-E", "-P"], input=source.encode("utf-8"),
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True).stdout
        # Flattening first provides deterministic source offsets even where JSON
        # AST serialization omits repeated header filenames or expands macros.
        tree = subprocess.run(command + ["-Xclang", "-ast-dump=json", "-fsyntax-only"],
                              input=preprocessed, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, check=True).stdout
        return json.loads(tree), preprocessed


def extract(tree, source):
    public_functions, public_structs, public_delegates = generated_scopes()
    result = {"functions": {}, "structs": {}, "delegates": {}}
    seen_functions = set()

    def add(container, name, record):
        if not record:
            return
        previous = result[container].get(name)
        if previous is not None and previous != record:
            raise ValueError(f"Conflicting metadata for {container}/{name}")
        result[container][name] = record

    def declaration(node):
        locations = node.get("range", {})
        begin, end = locations.get("begin", {}), locations.get("end", {})
        if "offset" in begin and "offset" in end:
            return source[begin["offset"]:end["offset"] + end.get("tokLen", 0)].decode("utf-8")
        return ""

    def callback(name, spelling):
        if name not in public_delegates:
            return
        match = re.fullmatch(r"(.*?)\(\*\)\((.*)\)", spelling)
        if not match:
            if re.search(NATIVE, spelling):
                raise ValueError(f"Unrecognized native callback: {name}: {spelling}")
            return
        names = public_delegates[name]
        types = [] if match[2] in ("", "void") else match[2].split(",")
        if len(types) != len(names):
            # Complex unrelated callback signatures are irrelevant to this map.
            if re.search(NATIVE, spelling):
                raise ValueError(f"Callback parameter mismatch: {name}")
            return
        mapped = {arg: kind for arg, spec in zip(names, types) if (kind := native_type(spec))}
        record = {"parameters": mapped} if mapped else {}
        if returns := native_type(match[1]):
            record["return"] = returns
        add("delegates", name, record)

    def walk(node, owner=""):
        kind, name = node.get("kind"), node.get("name", "")
        spelling = node.get("type", {}).get("qualType", "")
        desugared = node.get("type", {}).get("desugaredQualType", spelling)
        if kind == "RecordDecl":
            owner = name
        if kind == "FieldDecl" and owner in public_structs and name in public_structs[owner]:
            mapped = native_type(spelling, declaration(node))
            if mapped:
                result["structs"].setdefault(owner, {})[name] = mapped
            callback(owner + "_" + name, desugared)
        if kind == "FunctionDecl" and name in public_functions:
            seen_functions.add(name)
            mapped = {}
            args = [child for child in node.get("inner", []) if child.get("kind") == "ParmVarDecl"]
            names = public_functions[name]
            if len(args) != len(names) and re.search(NATIVE, spelling):
                raise ValueError(f"Function parameter mismatch: {name}")
            for index, parameter in enumerate(args):
                native_name = parameter.get("name", "")
                arg_type = parameter.get("type", {}).get("qualType", "")
                if value := native_type(arg_type, declaration(parameter)):
                    # Generated names may differ for anonymous native parameters;
                    # position is authoritative after checking arity above.
                    mapped[names[index]] = value
                callback(name + "_" + native_name, arg_type)
            record = {"parameters": mapped} if mapped else {}
            if returns := native_type(spelling.split("(", 1)[0]):
                record["return"] = returns
            add("functions", name, record)
        for child in node.get("inner", []):
            walk(child, owner)

    walk(tree)
    missing = set(public_functions) - seen_functions
    if missing:
        raise ValueError("Public functions missing from the native AST: " + ", ".join(sorted(missing)))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--clang", required=True, help="Clang executable; Unity Android NDK clang is supported")
    parser.add_argument("--target", default="armv7a-linux-androideabi23")
    parser.add_argument("--clang-arg", action="append", default=[])
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        tree, source = clang_ast(args)
        data = extract(tree, source)
    except subprocess.CalledProcessError as error:
        parser.exit(1, error.stderr.decode("utf-8", errors="replace") if error.stderr else str(error))
    data["headers"] = fingerprint((ROOT / "FFmpeg/include").rglob("*.h"))
    data["sources"] = fingerprint(ROOT / path for path in SOURCE_PATHS)
    contents = json.dumps(data, indent=2, sort_keys=True) + "\n"
    if args.check:
        if not OUTPUT.exists() or read(OUTPUT) != contents:
            parser.exit(1, "Native typedef metadata is stale; re-run extraction without --check.\n")
    else:
        OUTPUT.write_text(contents, encoding="utf-8", newline="\n")
    print(f"Native typedef metadata: {len(data['functions'])} functions, "
          f"{sum(len(fields) for fields in data['structs'].values())} fields, "
          f"{len(data['delegates'])} delegates")


if __name__ == "__main__":
    main()
