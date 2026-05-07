param(
    [string]$OutputDirectory = ".\docs\reviews"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$output = Resolve-Path $OutputDirectory
$temp = Join-Path $env:TEMP "AssetraGalleryCapture"

if (Test-Path $temp) {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

New-Item -ItemType Directory -Path $temp | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repoRoot\Assetra.WPF\Assetra.WPF.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $temp "AssetraGalleryCapture.csproj") -Encoding UTF8

@'
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Assetra.WPF.DesignSystem;

namespace AssetraGalleryCapture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outputDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(outputDir);

        var app = new Application();
        Render(app, "Light", outputDir);
        Render(app, "Dark", outputDir);
        return 0;
    }

    private static void Render(Application app, string theme, string outputDir)
    {
        app.Resources.MergedDictionaries.Clear();
        Add(app, "pack://application:,,,/Assetra.WPF;component/DesignSystem/Tokens.xaml");
        Add(app, $"pack://application:,,,/Assetra.WPF;component/DesignSystem/Themes/{theme}.xaml");
        Add(app, "pack://application:,,,/Assetra.WPF;component/DesignSystem/Styles/Focus.xaml");
        Add(app, "pack://application:,,,/Assetra.WPF;component/DesignSystem/Styles.xaml");
        Add(app, "pack://application:,,,/Assetra.WPF;component/Resources/AppResources.xaml");
        Add(app, "pack://application:,,,/Assetra.WPF;component/Languages/zh-TW.xaml");

        var gallery = new ControlGallery
        {
            Width = 1280,
            Height = 1500,
            Background = (Brush)app.Resources["AppBackground"]
        };

        gallery.Measure(new Size(gallery.Width, gallery.Height));
        gallery.Arrange(new Rect(0, 0, gallery.Width, gallery.Height));
        gallery.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)gallery.Width, (int)gallery.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(gallery);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(outputDir, $"control-gallery-{theme.ToLowerInvariant()}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine(path);
    }

    private static void Add(Application app, string uri) =>
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
}
'@ | Set-Content -Path (Join-Path $temp "Program.cs") -Encoding UTF8

dotnet run --project (Join-Path $temp "AssetraGalleryCapture.csproj") -- $output
