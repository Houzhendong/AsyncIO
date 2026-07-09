using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AsyncIO
{
    /// <summary>
    /// Represents a Unix Domain Socket (AF_UNIX) endpoint as a filesystem path.
    /// Serializes to a sockaddr_un structure (family = 1, followed by a
    /// zero-terminated UTF8 path, 108 bytes of path storage).
    /// </summary>
    public sealed class UnixEndPoint : EndPoint
    {
        // The real native sun_path size is platform-dependent: 108 bytes on Linux and
        // Windows, but only 104 on macOS/BSD. This check is applied on every platform
        // (this type is constructed unconditionally, regardless of OS), so use the
        // lowest common denominator - the macOS/BSD limit, minus the zero terminator -
        // rather than the Linux/Windows-only value, so a path validated here never
        // fails later when it is actually bound/connected on macOS.
        private const int MaxPathBytes = 103;
        private const int PathOffset = 2;
        private const int SocketAddressSize = 110; // 2 (sun_family) + 108 (sun_path)

        private readonly string m_path;

        public UnixEndPoint(string path)
        {
            if (path == null)
                throw new ArgumentNullException("path");

            if (path.Length == 0)
                throw new ArgumentException("Path cannot be empty.", "path");

            if (Encoding.UTF8.GetByteCount(path) > MaxPathBytes)
                throw new ArgumentException("Path is too long for a unix domain socket address (max " + MaxPathBytes + " UTF8 bytes).", "path");

            m_path = path;
        }

        public string Path
        {
            get { return m_path; }
        }

        public override AddressFamily AddressFamily
        {
            get { return AddressFamily.Unix; }
        }

        public override SocketAddress Serialize()
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(m_path);

            var socketAddress = new SocketAddress(AddressFamily.Unix, SocketAddressSize);

            // The SocketAddress constructor already writes the family (1, little-endian)
            // at offsets 0-1; write the path followed by a zero terminator.
            for (int i = 0; i < pathBytes.Length; i++)
                socketAddress[PathOffset + i] = pathBytes[i];

            socketAddress[PathOffset + pathBytes.Length] = 0;

            return socketAddress;
        }

        public override EndPoint Create(SocketAddress socketAddress)
        {
            if (socketAddress == null)
                throw new ArgumentNullException("socketAddress");

            if (socketAddress.Family != AddressFamily.Unix)
                throw new ArgumentException("Socket address is not a unix domain socket address.", "socketAddress");

            int length = 0;

            while (PathOffset + length < socketAddress.Size && socketAddress[PathOffset + length] != 0)
                length++;

            if (length == 0)
                throw new ArgumentException("Socket address contains an empty path.", "socketAddress");

            byte[] pathBytes = new byte[length];
            for (int i = 0; i < length; i++)
                pathBytes[i] = socketAddress[PathOffset + i];

            return new UnixEndPoint(Encoding.UTF8.GetString(pathBytes, 0, length));
        }

        public override string ToString()
        {
            return m_path;
        }

        public override bool Equals(object obj)
        {
            var other = obj as UnixEndPoint;
            return other != null && other.m_path == m_path;
        }

        public override int GetHashCode()
        {
            return m_path.GetHashCode();
        }
    }
}
