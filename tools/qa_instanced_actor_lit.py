#!/usr/bin/env python3
"""Generate the cycle61 QA-only Lit variant from the installed URP source.

No production material references this fixture. Keep all installed shading and
passes; only substitute the input include and add four traditional instance
properties. Re-running must produce the same bytes for the same package.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "unity/Library/PackageCache/com.unity.render-pipelines.universal@7865b6b91f8a/Shaders"
OUTPUT = ROOT / "unity/Assets/Spacewars/Tests/Fixtures/InstancedActorLit"
INSERT = """
// Cycle61 QA-only: original MPBs, original half precision, original shading.
// DOTS keeps the installed independent path. The non-instanced variant keeps
// the exact original UnityPerMaterial declarations and reads.
#if defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_DOTS_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(SpacewarsActorProps)
    UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(half4, _EmissionColor)
    UNITY_DEFINE_INSTANCED_PROP(half, _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(half, _Smoothness)
UNITY_INSTANCING_BUFFER_END(SpacewarsActorProps)
#define _BaseColor UNITY_ACCESS_INSTANCED_PROP(SpacewarsActorProps, _BaseColor)
#define _EmissionColor UNITY_ACCESS_INSTANCED_PROP(SpacewarsActorProps, _EmissionColor)
#define _Metallic UNITY_ACCESS_INSTANCED_PROP(SpacewarsActorProps, _Metallic)
#define _Smoothness UNITY_ACCESS_INSTANCED_PROP(SpacewarsActorProps, _Smoothness)
#endif
"""


def main():
    source_shader = (PACKAGE / "Lit.shader").read_text()
    source_input = (PACKAGE / "LitInput.hlsl").read_text()
    assert source_input.count("CBUFFER_END") == 1
    original_include = '"Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"'
    assert source_shader.count(original_include) == 9
    shader = source_shader.replace('Shader "Universal Render Pipeline/Lit"', 'Shader "Spacewars/Tests/InstancedActorLit"', 1)
    shader = shader.replace(original_include, '"Cycle61LitInput.hlsl"')
    inputs = source_input.replace("CBUFFER_END", "CBUFFER_END\n" + INSERT, 1)
    assert inputs.replace("\n" + INSERT, "", 1) == source_input
    assert shader.replace('"Cycle61LitInput.hlsl"', original_include).replace('Shader "Spacewars/Tests/InstancedActorLit"', 'Shader "Universal Render Pipeline/Lit"', 1) == source_shader
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "Cycle61Lit.shader").write_text(shader)
    (OUTPUT / "Cycle61LitInput.hlsl").write_text(inputs)
    proof = {"purpose": "QA-only, not a production shader decision", "package": str(PACKAGE.relative_to(ROOT)), "inputs": {}, "outputs": {}}
    for group, files in [("inputs", [PACKAGE / "Lit.shader", PACKAGE / "LitInput.hlsl"]), ("outputs", [OUTPUT / "Cycle61Lit.shader", OUTPUT / "Cycle61LitInput.hlsl"])]:
        for path in files:
            proof[group][str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (OUTPUT / "source-proof.json").write_text(json.dumps(proof, indent=2) + "\n")
    print(json.dumps(proof))


if __name__ == "__main__":
    main()
