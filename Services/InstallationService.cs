using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Win32;
using GeekToolDownloader.Models;

namespace GeekToolDownloader.Services
{
    public interface IInstallationService
    {
        bool IsInstalled(ToolItemModel tool);
        Task<bool> InstallAsync(ToolItemModel tool, string filePath);
    }

    public class InstallationService : IInstallationService
    {
        private static readonly Lazy<HashSet<string>> _installedApps = new Lazy<HashSet<string>>(() =>
        {
            var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            void ScanRegistry(RegistryKey baseKey, string path)
            {
                try
                {
                    using var key = baseKey.OpenSubKey(path);
                    if (key == null) return;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey != null)
                            {
                                var displayName = subKey.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(displayName))
                                {
                                    apps.Add(displayName!);
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(subKeyName))
                            {
                                apps.Add(subKeyName);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            ScanRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            return apps;
        });

        private static readonly Lazy<string[]> _pathDirectories = new Lazy<string[]>(() =>
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return Array.Empty<string>();
            return pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        });

        public static string GetArchiveInstallDirectory(ToolItemModel tool)
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeekToolDownloader",
                "Packages");

            var safeName = GetSafeFileName(tool.Name);
            return Path.Combine(baseDir, safeName);
        }

        public bool IsInstalled(ToolItemModel tool)
        {
            if (tool.Check == null || tool.Check.Length == 0) return false;

            foreach (var c in tool.Check)
            {
                if (c.StartsWith("reg:", StringComparison.OrdinalIgnoreCase))
                {
                    var regName = c.Substring(4).Trim();
                    if (_installedApps.Value.Contains(regName))
                    {
                        return true;
                    }
                }
                else if (c.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                {
                    var pathVal = c.Substring(5).Trim();
                    if (CheckPathRule(pathVal)) return true;
                }
            }

            return false;
        }

        private bool CheckPathRule(string pathRule)
        {
            if (string.IsNullOrWhiteSpace(pathRule)) return false;

            var normalized = pathRule.Trim().Trim('"');
            if (File.Exists(normalized) || Directory.Exists(normalized)) return true;

            if (!Path.IsPathRooted(normalized))
            {
                var fileName = Path.GetFileName(normalized);

                foreach (var entry in _pathDirectories.Value)
                {
                    var baseDir = entry.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(baseDir)) continue;

                    try
                    {
                        var directCandidate = Path.Combine(baseDir, normalized);
                        if (File.Exists(directCandidate) || Directory.Exists(directCandidate)) return true;

                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            var fileCandidate = Path.Combine(baseDir, fileName);
                            if (File.Exists(fileCandidate)) return true;
                        }
                    }
                    catch { }
                }

                var knownRoots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                };

                foreach (var root in knownRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    try
                    {
                        var candidate = Path.Combine(root, normalized);
                        if (File.Exists(candidate) || Directory.Exists(candidate)) return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        public async Task<bool> InstallAsync(ToolItemModel tool, string filePath)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (tool.Type == ToolType.Msix || tool.Type == ToolType.MsixBundle)
                    {
                        return InstallMsixPackage(filePath);
                    }

                    if (tool.Type == ToolType.Zip || tool.Type == ToolType.Tar || tool.Type == ToolType.SevenZip)
                    {
                        var extractPath = GetArchiveInstallDirectory(tool);
                        if (!Directory.Exists(extractPath))
                            Directory.CreateDirectory(extractPath);

                        if (tool.Type == ToolType.Zip)
                        {
                            ExtractZip(filePath, extractPath);
                        }
                        else if (tool.Type == ToolType.Tar)
                        {
                            if (!RunProcess("tar.exe", $"-xf \"{filePath}\" -C \"{extractPath}\""))
                                return false;
                        }
                        else if (tool.Type == ToolType.SevenZip)
                        {
                            if (!ExtractSevenZip(filePath, extractPath))
                                return false;
                        }

                        return true;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Arguments = tool.InstallArgs,
                        UseShellExecute = true,
                        Verb = "runas", // Request admin rights
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    if (tool.Type == ToolType.Msi)
                    {
                        psi.FileName = "msiexec.exe";
                        psi.Arguments = $"/i \"{filePath}\" {tool.InstallArgs}";
                    }

                    using (var process = Process.Start(psi))
                    {
                        if (process == null) return false;
                        
                        bool exited = await Task.Run(() => process.WaitForExit(15 * 60 * 1000));
                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                            return false;
                        }
                        
                        return process.ExitCode == 0 || process.ExitCode == 3010;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Install failed: {ex.Message}");
                    return false;
                }
            });
        }

        private static bool InstallMsixPackage(string filePath)
        {
            // Install for current user via Appx deployment pipeline.
            // Use single-quote escaping for PowerShell string literal safety.
            var escapedPath = filePath.Replace("'", "''");
            var command = $"Add-AppxPackage -Path '{escapedPath}' -ForceApplicationShutdown -ErrorAction Stop";
            return RunProcess("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"");
        }

        private static void ExtractZip(string filePath, string extractPath)
        {
            string fullExtractPath = Path.GetFullPath(extractPath);
            using (var archive = ZipFile.OpenRead(filePath))
            {
                foreach (var entry in archive.Entries)
                {
                    string destinationPath = Path.GetFullPath(Path.Combine(fullExtractPath, entry.FullName));
                    if (!destinationPath.StartsWith(fullExtractPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        var parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir))
                            Directory.CreateDirectory(parentDir);
                        entry.ExtractToFile(destinationPath, true);
                    }
                }
            }
        }

        private static bool ExtractSevenZip(string filePath, string extractPath)
        {
            // Prefer system-installed 7z; fallback to 7za.
            if (RunProcess("7z.exe", $"x -y \"{filePath}\" -o\"{extractPath}\""))
                return true;

            if (RunProcess("7za.exe", $"x -y \"{filePath}\" -o\"{extractPath}\""))
                return true;

            return false;
        }

        private static bool RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Package";

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return string.IsNullOrWhiteSpace(safeName) ? "Package" : safeName;
        }
    }
}
