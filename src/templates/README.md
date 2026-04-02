# PhantomRender Visual Studio Templates

This folder contains the source for two Visual Studio project templates:

- `PhantomRender NativeAOT Overlay Host`
- `PhantomRender .NET Framework Overlay Host`

The templates generate standalone sample projects that consume the published NuGet packages:

- `PhantomRender`
- `PhantomRender.ImGui`

## Layout

- `PhantomRender.NativeAot.Template/`
  The project template payload for the NativeAOT internal host.
- `PhantomRender.NetFramework.Template/`
  The project template payload for the .NET Framework internal host.
- `PhantomRender.Templates.Vsix/`
  A VSIX packaging scaffold that bundles both templates for Visual Studio Marketplace.

## Generate Template Archives

Use the packaging script to materialize the template `.zip` files with a concrete NuGet version:

```powershell
pwsh ./src/templates/pack-templates.ps1 -PhantomRenderVersion 0.1.0-preview.1
```

Generated archives are written to:

```text
src/templates/artifacts/
```

## Install Locally For Testing

To install the templates into your local Visual Studio user template directory:

```powershell
pwsh ./src/templates/install-local-templates.ps1 -PhantomRenderVersion 0.1.0-preview.1
```

After copying the archives, restart Visual Studio. If the templates do not appear immediately, run:

```powershell
devenv /installvstemplates
```

## Build The VSIX

1. Make sure the `Visual Studio extension development` workload is installed.
2. Run `pack-templates.ps1` or build the VSIX project directly.
3. Open `src/templates/PhantomRender.Templates.Vsix/PhantomRender.Templates.Vsix.csproj` in Visual Studio 2022.
4. Build the project to generate a `.vsix`.
5. Test the extension in the Visual Studio experimental instance before publishing.

The VSIX project runs `pack-templates.ps1` before build and drops the generated template archives into the `Templates/` folder consumed by the manifest.
