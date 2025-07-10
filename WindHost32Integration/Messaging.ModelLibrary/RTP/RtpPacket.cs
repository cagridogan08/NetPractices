using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.Rtp;

/// <summary>
/// RTP packet structure for real-time message transport
/// </summary>
public class RtpPacket
{
    public const int MinHeaderSize = 12;
    public const byte Version = 2;

    public byte V { get; set; } = Version;           // Version (2 bits)
    public bool P { get; set; }                      // Padding (1 bit)
    public bool X { get; set; }                      // Extension (1 bit)
    public byte CC { get; set; }                     // CSRC count (4 bits)
    public bool M { get; set; }                      // Marker (1 bit)
    public byte PT { get; set; } = 96;               // Payload type (7 bits) - Dynamic payload type for custom data
    public ushort SequenceNumber { get; set; }      // Sequence number (16 bits)
    public uint Timestamp { get; set; }             // Timestamp (32 bits)
    public uint SSRC { get; set; }                  // Synchronization source (32 bits)
    public uint[]? CSRC { get; set; }               // Contributing sources
    public byte[]? Extension { get; set; }          // Extension data
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Serializes the RTP packet to a byte array
    /// </summary>
    public byte[] ToBytes()
    {
        var headerSize = MinHeaderSize + (CC * 4) + (X ? (Extension?.Length ?? 0) + 4 : 0);
        var totalSize = headerSize + Payload.Length;
        var buffer = new byte[totalSize];

        // First byte: V(2) + P(1) + X(1) + CC(4)
        buffer[0] = (byte)((V << 6) | (P ? 0x20 : 0) | (X ? 0x10 : 0) | CC);

        // Second byte: M(1) + PT(7)
        buffer[1] = (byte)((M ? 0x80 : 0) | PT);

        // Sequence number (16 bits)
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), SequenceNumber);

        // Timestamp (32 bits)
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), Timestamp);

        // SSRC (32 bits)
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8), SSRC);

        var offset = 12;

        // CSRC list
        if (CSRC != null)
        {
            for (int i = 0; i < CC; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), CSRC[i]);
                offset += 4;
            }
        }

        // Extension header
        if (X && Extension != null)
        {
            // Extension identifier (16 bits) + length (16 bits)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), 0x1234); // Custom extension ID
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset + 2), (ushort)(Extension.Length / 4));
            offset += 4;

            Extension.CopyTo(buffer, offset);
            offset += Extension.Length;
        }

        // Payload
        Payload.CopyTo(buffer, offset);

        return buffer;
    }

    /// <summary>
    /// Deserializes an RTP packet from a byte array
    /// </summary>
    public static RtpPacket FromBytes(byte[] data)
    {
        if (data.Length < MinHeaderSize)
            throw new ArgumentException("Data too short for RTP packet");

        var packet = new RtpPacket();

        // Parse first byte
        packet.V = (byte)((data[0] >> 6) & 0x03);
        packet.P = (data[0] & 0x20) != 0;
        packet.X = (data[0] & 0x10) != 0;
        packet.CC = (byte)(data[0] & 0x0F);

        // Parse second byte
        packet.M = (data[1] & 0x80) != 0;
        packet.PT = (byte)(data[1] & 0x7F);

        // Parse sequence number, timestamp, and SSRC
        packet.SequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        packet.Timestamp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        packet.SSRC = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));

        var offset = 12;

        // Parse CSRC list
        if (packet.CC > 0)
        {
            packet.CSRC = new uint[packet.CC];
            for (int i = 0; i < packet.CC; i++)
            {
                packet.CSRC[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
                offset += 4;
            }
        }

        // Parse extension
        if (packet.X)
        {
            if (offset + 4 > data.Length)
                throw new ArgumentException("Invalid extension header");

            var extensionLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2)) * 4;
            offset += 4;

            if (offset + extensionLength > data.Length)
                throw new ArgumentException("Invalid extension length");

            packet.Extension = new byte[extensionLength];
            Array.Copy(data, offset, packet.Extension, 0, extensionLength);
            offset += extensionLength;
        }

        // Parse payload
        var payloadLength = data.Length - offset;
        if (payloadLength > 0)
        {
            packet.Payload = new byte[payloadLength];
            Array.Copy(data, offset, packet.Payload, 0, payloadLength);
        }

        return packet;
    }
}

/// <summary>
/// Utility class for converting between Message objects and RTP packets
/// </summary>
public static class RtpMessageConverter
{
    /// <summary>
    /// Converts a Message to an RTP packet
    /// </summary>
    public static RtpPacket MessageToRtpPacket(Message message, ushort sequenceNumber, uint ssrc)
    {
        var messageJson = JsonSerializer.Serialize(message);
        var payload = Encoding.UTF8.GetBytes(messageJson);

        return new RtpPacket
        {
            SequenceNumber = sequenceNumber,
            Timestamp = (uint)((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds(),
            SSRC = ssrc,
            Payload = payload,
            M = true, // Mark as important message
            PT = GetPayloadType(message.Type)
        };
    }

    /// <summary>
    /// Converts an RTP packet to a Message
    /// </summary>
    public static Message RtpPacketToMessage(RtpPacket packet)
    {
        var messageJson = Encoding.UTF8.GetString(packet.Payload);
        var message = JsonSerializer.Deserialize<Message>(messageJson);

        if (message == null)
            throw new InvalidOperationException("Failed to deserialize message from RTP packet");

        // Update timestamp from RTP header if needed
        message.Timestamp = DateTimeOffset.FromUnixTimeSeconds(packet.Timestamp).DateTime;

        return message;
    }

    /// <summary>
    /// Gets the RTP payload type based on message type
    /// </summary>
    private static byte GetPayloadType(MessageType messageType)
    {
        return messageType switch
        {
            MessageType.Text => 96,      // Dynamic payload type for text
            MessageType.Error => 97,     // Dynamic payload type for errors
            MessageType.Handshake => 98, // Dynamic payload type for handshake
            _ => 99                      // Default dynamic payload type
        };
    }

    /// <summary>
    /// Gets the message type from RTP payload type
    /// </summary>
    public static MessageType GetMessageType(byte payloadType)
    {
        return payloadType switch
        {
            96 => MessageType.Text,
            97 => MessageType.Error,
            98 => MessageType.Handshake,
            _ => MessageType.Text
        };
    }
}