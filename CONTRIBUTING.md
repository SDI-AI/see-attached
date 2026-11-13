# Contributing

Thanks for taking a look at See Attached.

## Questions and bugs

- Questions about using the libraries or the Windows builder belong in [Discussions](https://github.com/SDI-AI/see-attached/discussions) once that tab is enabled, or on the [contact form](https://seeattachedreports.com/contact).
- Bugs and concrete defects belong in [Issues](https://github.com/SDI-AI/see-attached/issues). Please include the report definition, the data (with confidential values removed), the .NET version, and what you expected to see.

## Pull requests

- Open an issue first for a substantial change, so the work can be discussed before a patch is written.
- Keep the change focused. Match the style of the surrounding code and docs: plain, technical, no hype, and no features that the code does not implement.
- Run `dotnet build ReportCreator.slnx`, then `dotnet test --project tests/ReportCreator.Compile.Tests`. If the change touches PDF output, run `dotnet test --project tests/ReportCreator.Pdf.Tests` as well (Chromium must already be installed as described in the README). Do not pass extra flags such as `--nologo`; this repo uses Microsoft.Testing.Platform.
- Docs live in `README.md` and `docs/getting-started.md`. If you change behavior, update both, and keep the public site in mind; the site is maintained separately at https://seeattachedreports.com.

## License

Contributions are accepted under the MIT license in `LICENSE`. Copyright (c) 2026 Innov8now LLC.
