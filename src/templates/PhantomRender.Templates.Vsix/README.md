# PhantomRender Templates VSIX

This project packages the generated PhantomRender project templates into a Visual Studio extension.

## Build Flow

1. Update `PhantomRenderTemplatePackageVersion` in `PhantomRender.Templates.Vsix.csproj` when you want the generated projects to consume a newer NuGet version.
2. Build the VSIX project in Visual Studio 2022 with the `Visual Studio extension development` workload installed.
3. The project runs `../pack-templates.ps1` before build to generate:
   - `Templates/PhantomRender.NativeAot.Template.zip`
   - `Templates/PhantomRender.NetFramework.Template.zip`
4. Visual Studio then packs those archives into the resulting `.vsix`.

## Marketplace

After validating the extension in the experimental instance, upload the generated `.vsix` to Visual Studio Marketplace.
