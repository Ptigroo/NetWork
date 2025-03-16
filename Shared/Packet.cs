// -----------------------------------------------------------------------
// <copyright>
//     Author: Gaspard de Villenfagne de Vogelsank - Nicolas Denoël
//     Description: The Packet class is the backbone of our lightweight TCP protocol,
//      enabling data to be sent and received over UDP. It mimics key features of TCP
//      by defining the structure of each unit of communication (a "packet")
// </copyright>
// -----------------------------------------------------------------------


using System.Net;

namespace Shared;

public class Packet
{
    /// <summary>
    ///     Represents a custom lightweight TCP-like packet used for communication over UDP.
    ///     Provides serialization and deserialization methods for sending and receiving data.
    /// </summary>
    /// <summary>
    ///     Total size of the data payload in bytes (16 bits).
    /// </summary>
    public ushort TotalDataSize { get; set; }

    /// <summary>
    ///     Sequence number of the packet (16 bits), used for ordering packets.
    /// </summary>
    public ushort SequenceNumber { get; set; }

    /// <summary>
    ///     Synchronization flag (1 bit), used to initiate a connection.
    /// </summary>
    public bool SYN { get; set; }

    /// <summary>
    ///     Acknowledgment flag (1 bit), used to acknowledge receipt of a packet.
    /// </summary>
    public bool ACK { get; set; }

    /// <summary>
    ///     Finish flag (1 bit), used to terminate a connection.
    /// </summary>
    public bool FIN { get; set; }

    /// <summary>
    ///     Reset flag (1 bit), used to reset a connection.
    /// </summary>
    public bool RST { get; set; }

    /// <summary>
    ///     The data payload of the packet (variable length).
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    ///     Serializes the Packet object into a byte array for network transmission.
    /// </summary>
    /// <returns>A byte array representing the serialized packet.</returns>
    public byte[] Serialize()
    {
        using (var ms = new MemoryStream())
        {
            // Write TotalDataSize (big-endian)
            ms.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)TotalDataSize)), 0, 2);

            // Write SequenceNumber (big-endian)
            ms.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)SequenceNumber)), 0, 2);

            // Combine flags into one byte
            byte flags = 0;
            if (SYN) flags |= 1 << 3; // 4th bit
            if (ACK) flags |= 1 << 2; // 3rd bit
            if (FIN) flags |= 1 << 1; // 2nd bit
            if (RST) flags |= 1 << 0; // 1st bit
            ms.WriteByte(flags);

            // Write Data if present
            if (Data != null && Data.Length > 0) ms.Write(Data, 0, Data.Length);

            return ms.ToArray();
        }
    }

    /// <summary>
    ///     Deserializes a byte array into a Packet object.
    /// </summary>
    /// <param name="bytes">The byte array representing the serialized packet.</param>
    /// <returns>A Packet object reconstructed from the byte array.</returns>
    public static Packet Deserialize(byte[] bytes)
    {
        var packet = new Packet();

        using (var ms = new MemoryStream(bytes))
        {
            var reader = new BinaryReader(ms);

            // Read TotalDataSize (big-endian)
            packet.TotalDataSize = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

            // Read SequenceNumber (big-endian)
            packet.SequenceNumber = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

            // Read Flags
            var flags = reader.ReadByte();
            packet.SYN = (flags & (1 << 3)) != 0; // 4th bit
            packet.ACK = (flags & (1 << 2)) != 0; // 3rd bit
            packet.FIN = (flags & (1 << 1)) != 0; // 2nd bit
            packet.RST = (flags & (1 << 0)) != 0; // 1st bit

            // Read Data
            var dataLength = bytes.Length - 5; // Subtract header size (2+2+1 bytes)
            if (dataLength > 0)
                packet.Data = reader.ReadBytes(dataLength);
            else
                packet.Data = new byte[0];
        }

        return packet;
    }
}