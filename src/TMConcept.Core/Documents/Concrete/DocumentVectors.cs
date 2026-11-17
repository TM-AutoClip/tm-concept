using System.Buffers.Binary;

namespace TMConcept.Core.Documents;

/// <summary>A vector as stored (little-endian floats) and back.</summary>
public static class DocumentVectors
{
    /// <summary>The stored bytes.</summary>
    public static byte[] ToBytes(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        var bytes = new byte[vector.Length * 4];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), vector[i]);
        }

        return bytes;
    }

    /// <summary>The vector of stored bytes.</summary>
    public static float[] FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var vector = new float[bytes.Length / 4];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }

        return vector;
    }
}
