# Contributing

1. Open an issue for behavior changes that affect networking, privilege, updates, or runtime packaging.
2. Create a focused branch and keep third-party source changes separate from product code changes.
3. Run:

```powershell
dotnet build .\FOG.Prime.sln -c Release
.\build\verify-repository.ps1
```

4. Explain security and compatibility impact in the pull request.
5. Preserve `FOG.Engine/UPSTREAM.md`, upstream licenses, and third-party notices.

Do not commit release binaries, private host lists, personal network details, build caches, signing keys, or secrets.
