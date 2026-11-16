namespace TMConcept.Speech.AhenkCep;

/// <summary>
/// The starting noise of a piece: PyTorch's CPU <c>torch.randn</c> from a seed (mt19937 seeded with the low 32 bits, 24-bit uniforms,
/// then blocks of 16 turned into normals by Box-Muller, value j paired with j + 8, in single precision), as Ahenk Cep's own engine does
/// (site/js/randn.js, Apache-2.0), so a seed gives the same voice here as there. Piece i of a scene uses the seed
/// (seed × 1 000 003 + i) mod 2^63.
/// </summary>
internal sealed class AhenkCepNoise
{
    private readonly uint[] _state = new uint[624];
    private int _index;

    /// <summary>A generator seeded with the low 32 bits of <paramref name="seed"/>.</summary>
    public AhenkCepNoise(ulong seed)
    {
        _state[0] = (uint)(seed & 0xffffffff);
        for (uint j = 1; j < 624; j++)
        {
            var previous = _state[j - 1] ^ (_state[j - 1] >> 30);
            _state[j] = unchecked((1812433253u * previous) + j);
        }

        _index = 624;
    }

    /// <summary>The seed of piece <paramref name="piece"/> of a scene spoken with <paramref name="seed"/>.</summary>
    public static ulong PieceSeed(ulong seed, int piece) =>
        (ulong)(((System.Numerics.BigInteger)seed * 1_000_003 + piece) % (System.Numerics.BigInteger.One << 63));

    /// <summary>The first <paramref name="count"/> values (a multiple of 16) of torch.randn with this seed.</summary>
    public static float[] Normal(ulong seed, int count)
    {
        if (count % 16 != 0)
        {
            throw new ArgumentException("The count must be a multiple of 16.", nameof(count));
        }

        var generator = new AhenkCepNoise(seed);
        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (generator.Next() & 0xffffff) * 5.9604644775390625e-8f;
        }

        for (var block = 0; block < count; block += 16)
        {
            for (var j = 0; j < 8; j++)
            {
                var u1 = 1f - values[block + j];
                var u2 = values[block + j + 8];
                var radius = (float)Math.Sqrt((float)(-2f * (float)Math.Log(u1)));
                var theta = (float)(2 * Math.PI * u2);
                values[block + j] = radius * (float)Math.Cos(theta);
                values[block + j + 8] = radius * (float)Math.Sin(theta);
            }
        }

        return values;
    }

    /// <summary>The next 32-bit value of mt19937.</summary>
    public uint Next()
    {
        if (_index >= 624)
        {
            Twist();
        }

        var y = _state[_index++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680;
        y ^= (y << 15) & 0xefc60000;
        y ^= y >> 18;
        return y;
    }

    private void Twist()
    {
        for (var i = 0; i < 624; i++)
        {
            var y = (_state[i] & 0x80000000) | (_state[(i + 1) % 624] & 0x7fffffff);
            var value = _state[(i + 397) % 624] ^ (y >> 1);
            if ((y & 1) != 0)
            {
                value ^= 0x9908b0df;
            }

            _state[i] = value;
        }

        _index = 0;
    }
}
