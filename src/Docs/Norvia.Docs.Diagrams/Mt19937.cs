namespace Norvia.Docs.Diagrams;

/// <summary>
/// The Mersenne Twister, seeded and drawn from the way the former Python generators did (<c>random.Random(seed).randint(1, 2**31 - 1)</c>),
/// so that the seeds written into the Excalidraw elements stay the same and a regenerated diagram does not change.
/// </summary>
public sealed class Mt19937
{
    private const int N = 624;
    private const int M = 397;
    private readonly uint[] _state = new uint[N];
    private int _index;

    public Mt19937(uint seed)
    {
        InitGenrand(19650218);
        var i = 1;
        var j = 0;
        for (var k = N; k > 0; k--)
        {
            _state[i] = (_state[i] ^ ((_state[i - 1] ^ (_state[i - 1] >> 30)) * 1664525)) + seed + (uint)j;
            i++;
            j++;
            if (i >= N)
            {
                _state[0] = _state[N - 1];
                i = 1;
            }

            if (j >= 1)
            {
                j = 0;
            }
        }

        for (var k = N - 1; k > 0; k--)
        {
            _state[i] = (_state[i] ^ ((_state[i - 1] ^ (_state[i - 1] >> 30)) * 1566083941)) - (uint)i;
            i++;
            if (i >= N)
            {
                _state[0] = _state[N - 1];
                i = 1;
            }
        }

        _state[0] = 0x80000000;
        _index = N;
    }

    /// <summary>A whole number from 1 to 2^31 - 1, both included.</summary>
    public int NextSeed()
    {
        const uint width = int.MaxValue; // 2^31 - 1 values, 31 bits each draw
        uint r;
        do
        {
            r = NextUInt32() >> 1;
        }
        while (r >= width);

        return (int)(1 + r);
    }

    private void InitGenrand(uint seed)
    {
        _state[0] = seed;
        for (var i = 1; i < N; i++)
        {
            _state[i] = (1812433253 * (_state[i - 1] ^ (_state[i - 1] >> 30))) + (uint)i;
        }
    }

    private uint NextUInt32()
    {
        if (_index >= N)
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
        for (var k = 0; k < N; k++)
        {
            var y = (_state[k] & 0x80000000) | (_state[(k + 1) % N] & 0x7fffffff);
            _state[k] = _state[(k + M) % N] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0df : 0);
        }

        _index = 0;
    }
}
