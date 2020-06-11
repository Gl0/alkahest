using Alkahest.Core.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Alkahest.Core.Data
{
    public sealed class DataCenter : IDisposable
    {
        public static int KeySize => 16;

        public static uint Version => 6;

        public static string PackedExtension => "dat";

        public static string UnpackedExtension => "dec";

        public static IReadOnlyDictionary<Region, string> FileNames { get; } =
            new Dictionary<Region, string>
            {
                { Region.DE, "DataCenter_Final_GER.dat" },
                { Region.FR, "DataCenter_Final_FRA.dat" },
                { Region.JP, "DataCenter_Final_JPN.dat" },
                { Region.KR, "DataCenter_Final.dat" },
                { Region.NA, "DataCenter_Final_USA.dat" },
                { Region.RU, "DataCenter_Final_RUS.dat" },
                { Region.SE, "DataCenter_Final_SE.dat" },
                { Region.TH, "DataCenter_Final_THA.dat" },
                { Region.TW, "DataCenter_Final_TW.dat" },
                { Region.UK, "DataCenter_Final_EUR.dat" },
            };

        public static IReadOnlyDictionary<Region, uint> Versions { get; } =
            new Dictionary<Region, uint>
            {
                { Region.DE, 353338 },
                { Region.FR, 353338 },
                { Region.JP, 353341 },
                { Region.KR, 0 },
                { Region.NA, 353337 },
                { Region.RU, 353342 },
                { Region.SE, 353339 },
                { Region.TH, 353339 },
                { Region.TW, 353340 },
                { Region.UK, 353338 },
            };

        const int ExtensionSize = 8;

        uint AttributeSize = 8;

        uint ElementSize = 16;

        const int MetadataSize = 16;

        public DataCenterHeader Header { get; }

        public DataCenterFooter Footer { get; }

        public DataCenterElement Root { get; private set; }

        internal DataCenterSegmentedRegion Attributes { get; private set; }

        internal DataCenterSegmentedRegion Elements { get; private set; }

        internal IReadOnlyList<string> Names { get; private set; }

        public bool IsFrozen => _frozen != null;

        internal bool IsDisposed { get; private set; }

        internal ReaderWriterLockSlim Lock { get; } = new ReaderWriterLockSlim();
        public bool x64 { get; }

        ConcurrentDictionary<DataCenterAddress, string> _strings =
            new ConcurrentDictionary<DataCenterAddress, string>();

        object _frozen;

        readonly bool _intern;

        DataCenterSegmentedRegion _stringRegion;

        public DataCenter(uint version)
        {
            Header = new DataCenterHeader(0, 0, -16400, version, 0, 0, 0, 0);
            Footer = new DataCenterFooter(0);
            Root = new DataCenterElement(this, DataCenterAddress.Zero);
        }

        public unsafe DataCenter(Stream stream, bool intern)
        {
            _intern = intern;
            if (stream.Length > 375000000) /// ugly hack since x64 has the same version
            {
                x64 = true;
                AttributeSize = 12;
                ElementSize = 24;
            }

            using var reader = new GameBinaryReader(stream);

            Header = ReadHeader(reader);

            ReadSimpleRegion(reader, false, ExtensionSize);

            var attributeRegion = ReadSegmentedRegion(reader, AttributeSize);
            var elementRegion = ReadSegmentedRegion(reader, ElementSize);

            _stringRegion = ReadSegmentedRegion(reader, sizeof(char));

            ReadSimpleSegmentedRegion(reader, 1024, MetadataSize);
            ReadSimpleRegion(reader, true, (uint)sizeof(DataCenterAddress));

            var nameRegion = ReadSegmentedRegion(reader, sizeof(char));

            ReadSimpleSegmentedRegion(reader, 512, MetadataSize);

            var nameAddressRegion = ReadSimpleRegion(reader, true, (uint)sizeof(DataCenterAddress));

            Footer = ReadFooter(reader);
            Attributes = attributeRegion;
            Elements = elementRegion;
            Names = ReadAddresses(nameAddressRegion).Select(x => ReadString(nameRegion, x)).ToArray();

            Reset();
        }

        public void Dispose()
        {
            if (IsFrozen)
                throw new InvalidOperationException("Data center is frozen.");

            try
            {
                Lock.EnterWriteLock();

                IsDisposed = true;

                Root = null;
                Attributes = null;
                Elements = null;
                Names = null;
                _strings = null;
                _stringRegion = null;
            }
            finally
            {
                Lock.ExitWriteLock();
            }
        }

        public object Freeze()
        {
            if (IsFrozen)
                throw new InvalidOperationException("Data center is already frozen.");

            return _frozen = new object();
        }

        public void Thaw(object token)
        {
            if (!IsFrozen)
                throw new InvalidOperationException("Data center is not frozen.");

            if (_frozen != token)
                throw new ArgumentException("Invalid freeze token.", nameof(token));

            _frozen = null;
        }

        public void Reset()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (IsFrozen)
                throw new InvalidOperationException("Data center is frozen.");

            Root = new DataCenterElement(this, DataCenterAddress.Zero);
        }

        internal string GetString(DataCenterAddress address)
        {
            return _strings.GetOrAdd(address, a =>
            {
                var str = _stringRegion.GetReader(address).ReadString();

                return _intern ? string.Intern(str) : str;
            });
        }

        static DataCenterHeader ReadHeader(GameBinaryReader reader)
        {
            var unk1 = reader.ReadInt32();
            var unk2 = reader.ReadInt32();

            if (unk2 != 0)
                throw new InvalidDataException();

            var unk3 = reader.ReadInt32();
            var version = reader.ReadUInt32();
            var unk4 = reader.ReadInt32();

            if (unk4 != 0)
                throw new InvalidDataException();

            var unk5 = reader.ReadInt32();

            if (unk5 != 0)
                throw new InvalidDataException();

            var unk6 = reader.ReadInt32();

            if (unk6 != 0)
                throw new InvalidDataException();

            var unk7 = reader.ReadInt32();

            if (unk7 != 0)
                throw new InvalidDataException();

            return new DataCenterHeader(unk1, unk2, unk3, version, unk4, unk5, unk6, unk7);
        }

        static DataCenterFooter ReadFooter(GameBinaryReader reader)
        {
            var unk1 = reader.ReadInt32();

            if (unk1 != 0)
                throw new InvalidDataException();

            return new DataCenterFooter(unk1);
        }

        static DataCenterSimpleRegion ReadSimpleRegion(GameBinaryReader reader, bool offByOne,
            uint elementSize)
        {
            var count = reader.ReadUInt32();

            if (offByOne)
                count--;

            var data = reader.ReadBytes((int)(count * elementSize));

            return new DataCenterSimpleRegion(elementSize, count, data);
        }

        static DataCenterSimpleSegmentedRegion ReadSimpleSegmentedRegion(GameBinaryReader reader,
            uint count, uint elementSize)
        {
            var segments = new List<DataCenterSimpleRegion>();

            for (var i = 0; i < count; i++)
                segments.Add(ReadSimpleRegion(reader, false, elementSize));

            return new DataCenterSimpleSegmentedRegion(elementSize, segments);
        }

        static DataCenterSegment ReadSegment(GameBinaryReader reader, uint elementSize)
        {
            var full = reader.ReadUInt32();
            var used = reader.ReadUInt32();
            var data = reader.ReadBytes((int)(full * elementSize));

            return new DataCenterSegment(elementSize, full, used, data);
        }

        static DataCenterSegmentedRegion ReadSegmentedRegion(GameBinaryReader reader, uint elementSize)
        {
            var count = reader.ReadUInt32();
            var segments = new List<DataCenterSegment>((int)count);

            for (var i = 0; i < count; i++)
                segments.Add(ReadSegment(reader, elementSize));

            return new DataCenterSegmentedRegion(elementSize, segments);
        }

        internal static DataCenterAddress ReadAddress(GameBinaryReader reader)
        {
            var segment = reader.ReadUInt16();
            var element = reader.ReadUInt16();

            return new DataCenterAddress(segment, element);
        }

        static IEnumerable<DataCenterAddress> ReadAddresses(DataCenterSimpleRegion region)
        {
            var reader = region.GetReader(0);

            for (var i = 0; i < region.Count; i++)
                yield return ReadAddress(reader);
        }

        static string ReadString(DataCenterSegmentedRegion region, DataCenterAddress address)
        {
            return region.GetReader(address).ReadString();
        }
    }
}
