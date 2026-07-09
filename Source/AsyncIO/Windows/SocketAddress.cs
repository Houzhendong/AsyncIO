using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace AsyncIO.Windows
{
  class SocketAddress : IDisposable
  {
    private byte[] m_buffer;
    private AddressFamily m_addressFamily;

    private bool m_disposed = false;
    private GCHandle m_bufferHandle;
    private IntPtr m_bufferAddress;

    // sizeof(sun_path) in sockaddr_un is 108, leaving room for the zero terminator.
    private const int UnixMaxPathBytes = 107;
    private const int UnixPathOffset = 2;
    private const int UnixSocketAddressSize = 110; // 2 (sun_family) + 108 (sun_path)

    public SocketAddress(AddressFamily addressFamily, int size)
    {
      Size = size;
      BindSize = size;
      m_addressFamily = addressFamily;
      m_buffer = new byte[size];

      m_buffer = new byte[(size / IntPtr.Size + 2) * IntPtr.Size];
      m_buffer[0] = (byte)addressFamily;
      m_buffer[1] = (byte)((uint)addressFamily >> 8);

      m_bufferHandle = GCHandle.Alloc(m_buffer, GCHandleType.Pinned);
      m_bufferAddress = Marshal.UnsafeAddrOfPinnedArrayElement(m_buffer, 0);
    }

    /// <summary>
    /// Builds a sockaddr_un for the given Unix Domain Socket path.
    /// </summary>
    public SocketAddress(string unixPath)
      : this(AddressFamily.Unix, UnixSocketAddressSize)
    {
      if (unixPath == null)
        throw new ArgumentNullException("unixPath");

      byte[] pathBytes = Encoding.UTF8.GetBytes(unixPath);

      if (pathBytes.Length > UnixMaxPathBytes)
        throw new ArgumentException("Path is too long for a unix domain socket address (max " + UnixMaxPathBytes + " UTF8 bytes).", "unixPath");

      for (int i = 0; i < pathBytes.Length; i++)
        m_buffer[UnixPathOffset + i] = pathBytes[i];

      m_buffer[UnixPathOffset + pathBytes.Length] = 0;
    }

    /// <summary>
    /// Creates an unnamed AF_UNIX address, used for autobind before ConnectEx.
    /// The initial BindSize is 2 (just sun_family); callers should retry a
    /// failed bind with the full sockaddr_un size (see BindSize).
    /// </summary>
    public static SocketAddress CreateUnixUnnamed()
    {
      var address = new SocketAddress(AddressFamily.Unix, UnixSocketAddressSize);
      address.BindSize = 2;
      return address;
    }

    public SocketAddress(IPAddress ipAddress)
      : this(ipAddress.AddressFamily, ipAddress.AddressFamily == AddressFamily.InterNetwork ? 16 : 28)
    {
      this.m_buffer[2] = (byte)0;
      this.m_buffer[3] = (byte)0;
      if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
      {
        this.m_buffer[4] = (byte)0;
        this.m_buffer[5] = (byte)0;
        this.m_buffer[6] = (byte)0;
        this.m_buffer[7] = (byte)0;
        long scopeId = ipAddress.ScopeId;
        this.m_buffer[24] = (byte)scopeId;
        this.m_buffer[25] = (byte)(scopeId >> 8);
        this.m_buffer[26] = (byte)(scopeId >> 16);
        this.m_buffer[27] = (byte)(scopeId >> 24);
        byte[] addressBytes = ipAddress.GetAddressBytes();
        for (int index = 0; index < addressBytes.Length; ++index)
          this.m_buffer[8 + index] = addressBytes[index];
      }
      else
      {
        System.Buffer.BlockCopy(ipAddress.GetAddressBytes(), 0, m_buffer, 4, 4);
      }
    }

    public IPEndPoint GetEndPoint()
    {
      return new IPEndPoint(GetIPAddress(), (int)Buffer[2] << 8 & 65280 | (int)Buffer[3]);
    }

    /// <summary>
    /// Reads back the path from a sockaddr_un buffer. UDS code paths
    /// generally never need this (they don't call GetEndPoint), but it is
    /// provided for completeness.
    /// </summary>
    public string GetUnixPath()
    {
      int length = 0;

      while (UnixPathOffset + length < Size && Buffer[UnixPathOffset + length] != 0)
        length++;

      return Encoding.UTF8.GetString(Buffer, UnixPathOffset, length);
    }

    internal IPAddress GetIPAddress()
    {
      if (m_addressFamily == AddressFamily.InterNetworkV6)
      {
        byte[] address = new byte[16];
        for (int index = 0; index < address.Length; ++index)
          address[index] = this.Buffer[index + 8];
        long scopeid = (long)(((int)this.Buffer[27] << 24) + ((int)this.Buffer[26] << 16) + ((int)this.Buffer[25] << 8) + (int)this.Buffer[24]);
        return new IPAddress(address, scopeid);
      }
      else if (m_addressFamily == AddressFamily.InterNetwork)
        return new IPAddress((long)((int)this.Buffer[4] & (int)byte.MaxValue | (int)this.Buffer[5] << 8 & 65280 | (int)this.Buffer[6] << 16 & 16711680 | (int)this.Buffer[7] << 24) & (long)uint.MaxValue);
      else
        throw new SocketException((int)SocketError.AddressFamilyNotSupported);
    }

    public SocketAddress(IPAddress ipAddress, int port)
      : this(ipAddress)
    {
      this.m_buffer[2] = (byte)(port >> 8);
      this.m_buffer[3] = (byte)port;
    }

    public int Size { get; private set; }

    /// <summary>
    /// The size to pass to bind(). Normally equal to Size, but for an unnamed
    /// AF_UNIX address this can be set to 2 (just sun_family) for an initial
    /// attempt, falling back to the full Size on failure.
    /// </summary>
    public int BindSize { get; set; }

    public IntPtr PinnedAddressBuffer
    {
      get { return m_bufferAddress; }
    }

    public void Dispose()
    {
      if (!m_disposed)
      {
        m_disposed = true;
        m_bufferHandle.Free();
      }
    }

    public byte[] Buffer { get { return m_buffer; } }
  }
}
