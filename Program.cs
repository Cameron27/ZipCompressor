using System.Text;

namespace ZipCompressor
{
    class Program
    {
        static void Main(string[] args)
        {
            // Check args
            if (args.Length < 1)
            {
                Console.Error.WriteLine($"usage: ZipCompressor <out> [<files|directories>]");
                return;
            }

            // Create file to write to
            FileStream fs;
            try
            {
                fs = new FileStream(args[0], FileMode.Create);
            }
            catch (Exception)
            {
                Console.Error.WriteLine($"Unable to create file {args[0]}.");
                return;
            }

            EntryInfo[] files = GetFilesAndDirectories(args.Skip(1).ToArray());

            int currentOffset = 0;
            List<byte[]> centralHeaders = new List<byte[]>();
            // Compress and write each file
            WriteFiles(files, ref currentOffset, centralHeaders, fs);

            // Create central directory
            try
            {
                foreach (byte[] centralHeader in centralHeaders)
                {
                    fs.Write(centralHeader, 0, centralHeader.Length);
                }
                byte[] endOFCentral = CreateEndOfCentral(currentOffset, centralHeaders.Count, centralHeaders.Sum(bs => bs.Length));
                fs.Write(endOFCentral, 0, endOFCentral.Length);
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Error encountered writing to file.");
            }

            try
            {
                fs.Close();
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Error encountered closing file.");
            }
        }

        private static void WriteFiles(IEnumerable<EntryInfo> entries, ref int currentOffset, List<byte[]> centralHeaders, FileStream fs)
        {
            foreach (EntryInfo entry in entries)
            {
                // Read entry data
                byte[] entryData = new byte[] { };
                DateTime entryDate;
                try
                {
                    if (!entry.isDirectory)
                        entryData = File.ReadAllBytes(entry.relativePath);
                    entryDate = File.GetLastWriteTime(entry.relativePath);
                }
                catch (Exception)
                {
                    if (entry.isDirectory)
                        Console.Error.WriteLine($"Directory {entry.relativePath} is not being explisitaly added as its info could not be read.");
                    else
                        Console.Error.WriteLine($"Skipping file {entry.relativePath} as there was a problem reading the file.");
                    continue;
                }

                // Create entry
                (byte[] header, byte[] body, byte[] centralHeader) = CreateEntry(entry.path, entryData, entryDate, currentOffset, entry.isDirectory);
                currentOffset += header.Length + body.Length;
                centralHeaders.Add(centralHeader);

                // Write entry
                try
                {
                    fs.Write(header, 0, header.Length);
                    fs.Write(body, 0, body.Length);
                }
                catch (Exception)
                {
                    Console.Error.WriteLine("Error encountered writing to entry.");
                }
            }
        }

        private static (byte[] header, byte[] body, byte[] centralHeader) CreateEntry(string entryName, byte[] entryData, DateTime entryDate, int offset, bool isDirectory)
        {
            List<byte> header = new List<byte>();
            List<byte> centralHeader = new List<byte>();

            // Header
            header.AddRange(new byte[] { (byte)0x50, (byte)0x4b, (byte)0x03, (byte)0x04 });
            centralHeader.AddRange(new byte[] { (byte)0x50, (byte)0x4b, (byte)0x01, (byte)0x02 });

            // Version
            header.AddRange(new byte[] { (byte)0x14, (byte)0x00 });
            centralHeader.AddRange(new byte[] { (byte)0x14, (byte)0x00 });
            centralHeader.AddRange(new byte[] { (byte)0x14, (byte)0x00 });

            // Flags
            header.AddRange(new byte[] { (byte)0x00, (byte)0x00 });
            centralHeader.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // Compression method
            header.AddRange(new byte[] { isDirectory ? (byte)0x00 : (byte)0x08, (byte)0x00 });
            centralHeader.AddRange(new byte[] { (byte)0x08, (byte)0x00 });

            // Last modified time and date
            int time = entryDate.Second / 2 + (entryDate.Minute << 5) + (entryDate.Hour << 11);
            header.AddRange(new byte[] { (byte)time, (byte)(time >> 8) });
            centralHeader.AddRange(new byte[] { (byte)time, (byte)(time >> 8) });
            int date = entryDate.Day + (entryDate.Month << 5) + (entryDate.Year - 1980 << 9);
            header.AddRange(new byte[] { (byte)date, (byte)(date >> 8) });
            centralHeader.AddRange(new byte[] { (byte)date, (byte)(date >> 8) });

            // CRC-32
            uint crc32 = Cryptography.CRC32(entryData);
            header.AddRange(new byte[] { (byte)crc32, (byte)(crc32 >> 8), (byte)(crc32 >> 16), (byte)(crc32 >> 24) });
            centralHeader.AddRange(new byte[] { (byte)crc32, (byte)(crc32 >> 8), (byte)(crc32 >> 16), (byte)(crc32 >> 24) });

            // Compressed size
            byte[] compressedData = Deflate.Encode(entryData);
            int l = compressedData.Length;
            header.AddRange(new byte[] { (byte)l, (byte)(l >> 8), (byte)(l >> 16), (byte)(l >> 24) });
            centralHeader.AddRange(new byte[] { (byte)l, (byte)(l >> 8), (byte)(l >> 16), (byte)(l >> 24) });

            // Uncompressed size
            l = entryData.Length;
            header.AddRange(new byte[] { (byte)l, (byte)(l >> 8), (byte)(l >> 16), (byte)(l >> 24) });
            centralHeader.AddRange(new byte[] { (byte)l, (byte)(l >> 8), (byte)(l >> 16), (byte)(l >> 24) });

            // File name length
            l = entryName.Length;
            header.AddRange(new byte[] { (byte)l, (byte)(l >> 8) });
            centralHeader.AddRange(new byte[] { (byte)l, (byte)(l >> 8) });

            // Extra field length
            header.AddRange(new byte[] { (byte)0x00, (byte)0x00 });
            centralHeader.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // Comment length
            centralHeader.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // Disk number
            centralHeader.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // File attributes
            centralHeader.AddRange(new byte[] { (byte)0x00, (byte)0x00 });
            centralHeader.AddRange(new byte[] { isDirectory ? (byte)0x10 : (byte)0x20, (byte)0x00, (byte)0x00, (byte)0x00 });

            // Offset
            centralHeader.AddRange(new byte[] { (byte)offset, (byte)(offset >> 8), (byte)(offset >> 16), (byte)(offset >> 24) });

            // File name
            header.AddRange(Encoding.ASCII.GetBytes(entryName));
            centralHeader.AddRange(Encoding.ASCII.GetBytes(entryName));

            return (header.ToArray(), compressedData, centralHeader.ToArray());
        }

        private static byte[] CreateEndOfCentral(int offset, int numEntries, int centralSize)
        {
            List<byte> endOfCentral = new List<byte>();

            // Header
            endOfCentral.AddRange(new byte[] { (byte)0x50, (byte)0x4b, (byte)0x05, (byte)0x06 });

            // Number of disks
            endOfCentral.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // Disk number
            endOfCentral.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            // Num Entries
            endOfCentral.AddRange(new byte[] { (byte)numEntries, (byte)(numEntries >> 8) });
            endOfCentral.AddRange(new byte[] { (byte)numEntries, (byte)(numEntries >> 8) });

            // Size
            endOfCentral.AddRange(new byte[] { (byte)centralSize, (byte)(centralSize >> 8), (byte)(centralSize >> 16), (byte)(centralSize >> 24) });

            // Offset
            endOfCentral.AddRange(new byte[] { (byte)offset, (byte)(offset >> 8), (byte)(offset >> 16), (byte)(offset >> 24) });

            // Comment length
            endOfCentral.AddRange(new byte[] { (byte)0x00, (byte)0x00 });

            return endOfCentral.ToArray();
        }

        private static EntryInfo[] GetFilesAndDirectories(string[] paths)
        {
            List<EntryInfo> entries = new List<EntryInfo>();

            foreach (string path in paths)
            {
                // If file
                if (File.Exists(path))
                {
                    string fileName = Path.GetFileName(path);
                    entries.Add((fileName, path, false));
                }
                // If directory
                else if (Directory.Exists(path))
                {
                    string directoryName = Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
                    entries.Add((directoryName, path, true));
                    string[] subPaths = Directory.GetFileSystemEntries(path);

                    // Get all subfiles and directories and add this directory to the path
                    EntryInfo[] subEntries = GetFilesAndDirectories(subPaths);
                    entries.AddRange(subEntries.Select(entry => (EntryInfo)(Path.Join(directoryName, entry.path), entry.relativePath, entry.isDirectory)));
                }
                else
                {
                    Console.Error.WriteLine($"Skipping {path} as it does not exist.");
                }
            }

            return entries.ToArray();
        }
    }

    internal record struct EntryInfo(string path, string relativePath, bool isDirectory)
    {
        public static implicit operator EntryInfo((string path, string relativePath, bool isDirectory) value)
        {
            return new EntryInfo(value.path, value.relativePath, value.isDirectory);
        }
    }
}