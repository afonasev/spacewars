# Asset and dependency notices

Project code is distributed under the repository MIT license. Unity Editor and packages retain their own licenses; they are not bundled as source in this repository. The pinned package inventory is `unity/Packages/manifest.json` (Unity URP, Input System, Test Framework, glTFast and Newtonsoft JSON).

Spacewars models/textures, the planet/orbit icon and website artwork are project-authored assets, including generated artwork. StyleA asset hashes are recorded in `unity/Assets/Spacewars/Content/StyleA/provenance.json`; the native balance contract has its own provenance. The URP RuntimeLit material is Unity-dependent serialized material data; no third-party donor gameplay/runtime assets are included.

The Windows resource generator github.com/akavel/rsrc v0.10.2 is MIT licensed and used as a build tool. Its source is fetched through Go modules and not vendored. The release update signatures use Go's standard Ed25519 implementation.
