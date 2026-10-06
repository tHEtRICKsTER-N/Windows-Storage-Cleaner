# Third-party notices

The project source and original icon are MIT licensed.

Self-contained release packages include Microsoft .NET 10 and Windows Desktop runtime components. Their licenses and third-party notices are preserved in the release's `third-party` directory. Consult [dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/wpf](https://github.com/dotnet/wpf) for source and license information.

Inno Setup 6.7.3 is a build tool, downloaded from the official [jrsoftware/issrc release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3). The compiler is not included in portable packages or source archives. The compiled installer contains Inno Setup's installation engine, governed by its [license](https://github.com/jrsoftware/issrc/blob/main/license.txt); a copy is included with releases.

No third-party NuGet library packages are referenced by the application. .NET runtime packs are retrieved from NuGet.org during self-contained publishing.
