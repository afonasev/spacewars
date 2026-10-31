# Recast authority geometry prerequisite

This bounded .NET 8 executable compiles the production Runtime/Simulation sources,
prepares the real Three Crossings authority polygons, bakes the production C ABI,
and validates every returned ramp→deck segment through authority sweep.
It also checks cancellation and native handle release. It is not an ordinary
threaded match, Unity lifecycle check, performance qualification or Player gate.

From repository root after `python3 tools/build_recast.py`:

```
DYLD_LIBRARY_PATH="$PWD/.local/recast-native" dotnet run --project tools/recast-qa/recast-qa.csproj -p:UseSharedCompilation=false
python3 -m unittest tools.test_recast_native -v
```

The map backend still refuses explicit layered-portal terrain; those links and
an overlapping-floor case remain required before stage completion. There is no
fallback that silently flattens a floor or starts an exact per-member search.
