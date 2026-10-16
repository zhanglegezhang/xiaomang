using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ShareX
{
    // Store a verified, input-only archive; never require installed code to preserve it.
    internal static class PersonalDataDeferredInput
    {
        internal const string Folder = "PendingInputData";
        internal static bool IsPendingRelative(string relative) =>
            relative.StartsWith(Folder + "/", StringComparison.Ordinal) &&
            relative.EndsWith(".xmbackup", StringComparison.Ordinal) &&
            Guid.TryParseExact(relative.Substring(Folder.Length + 1, relative.Length - Folder.Length - 10), "N", out _);

        internal static string[] List(string personal)
        {
            string folder = Path.Combine(personal, Folder);
            PersonalDataArchive.SafePath(folder);
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.xmbackup").OrderBy(File.GetCreationTimeUtc).ToArray() : Array.Empty<string>();
        }

        internal static void Create(string extracted, string destination)
        {
            var manifest = PersonalDataArchive.ReadJson(Path.Combine(extracted, PersonalDataArchive.ManifestName))
                .ToObject<PersonalDataManifest>(PersonalDataPreferences.Serializer());
            manifest.Categories = new() { PersonalDataKind.InputMethod };
            manifest.Files = manifest.Files.Where(f => f.Path.StartsWith("InputMethod/", StringComparison.Ordinal)).ToList();
            manifest.Directories = manifest.Directories.Where(p => p.StartsWith("InputMethod/", StringComparison.Ordinal)).ToList();
            manifest.MissingVoiceAttachments.Clear(); manifest.InvalidDiaryFiles.Clear();
            using (var zip = ZipFile.Open(destination, ZipArchiveMode.Create)) {
                foreach (var file in manifest.Files) {
                    string relative = PersonalDataArchive.Relative(file.Path);
                    string source = Path.Combine(extracted, relative.Replace('/', Path.DirectorySeparatorChar));
                    PersonalDataArchive.SafePath(source);
                    zip.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
                }
                using var writer = new StreamWriter(zip.CreateEntry(PersonalDataArchive.ManifestName).Open());
                writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(manifest));
            }
            PersonalDataArchive.Inspect(destination);
        }
        internal static void CheckSpace(string personal, PersonalDataManifest manifest, PersonalDataTransferControl control)
        {
            long bytes = manifest.Files.Where(f => f.Path.StartsWith("InputMethod/", StringComparison.Ordinal))
                .Sum(f => checked(PersonalDataSpace.Allocated(f.Bytes) + 4096));
            var budget = new PersonalDataSpace(control);
            budget.Add(personal, checked(bytes * 4 + 4 * 1024 * 1024));
            budget.Check();
        }
    }
}
