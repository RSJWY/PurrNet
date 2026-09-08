# Repository Guidelines

## Project Structure & Module Organization

PurrNet is a Unity `6000.5.11f1` project. Runtime networking code lives in `Assets/PurrNet/Runtime`; editor tooling and build entry points are in `Assets/PurrNet/Editor` and `Assets/Editor`. Examples are under `Assets/Examples`, performance scenarios under `Assets/Benchmarks`, and Unity Test Framework assemblies under `Assets/Tests` (edit-mode) and `Assets/PlayModeTests` (play-mode). Supporting analyzers are in `RoslynAnalyzers/`. Keep Unity `.meta` files beside every asset and do not commit generated `Library/`, `Temp/`, `Builds/`, or `Logs/` content.

## Build, Test, and Development Commands

- Open the repository in Unity `6000.5.11f1` and allow Package Manager import to finish.
- Run edit-mode tests headlessly: `Unity -batchmode -quit -projectPath . -runTests -testPlatform editmode -testResults test-results/editmode.xml`.
- Run play-mode tests headlessly: `Unity -batchmode -quit -projectPath . -runTests -testPlatform playmode -testResults test-results/playmode.xml`.
- Build local Windows network-test player: `Unity -batchmode -quit -projectPath . -executeMethod LocalTestBuild.BuildWindowsPlayer` (override output with `-purrBuildOutput <path>`).
- Build the CI Linux IL2CPP player with `-executeMethod CIBuild.BuildLinuxPlayer`; add `-purrDevBuild` for a development build.

## Coding Style & Naming Conventions

Use four-space indentation, braces on their own lines, and explicit namespaces. Follow C# conventions already used here: PascalCase for types and public members, `_camelCase` for private fields, and `I`-prefixed interfaces. Keep Unity component scripts and assets co-located, and update the relevant `.asmdef` when adding a new test or module. Match nearby code rather than introducing a new formatter; no repository-wide lint command is configured.

## Testing Guidelines

Write NUnit tests with `[Test]`, `[UnityTest]`, `[SetUp]`, and `[TearDown]` as appropriate. Place tests in the assembly that owns the behavior (`Assets/Tests/...` or `Assets/PlayModeTests/...`), name classes with a `Tests` suffix, and use descriptive method names such as `ReadBits_..._Throws`. Run the narrowest affected test assembly first, then the full edit/play-mode suites for cross-cutting changes.

## Commit & Pull Request Guidelines

Use short imperative Conventional Commit subjects, for example `fix: ...`, `feat: ...`, or `ci(release): ...`; keep unrelated changes in separate commits. Pull requests should explain the behavior change and affected Unity scenes/packages, link an issue when one exists, list the Unity test commands run (including result paths), and attach screenshots or recordings for editor/UI changes. Do not include generated build output or local machine settings.
