// Usage: dotnet run --project Windows/IconGen -- <out.ico>
var path = args.Length > 0 ? args[0] : "Snapmark.ico";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
Snapmark.AppIcon.WriteIco(path, new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
Console.WriteLine($"Wrote {path}");
