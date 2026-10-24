# Neutral Replay actual214 declarations

Exported from donor SHA256 `1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b` by its exact producer source `582b270570de4d17bc428b20494341bb1ffa24e3`, with `tools/qa/replay_fixture_export.cs` as the .NET entrypoint. Compile that entrypoint with Runtime/Simulation C# files from the producer revision. Pass a directory containing `donor-path.txt`; the exporter writes `declarations.json` and requires zero final ledger.

The current candidate reads only these plain obstacle/profile declarations and the existing canonical input/typed-leg data. It constructs the current ThreeCrossings provider, checks the exact recorded provider binding and all original member/recipe keys, and retains the 214-leg, 208-point, six-portal assertions. It does not decode the historical Work BODY. Producer source, compiled artifacts, donor identity and export hashes are preserved in the integration raw packet.
