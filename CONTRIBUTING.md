# Contributing

Thanks for helping! The project is intentionally small: a handful of C# files, no dependencies.

- Build: run `build.bat` (uses the compiler that ships with Windows, C# 5 syntax).
- Keep the code C# 5 compatible (no `?.`, no `$""`, no `nameof`).
- The mouse hook must never block: do slow work on another thread.
- All user-visible text lives in `src/Strings.cs` (Arabic + English); add every new string in both languages and use `Loc.T`/`Loc.F`.
- Source files are UTF-8 with BOM.
- Open an issue before large changes.

- Quick questions and bug reports: https://t.me/+NonxC28MrMBjYjlk
